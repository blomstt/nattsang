using Nattsang.Core;
using Nattsang.Playback;
using static Nattsang.Gui.Ui;

namespace Nattsang.Gui;

// The window: the library at the left, now playing with lyrics or the queue
// at the right, the dock below them and the key bar at the bottom; the × at
// the top right quits. Too small for all of it, parts hide (Fit.cs). Nothing
// runs while it's paused: the clock, the progress bar and the visualizer only
// tick while something plays.
//
// It plays through the helper when there is one (Helper.cs), the library
// coming from the account through it too, the lyrics from LRCLIB and the
// visualizer's levels from the music; and the sample album on a simulated
// clock when there isn't, or with --simulate (placeholder lyrics, the
// visualizer's own motion, nothing asked of the network). Either way the
// desktop can see and steer it over MPRIS (Mpris.cs).
sealed unsafe partial class App
{
    // One window at a time: starting it again shows the one that's open.
    // NATTSANG_APP_ID names another, to run a second one beside it.
    public const string Id = "dev.blomst.Nattsang";

    Palette palette; // read again when the colours file changes (Recolor)
    readonly IPlayback player;
    readonly ILibrary lists; // the library's lists
    readonly Helper? helper;
    readonly LyricsSource? lyricsSource;
    readonly LevelFeed? levelFeed;
    readonly Http? http;
    // The volume and the widths, as they were left last time (Remembered).
    readonly Remembered remembered = Remembered.Load(Remembered.FilePath());
    readonly Volume volumeLevel;
    MprisBus? mpris;
    bool animations;
    nint app, window;
    bool secondWindow; // another window beside the usual one (NATTSANG_APP_ID)
    bool vizOn = true;

    App(Palette palette, IPlayback player, ILibrary library, Helper? helper, Navigation nav,
        LyricsSource? lyrics = null, LevelFeed? levels = null, Http? http = null)
    {
        this.palette = palette;
        this.player = player;
        this.lists = library;
        this.helper = helper;
        this.nav = nav;
        lyricsSource = lyrics;
        levelFeed = levels;
        this.http = http;
        volumeLevel = new(remembered.Volume);
        playlistWidth = remembered.PlaylistWidth;
    }

    public static int Run(string[] args)
    {
        if (N.gtk_get_minor_version() < 12)
        {
            Console.Error.WriteLine("nattsang needs GTK 4.12 or newer.");
            return 1;
        }
        var path = args.Contains("--simulate") ? null : Helper.Find();
        if (path == null && !args.Contains("--simulate"))
            Console.Error.WriteLine("nattsang: no nattsang-helper beside the binary, so it plays the sample album (the README says how to build it).");
        App a;
        if (path == null)
        {
            // The design's picture: Meteora open under albums, Numb playing.
            var library = new SampleLibrary();
            var album = library.Open(Sample.AlbumUri, Sample.Album);
            var player = new SimulatedPlayback(new Order(new Queue(Sample.Queue)), album, Sample.Playing, Sample.Position, true, () => Clock);
            var nav = new Navigation(Keys.Albums);
            nav.Open(Sample.AlbumUri, Sample.Album);
            a = new App(Palette.Load(), player, library, null, nav);
        }
        else
        {
            var helper = new Helper(path);
            var cache = LyricsCache.Default() is { } dir ? new LyricsCache(dir) : null;
            var http = new Http(Lrclib.UserAgent);
            var lyrics = new LyricsSource(cache, http.Get);
            a = new App(Palette.Load(), new SpotifyPlayback(helper.Send, () => Clock, new Order(new Queue())),
                new SpotifyLibrary(helper.Send), helper, new Navigation(Keys.Albums), lyrics, new LevelFeed(helper.Send), http);
        }
        N.g_set_application_name("Nattsang");
        var id = Environment.GetEnvironmentVariable("NATTSANG_APP_ID");
        a.secondWindow = !string.IsNullOrEmpty(id) && id != Id;
        a.app = N.gtk_application_new(a.secondWindow ? id! : Id, 0);
        On(a.app, "activate", a.Activate);
        On(a.app, "shutdown", a.Closed);
        return N.g_application_run(a.app, 0, 0);
    }

    void Activate()
    {
        if (window != 0) { N.gtk_window_present(window); return; }
        animations = Animations;
        css = N.gtk_css_provider_new();
        N.gtk_css_provider_load_from_string(css, Style.Css(palette));
        N.gtk_style_context_add_provider_for_display(N.gdk_display_get_default(), css, 801);

        window = Css(N.gtk_application_window_new(app), "nattsang");
        N.gtk_window_set_title(window, "Nattsang");
        N.gtk_window_set_decorated(window, false);

        // The library takes the spare width (flex 999 1 440px against 1 1 480px);
        // too narrow for both, now playing hides (App.Fit).
        library = BuildLibrary();
        nowPanel = BuildNowPlaying();
        var top = Wrap.Row(Spacing, 16, Wrap.Align.Stretch);
        N.gtk_widget_set_vexpand(top, true);
        Add(top, Wrap.Item(library, 999, LibraryBasis), Wrap.Item(nowPanel, 1, NowBasis));
        var main = Add(Box(Vertical, Spacing, "main"), top, BuildDock());
        N.gtk_widget_set_vexpand(main, true);

        // The × sits over everything, in the top border at the right.
        var layers = N.gtk_overlay_new();
        N.gtk_overlay_set_child(layers, Add(Box(Vertical, Spacing), main, BuildKeyBar()));
        var close = Button("×", "Close", Quit, "close");
        N.gtk_widget_set_halign(close, End);
        N.gtk_widget_set_valign(close, Start);
        N.gtk_overlay_add_overlay(layers, close);
        N.gtk_window_set_child(window, Fit(layers));

        OnKey(window, Key);
        On(window, "destroy", Closed);
        player.Changed += Sync;
        player.Order.Queue.Changed += QueueChanged;
        player.Order.Changed += OrderChanged;
        lists.Changed += ListChanged;
        volumeLevel.Changed += VolumeChanged;
        QueueChanged();
        NextChanged();
        Sync();
        // The helper first: the lists are asked of it. The levels, 30 lines
        // a second while playing, go straight to the visualizer.
        if (helper != null && player is SpotifyPlayback spotify && lists is SpotifyLibrary account && levelFeed is { } feed)
        {
            helper.Start(line =>
            {
                if (line.StartsWith("levels\t", StringComparison.Ordinal)) { feed.Receive(line); return; }
                spotify.Receive(line);
                account.Receive(line);
                feed.Receive(line);
            }, why => { spotify.Ended(why); account.Ended(why); feed.Ended(); });
            feed.Changed += LevelsChanged;
            WantLevels();
        }
        player.SetVolume(volumeLevel.Gain); // after Start: the helper hears nothing before
        WatchColors();
        ShowHere();
        // It opens wide enough for everything, the key bar on one line.
        var open = Need(Room.Part.None, -1);
        N.gtk_window_set_default_size(window, open.Width, Math.Max(760, open.Height));
        N.gtk_window_present(window);
        FocusList();
        // Again once the list has its size, so it scrolls to the playing track.
        var frames = 0;
        Ticking(window, _ =>
        {
            if (++frames < 2) return true;
            FocusList();
            return false;
        });
        // Parts fade as they hide and show once it has its first size: a
        // compositor may give it the size it last had a few frames in.
        After(500, () => fading = true);
        mpris = MprisBus.Start(app, player, volumeLevel, () => N.gtk_window_present(window), Quit, secondWindow);
    }

    // The ×: everything closes, through shutdown (Closed).
    void Quit() => N.g_application_quit(app);

    // The window is gone, or the app is: nothing may touch its widgets after,
    // and the helper goes with it.
    void Closed()
    {
        mpris?.Stop();
        mpris = null;
        helper?.Stop();
        Cancel(ref clock);
        if (rememberLater != 0) { Cancel(ref rememberLater); remembered.Save(Remembered.FilePath()); }
        Cancel(ref recolorLater);
        Cancel(ref coverLater);
        player.Changed -= Sync;
        player.Order.Queue.Changed -= QueueChanged;
        player.Order.Changed -= OrderChanged;
        lists.Changed -= ListChanged;
        volumeLevel.Changed -= VolumeChanged;
        if (levelFeed != null) levelFeed.Changed -= LevelsChanged;
    }

    // ---- keys ----------------------------------------------------------------

    const uint Space = 0x20, Slash = 0x2f, KeyA = 0x61, KeyN = 0x6e, KeyO = 0x6f, KeyP = 0x70, KeyQ = 0x71, KeyR = 0x72, KeyV = 0x76;
    const uint Plus = 0x2b, Minus = 0x2d, KpAdd = 0xffab, KpSubtract = 0xffad;
    const uint Escape = 0xff1b, Return = 0xff0d, KpEnter = 0xff8d, BackSpace = 0xff08;
    const uint Modifiers = 4 | 8 | 1 << 26 | 1 << 27 | 1 << 28; // Control, Alt, Super, Hyper, Meta

    bool Key(uint keyval, uint state)
    {
        if ((state & Modifiers) != 0) return false;
        if (Searching)
        {
            // Typing goes to the search; Enter searches the songs, and it and
            // Escape go back to the list.
            if (keyval is not (Escape or Return or KpEnter)) return false;
            if (keyval != Escape) Search();
            FocusList();
            return true;
        }
        switch (N.gdk_keyval_to_lower(keyval)) // Caps Lock gives upper case
        {
            case Slash: N.gtk_entry_grab_focus_without_selecting(search); return true;
            case Space: player.Toggle(); return true;
            case KeyN: player.Next(); return true;
            case KeyP: player.Previous(); return true;
            case KeyA: AddSelected(); return true;
            case KeyO: OpenAlbum(); return true;
            case KeyR: OpenArtist(); return true;
            case BackSpace: Back(); return true;
            case KeyQ: ShowQueue(!showingQueue); return true;
            case KeyV: ToggleViz(); return true;
            case Plus or KpAdd: volumeLevel.Up(); return true;
            case Minus or KpSubtract: volumeLevel.Down(); return true;
        }
        return false;
    }

    // ---- playback ------------------------------------------------------------

    Track? shownTrack;
    Status? shownStatus;
    int shownSecond = -1;
    double lastPosition;
    uint clock;

    // The player changed: a new track or album, play or pause, a jump, a status.
    void Sync()
    {
        var t = player.Track;
        var pos = player.Position;
        var first = shownTrack == null;
        var newTrack = t != shownTrack;
        var none = t.Uri.Length == 0;
        if (newTrack)
        {
            shownTrack = t;
            N.gtk_label_set_text(total, Time.Format(t.Seconds));
            N.gtk_label_set_text(queueNowTitle, none ? "" : $"{t.Title} · {t.Artist}");
            N.gtk_label_set_text(queueNowTime, Time.Format(t.Seconds));
            MarkCurrentRow();
            LyricsForTrack(t, pos, first);
            MoreOfPlaying();
        }
        ShowCover(player.Cover);
        // While the player can't play (signing in, an error), its status
        // stands where the track's title goes; with the now playing panel
        // hidden, it isn't shown anywhere.
        var status = player.Status;
        if (newTrack || status != shownStatus)
        {
            shownStatus = status;
            N.gtk_label_set_text(song, status?.Title ?? t.Title);
            N.gtk_label_set_text(sub, status?.Detail ?? t.Artist);
            ShowContext();
        }
        // A new track, or the same one back towards its start, slides the bar.
        if (!first && (newTrack || pos + 0.5 < lastPosition)) ProgressJump();
        N.gtk_button_set_label(playButton, player.Playing ? "⏸" : "▶");
        Name(playButton, player.Playing ? "Pause" : "Play");
        ShowTime(pos);
        lastPosition = pos;
        Schedule();
        Motion();
    }

    // While playing, the clock wakes at the next whole second (the elapsed
    // time), the next synced lyric line or the next half pixel of the
    // progress bar, whichever comes first; paused, it doesn't run.
    void Schedule()
    {
        Cancel(ref clock);
        if (!player.Playing) return;
        var pos = player.Position;
        var toSecond = Math.Floor(pos) + 1 - pos;
        var width = Math.Max(1, ProgressWidth);
        var perStep = (player.Track.Seconds + 1) / (width * 2.0);
        var wait = Math.Clamp(Math.Min(Math.Min(toSecond, perStep), ToNextLyric(pos) + 0.001), 0.016, 1.0);
        clock = After((uint)Math.Ceiling(wait * 1000), clockTick);
    }

    Action clockTick => tickAction ??= () =>
    {
        clock = 0;
        try
        {
            var pos = player.Position;
            if (pos + 0.5 < lastPosition) ProgressJump(); // came round to the start again
            lastPosition = pos;
            ShowTime(pos);
        }
        finally { Schedule(); } // a throw mustn't stop the clock
    };
    Action? tickAction;

    void ShowTime(double pos)
    {
        var second = Math.Min((int)pos, player.Track.Seconds); // a player may report past the end
        if (second != shownSecond)
        {
            shownSecond = second;
            N.gtk_label_set_text(elapsed, Time.Format(second));
        }
        LyricLine(pos);
        N.gtk_widget_queue_draw(progress);
    }

    // The list it plays from: near the end of what's in of it, its next
    // page; shuffled, every page as it comes, so all of it can come up.
    void MoreOfPlaying()
    {
        var order = player.Order;
        if (order.List.More && (order.Shuffle || order.At >= order.List.Count - 3)) lists.More(order.List);
    }

    // Shuffle or repeat changed, or where it is in the list.
    void OrderChanged()
    {
        NextChanged();
        ShowModes();
        ShowContext();
    }

    // ---- what it remembers -------------------------------------------------------

    uint rememberLater;

    // Saved half a second after the last change, so a drag writes once.
    void Remember()
    {
        Cancel(ref rememberLater);
        rememberLater = After(500, () =>
        {
            rememberLater = 0;
            remembered.Save(Remembered.FilePath());
        });
    }

    // ---- colours -----------------------------------------------------------------

    nint css, colorsMonitor;
    uint recolorLater;

    // The colours file is watched (inotify, through GLib), so a matugen
    // template writing it as the wallpaper changes recolours the window
    // as it is; nothing runs until it changes. Its folder is made if it
    // isn't there: a missing folder can't be watched without polling.
    void WatchColors()
    {
        var path = Palette.FilePath();
        try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        nint error = 0;
        colorsMonitor = N.g_file_monitor_file(N.g_file_new_for_path(path), 0, 0, &error);
        if (colorsMonitor == 0)
        {
            Console.Error.WriteLine($"nattsang: can't watch {path}: {(error != 0 ? Str(*(nint*)(error + 8)) : "")}");
            if (error != 0) N.g_error_free(error);
            return;
        }
        OnFileChanged(colorsMonitor, kind =>
        {
            // Written (done), made, gone, moved in or over: read it again,
            // once, when the writing has settled.
            if (kind is not (1 or 2 or 3 or 7 or 8 or 9)) return;
            Cancel(ref recolorLater);
            recolorLater = After(150, () =>
            {
                recolorLater = 0;
                Recolor(Palette.Load());
            });
        });
    }

    // New colours: the stylesheet, and what's coloured outside it (markup
    // and drawing) made again.
    void Recolor(Palette next)
    {
        if (next.Same(palette)) return;
        palette = next;
        N.gtk_css_provider_load_from_string(css, Style.Css(palette));
        foreach (var (label, key, what) in keyHints) N.gtk_label_set_markup(label, KeyMarkup(key, what));
        foreach (var item in made.Keys) FillRow(item);
        QueueChanged();
        NextChanged();
        foreach (var a in (nint[])[progress, pills, lyricsArea, volumeBar, divider, .. eqAreas]) N.gtk_widget_queue_draw(a);
    }

    void ToggleViz()
    {
        vizOn = !vizOn;
        Opacity(pills, FadeOf(pills).Shown);
        WantLevels();
        Motion();
    }
}

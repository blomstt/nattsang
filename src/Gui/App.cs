using Nattsang.Core;
using Nattsang.Playback;
using static Nattsang.Gui.Ui;

namespace Nattsang.Gui;

// The window: the library at the left, now playing with lyrics or the queue
// at the right, the dock below them and the key bar at the bottom; the × at
// the top right quits. Too small for all of it, parts hide (Fit.cs). Nothing
// runs while it's paused: the clock, the progress bar and the visualizer only
// tick while something plays.
sealed unsafe partial class App
{
    public const string Id = "dev.blomst.Nattsang";

    readonly Palette palette;
    readonly IPlayback player;
    readonly Queue queue;
    bool animations;
    nint app, window;
    bool vizOn = true;

    App(Palette palette, IPlayback player, Queue queue)
    {
        this.palette = palette;
        this.player = player;
        this.queue = queue;
    }

    public static int Run()
    {
        if (N.gtk_get_minor_version() < 12)
        {
            Console.Error.WriteLine("nattsang needs GTK 4.12 or newer.");
            return 1;
        }
        var player = new SimulatedPlayback(Sample.Tracks, Sample.Playing, Sample.Position, true, () => Clock);
        var a = new App(Palette.Load(), player, new Queue(Sample.Queue));
        N.g_set_application_name("Nattsang");
        a.app = N.gtk_application_new(Id, 0);
        On(a.app, "activate", a.Activate);
        On(a.app, "shutdown", a.Closed);
        return N.g_application_run(a.app, 0, 0);
    }

    void Activate()
    {
        if (window != 0) { N.gtk_window_present(window); return; }
        animations = Animations;
        var css = N.gtk_css_provider_new();
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
        queue.Changed += QueueChanged;
        QueueChanged();
        Sync();
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
    }

    // The ×: everything closes, through shutdown (Closed).
    void Quit() => N.g_application_quit(app);

    // The window is gone, or the app is: nothing may touch its widgets after.
    void Closed()
    {
        Cancel(ref clock);
        player.Changed -= Sync;
        queue.Changed -= QueueChanged;
    }

    // ---- keys ----------------------------------------------------------------

    const uint Space = 0x20, Slash = 0x2f, KeyA = 0x61, KeyN = 0x6e, KeyP = 0x70, KeyQ = 0x71, KeyV = 0x76;
    const uint Escape = 0xff1b, Return = 0xff0d, KpEnter = 0xff8d;
    const uint Modifiers = 4 | 8 | 1 << 26 | 1 << 27 | 1 << 28; // Control, Alt, Super, Hyper, Meta

    bool Key(uint keyval, uint state)
    {
        if ((state & Modifiers) != 0) return false;
        var focus = N.gtk_root_get_focus(window);
        if (focus != 0 && (focus == search || N.gtk_widget_is_ancestor(focus, search)))
        {
            // Typing goes to the search; Escape or Enter goes back to the list.
            if (keyval is not (Escape or Return or KpEnter)) return false;
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
            case KeyQ: ShowQueue(!showingQueue); return true;
            case KeyV: ToggleViz(); return true;
        }
        return false;
    }

    // ---- playback ------------------------------------------------------------

    Track? shownTrack;
    int shownSecond = -1;
    double lastPosition;
    uint clock;

    // The player changed: a new track, play or pause, a jump.
    void Sync()
    {
        var t = player.Track;
        var pos = player.Position;
        var first = shownTrack == null;
        var newTrack = t != shownTrack;
        if (newTrack)
        {
            shownTrack = t;
            N.gtk_label_set_text(song, t.Title);
            N.gtk_label_set_text(sub, $"{t.Artist} · {t.Album}");
            N.gtk_label_set_text(total, Time.Format(t.Seconds));
            N.gtk_label_set_text(queueNowTitle, $"{t.Title} · {t.Artist}");
            N.gtk_label_set_text(queueNowTime, Time.Format(t.Seconds));
            MarkCurrentRow();
            LyricsForTrack(t, pos, first);
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
    // time, the lyric line) or the next half pixel of the progress bar,
    // whichever comes first; paused, it doesn't run.
    void Schedule()
    {
        Cancel(ref clock);
        if (!player.Playing) return;
        var pos = player.Position;
        var toSecond = Math.Floor(pos) + 1 - pos;
        var width = Math.Max(1, ProgressWidth);
        var perStep = (player.Track.Seconds + 1) / (width * 2.0);
        var wait = Math.Clamp(Math.Min(toSecond, perStep), 0.016, 1.0);
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

    void ToggleViz()
    {
        vizOn = !vizOn;
        Opacity(pills, FadeOf(pills).Shown);
        Motion();
    }
}

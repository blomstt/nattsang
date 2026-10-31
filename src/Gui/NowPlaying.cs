using System.Runtime.InteropServices;
using Nattsang.Core;
using static Nattsang.Gui.Ui;

namespace Nattsang.Gui;

// The now playing panel: the title, the lyrics | queue tabs and the view
// under them. A view that comes in rises 10 px and fades in (0.4 s).
sealed unsafe partial class App
{
    nint song, sub, lyricsTab, queueTab, swap, lyricsArea, queueView;
    nint queueNowTitle, queueNowTime, queueList, queueSummary, queueHead;
    bool showingQueue;

    nint BuildNowPlaying()
    {
        song = Label("", "song");
        sub = Label("", "sub");
        N.gtk_label_set_wrap(song, true);
        N.gtk_label_set_wrap(sub, true);
        var heading = Add(Box(Vertical, 4), song, sub);

        lyricsTab = Button("lyrics", null, () => ShowQueue(false), "tab", "on");
        queueTab = Button("queue", null, () => ShowQueue(true), "tab");
        var tabs = Add(Box(Horizontal, 4, "tabs"), lyricsTab, queueTab);
        // The title at the left and the tabs at the right, on its bottom; a
        // long title pushes the tabs onto a line of their own.
        var head = Add(Wrap.Row(12, 12, Wrap.Align.End), Wrap.Item(heading, 1), tabs);

        lyricsArea = Area(-1, SwapHeight);
        OnDraw(lyricsArea, DrawLyrics);
        queueView = BuildQueue();
        N.gtk_widget_set_visible(queueView, false);

        // The views sit in a box laid out by hand, so one can be drawn
        // shifted down while it comes in, clipped to the box.
        swap = N.gtk_box_new(Vertical, 0);
        N.gtk_widget_set_layout_manager(swap, N.gtk_custom_layout_new(0,
            (nint)(delegate* unmanaged<nint, int, int, int*, int*, int*, int*, void>)&MeasureSwap,
            (nint)(delegate* unmanaged<nint, int, int, int, void>)&AllocateSwap));
        N.gtk_widget_set_overflow(swap, 1); // GTK_OVERFLOW_HIDDEN
        N.gtk_widget_set_vexpand(swap, false); // fixed 392 px: the queue's list mustn't make it grow
        N.gtk_widget_set_parent(lyricsArea, swap);
        N.gtk_widget_set_parent(queueView, swap);

        var panel = Add(Box(Vertical, 16, "panel", "now"), head, swap);
        return Titled(panel, "now playing", out _);
    }

    // ---- lyrics | queue --------------------------------------------------------

    const int SwapHeight = 392;
    static float swapShift;
    double swapStart;
    uint swapTick;

    void ShowQueue(bool on)
    {
        if (on == showingQueue) return;
        showingQueue = on;
        Class(lyricsTab, "on", !on);
        Class(queueTab, "on", on);
        var shown = on ? queueView : lyricsArea;
        N.gtk_widget_set_visible(lyricsArea, !on);
        N.gtk_widget_set_visible(queueView, on);
        StopTicking(lyricsArea, ref lyricTick);
        lyricStart = 0; // a view that comes in starts where it is, as a remounted one does

        StopTicking(swap, ref swapTick);
        if (!animations)
        {
            swapShift = 0;
            N.gtk_widget_set_opacity(shown, 1);
            return;
        }
        swapStart = Clock;
        swapShift = 10;
        N.gtk_widget_set_opacity(shown, 0);
        N.gtk_widget_queue_allocate(swap);
        swapTick = Ticking(swap, now =>
        {
            var p = Math.Clamp((now - swapStart) / 0.4, 0, 1);
            var e = Ease.Out.At(p);
            swapShift = (float)(10 * (1 - e));
            N.gtk_widget_set_opacity(shown, e);
            N.gtk_widget_queue_allocate(swap);
            if (p < 1) return true;
            swapTick = 0;
            return false;
        });
    }

    // As Wrap's: no exception gets into GTK; a failed measure asks for
    // nothing across and the fixed height down.
    [UnmanagedCallersOnly]
    static void MeasureSwap(nint widget, int orientation, int forSize, int* min, int* nat, int* minBaseline, int* natBaseline)
    {
        int lo = 0, hi = 0;
        try
        {
            if (orientation == Horizontal)
                for (var c = N.gtk_widget_get_first_child(widget); c != 0; c = N.gtk_widget_get_next_sibling(c))
                {
                    if (!N.gtk_widget_get_visible(c)) continue;
                    int m, n;
                    N.gtk_widget_measure(c, Horizontal, -1, &m, &n, null, null);
                    lo = Math.Max(lo, m);
                    hi = Math.Max(hi, n);
                }
            else lo = hi = SwapHeight;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            lo = hi = orientation == Horizontal ? 0 : SwapHeight;
        }
        *min = lo;
        *nat = hi;
        *minBaseline = *natBaseline = -1;
    }

    [UnmanagedCallersOnly]
    static void AllocateSwap(nint widget, int width, int height, int baseline)
    {
        var shift = stackalloc float[] { 0, swapShift };
        try
        {
            for (var c = N.gtk_widget_get_first_child(widget); c != 0; c = N.gtk_widget_get_next_sibling(c))
            {
                if (!N.gtk_widget_get_visible(c)) continue;
                int m, n;
                N.gtk_widget_measure(c, Vertical, width, &m, &n, null, null); // GTK wants children measured before they're allocated
                N.gtk_widget_allocate(c, width, height, -1, swapShift == 0 ? 0 : N.gsk_transform_translate(0, shift));
            }
        }
        catch (Exception e) { Console.Error.WriteLine(e); }
    }

    // ---- lyrics ----------------------------------------------------------------

    // What the lyrics view shows: lines with a current one (synced lyrics,
    // or the sample's placeholders), plain lyrics going by evenly with none
    // marked, or a note in place of the current line (looking, none found,
    // instrumental, an error).
    enum LyricView { Lines, Paced, Note }

    string[] lyricLines = [];
    double[]? lyricTimes; // synced: when each line starts; null: the placeholders' four seconds
    LyricView lyricView;
    string lyricsUri = ""; // the track they're for
    int lyricLine, lyricFrom;
    double lyricStart, lyricFromOffset; // lyricStart 0: not moving
    uint lyricTick;
    nint lyricFont, lyricBold, lyricAttrs;

    // A new track: the sample's get placeholders; the account's are looked
    // up (lyricsSource), once per track, the note showing until they come.
    void LyricsForTrack(Track t, double pos, bool first)
    {
        if (t.Uri == lyricsUri && !first) return; // the same track, its details filled in
        lyricsUri = t.Uri;
        if (t.Uri.Length == 0) ShowLyrics(LyricView.Note, [], null, pos, first);
        else if (lyricsSource == null) ShowLyrics(LyricView.Lines, Lyrics.Placeholder(t.Seconds), null, pos, first);
        else
        {
            ShowLyrics(LyricView.Note, ["looking for lyrics…"], null, pos, first);
            lyricsSource.Find(t, lyricsFound);
        }
    }

    // Only for the track that's on: an answer for one it has left is dropped.
    Action<Track, Lyrics.Song?, string?> lyricsFound => lyricsAction ??= (t, song, error) =>
    {
        if (t.Uri != lyricsUri || t.Uri != player.Track.Uri) return;
        var pos = player.Position;
        if (song == null) ShowLyrics(LyricView.Note, [$"couldn't load the lyrics: {error}"], null, pos, false);
        else if (song.Kind == Lyrics.Kind.Synced) ShowLyrics(LyricView.Lines, song.Lines, song.Times, pos, false);
        else if (song.Kind == Lyrics.Kind.Plain) ShowLyrics(LyricView.Paced, song.Lines, null, pos, false);
        else if (song.Kind == Lyrics.Kind.Instrumental) ShowLyrics(LyricView.Note, [$"{Lyrics.Break} instrumental"], null, pos, false);
        else ShowLyrics(LyricView.Note, ["no lyrics found for this song"], null, pos, false);
        Schedule(); // the clock wakes for the next line
    };
    Action<Track, Lyrics.Song?, string?>? lyricsAction;

    void ShowLyrics(LyricView view, string[] lines, double[]? times, double pos, bool first)
    {
        (lyricView, lyricLines, lyricTimes) = (view, lines, times);
        var line = LyricLineAt(pos);
        if (first) { lyricLine = lyricFrom = line; lyricFromOffset = Lyrics.Offset(line); return; }
        MoveLyrics(line);
    }

    int LyricLineAt(double pos) => lyricView switch
    {
        LyricView.Lines => lyricTimes is { } times ? Lyrics.LineAt(pos, times) : Lyrics.LineAt(pos, lyricLines.Length),
        LyricView.Paced => Lyrics.PacedAt(pos, player.Track.Seconds, lyricLines.Length),
        _ => 0,
    };

    // Seconds from pos to the next synced line, for the clock; infinity when none comes.
    double ToNextLyric(double pos)
    {
        if (lyricView != LyricView.Lines || lyricTimes is not { } times) return double.PositiveInfinity;
        var next = Lyrics.LineAt(pos, times) + 1;
        if (pos < times.FirstOrDefault()) next = 0;
        return next < times.Length ? times[next] - pos : double.PositiveInfinity;
    }

    void LyricLine(double pos)
    {
        var line = LyricLineAt(pos);
        if (line != lyricLine) MoveLyrics(line);
    }

    // How far into the 0.6 s move, 0 to 1 (1 when still).
    double LyricProgress => lyricStart == 0 ? 1 : Math.Clamp((Clock - lyricStart) / 0.6, 0, 1);

    // The lines' offset, move (eased) of the way from where the move started.
    double LyricOffset(double move) => lyricFromOffset + (Lyrics.Offset(lyricLine) - lyricFromOffset) * move;

    // The new line moves up into place over 0.6 s; colours and size follow.
    // A move that comes mid-move starts from where the lines are drawn now.
    void MoveLyrics(int line)
    {
        lyricFromOffset = LyricOffset(Ease.Out.At(LyricProgress));
        lyricFrom = lyricLine;
        lyricLine = line;
        if (!animations || showingQueue)
        {
            lyricStart = 0;
            N.gtk_widget_queue_draw(lyricsArea);
            return;
        }
        lyricStart = Clock;
        N.gtk_widget_queue_draw(lyricsArea);
        if (lyricTick != 0) return;
        lyricTick = Ticking(lyricsArea, now =>
        {
            N.gtk_widget_queue_draw(lyricsArea);
            if (now - lyricStart < 0.6) return true;
            lyricTick = 0;
            lyricStart = 0; // and the last frame draws it in place
            return false;
        });
    }

    uint ToneColor(int distance) => Lyrics.ToneAt(distance) switch
    {
        Lyrics.Tone.Current => palette.Playing,
        Lyrics.Tone.Next => palette.Bright,
        Lyrics.Tone.Near => palette.Text,
        _ => palette.Dim,
    };

    void DrawLyrics(nint cr, int width, int height)
    {
        if (lyricFont == 0)
        {
            lyricFont = Font(400);
            lyricBold = Font(700);
            lyricAttrs = N.pango_attr_list_new();
            N.pango_attr_list_insert(lyricAttrs, N.pango_attr_font_features_new("calt=0,liga=0"));
        }
        var p = LyricProgress;
        var move = Ease.Out.At(p);   // transform .6s cubic-bezier(.2,.8,.2,1)
        var fade = Ease.Css.At(p);   // color .6s ease
        var offset = LyricOffset(move);
        const int h = Lyrics.LineHeight;

        // Only lines with a current one mark it; plain lyrics and notes are
        // drawn evenly, plain in the text colour, a note dim.
        var marked = lyricView == LyricView.Lines;
        var layout = N.pango_cairo_create_layout(cr);
        N.pango_layout_set_attributes(layout, lyricAttrs);
        N.pango_layout_set_ellipsize(layout, 3); // PANGO_ELLIPSIZE_END
        for (int i = 0; i < lyricLines.Length; i++)
        {
            var y = offset + i * h;
            if (y < -h * 1.2 || y > height + h * 0.2 || lyricLines[i].Length == 0) continue;
            var current = marked && i == lyricLine;
            N.pango_layout_set_font_description(layout, current ? lyricBold : lyricFont);
            N.pango_layout_set_text(layout, current ? "▸ " + lyricLines[i] : lyricLines[i], -1);
            var scaleFrom = marked && i == lyricFrom ? 1.12 : 1;
            var scale = scaleFrom + ((current ? 1.12 : 1) - scaleFrom) * move;
            // A line too long for the panel ends in "…", at the size it's drawn.
            N.pango_layout_set_width(layout, (int)(width / scale * 1024));
            int lw, lh;
            N.pango_layout_get_size(layout, &lw, &lh);
            N.cairo_save(cr);
            N.cairo_translate(cr, 0, y + h / 2.0);
            N.cairo_scale(cr, scale, scale);
            N.cairo_move_to(cr, 0, -lh / 1024.0 / 2);
            Rgb(cr, !marked ? (lyricView == LyricView.Note ? palette.Dim : palette.Text)
                : Mix(ToneColor(i - lyricFrom), ToneColor(i - lyricLine), fade));
            N.pango_cairo_show_layout(cr, layout);
            N.cairo_restore(cr);
        }
        N.g_object_unref(layout);
    }

    static nint Font(int weight)
    {
        var f = N.pango_font_description_from_string("Cascadia Code");
        N.pango_font_description_set_absolute_size(f, 14 * 1024);
        N.pango_font_description_set_weight(f, weight);
        return f;
    }

    // ---- queue -----------------------------------------------------------------

    // The view: the playing track, then what plays next: the queue (each
    // can be taken off it), then the list it plays from, as far as
    // NextShown goes. Rows are kept and changed in place, never all made
    // again, so a long queue costs only what changed.
    const int NextShown = 50;
    nint nextList, queueHeadQueue;
    readonly List<QueueRow> queueRows = [], nextRows = [];

    sealed class QueueRow
    {
        public nint Row, Title, Time;
        public string Text = "", Seconds = "";
    }

    nint BuildQueue()
    {
        var eq = Area(24, 21);
        OnDraw(eq, DrawEq);
        eqAreas.Add(eq);
        queueNowTitle = Label("");
        N.gtk_widget_set_hexpand(queueNowTitle, true);
        N.gtk_label_set_ellipsize(queueNowTitle, 3);
        queueNowTime = Label("");
        N.gtk_label_set_xalign(queueNowTime, 1);
        N.gtk_widget_set_size_request(queueNowTime, 48, -1);
        var gap = Box(Horizontal, 0);
        N.gtk_widget_set_size_request(gap, 24, -1);
        var now = Add(Box(Horizontal, 10, "qnow"), eq, queueNowTitle, queueNowTime, gap);

        queueHeadQueue = Label("next in queue", "qhead");
        queueList = Css(N.gtk_list_box_new(), "queue-list");
        N.gtk_list_box_set_selection_mode(queueList, 0); // GTK_SELECTION_NONE
        queueHead = Label("next", "qhead");
        nextList = Css(N.gtk_list_box_new(), "queue-list");
        N.gtk_list_box_set_selection_mode(nextList, 0);
        var scroll = N.gtk_scrolled_window_new();
        N.gtk_scrolled_window_set_policy(scroll, 2, 1);
        N.gtk_scrolled_window_set_child(scroll, Add(Box(Vertical, 0), queueHeadQueue, queueList, queueHead, nextList));
        N.gtk_widget_set_vexpand(scroll, true);

        queueSummary = Label("");
        var foot = Add(Box(Horizontal, 18, "qfoot"), queueSummary, Spacer(),
            Button("shuffle queue", null, () => player.Order.Queue.Shuffle(Random.Shared)),
            Button("clear", null, player.Order.Queue.Clear));

        return Add(Box(Vertical, 2), now, scroll, foot);
    }

    void QueueChanged()
    {
        var queue = player.Order.Queue;
        N.gtk_button_set_label(queueTab, $"queue · {queue.Count}");
        N.gtk_label_set_text(queueSummary, queue.Summary);
        N.gtk_widget_set_visible(queueHeadQueue, queue.Count > 0);
        SyncRows(queueList, queueRows, queue.Items, true);
    }

    // The list it plays from moved on, or grew: the rows after the queue.
    void NextChanged()
    {
        var order = player.Order;
        var next = order.Next(NextShown).ToList();
        N.gtk_label_set_text(queueHead, order.List.Title.Length > 0 ? $"next from {order.List.Title}" : "next");
        N.gtk_widget_set_visible(queueHead, next.Count > 0);
        SyncRows(nextList, nextRows, next, false);
    }

    // The list's rows show tracks: the rows there are get the new text where
    // it differs, missing ones are added and extra ones taken off the end.
    void SyncRows(nint list, List<QueueRow> rows, IReadOnlyList<Track> tracks, bool removable)
    {
        for (int i = 0; i < tracks.Count; i++)
        {
            var t = tracks[i];
            var text = $"{Escape(t.Title)} <span foreground=\"{palette.Hex("dim")}\">· {Escape(t.Artist)}</span>";
            var seconds = Time.Format(t.Seconds);
            if (i == rows.Count) rows.Add(QueueRowFor(list, removable));
            var r = rows[i];
            if (r.Text != text) N.gtk_label_set_markup(r.Title, r.Text = text);
            if (r.Seconds != seconds) N.gtk_label_set_text(r.Time, r.Seconds = seconds);
        }
        while (rows.Count > tracks.Count)
        {
            N.gtk_list_box_remove(list, rows[^1].Row);
            rows.RemoveAt(rows.Count - 1);
        }
    }

    QueueRow QueueRowFor(nint list, bool removable)
    {
        var r = new QueueRow();
        var handle = Label(removable ? "⋮⋮" : "", "handle");
        N.gtk_widget_set_size_request(handle, 24, -1);
        r.Title = Label("");
        N.gtk_widget_set_hexpand(r.Title, true);
        N.gtk_label_set_ellipsize(r.Title, 3);
        r.Time = Label("");
        N.gtk_label_set_xalign(r.Time, 1);
        N.gtk_widget_set_size_request(r.Time, 48, -1);
        nint end;
        if (removable)
        {
            end = N.gtk_button_new_with_label("×");
            Css(end, "btn", "x", "playing");
            Name(end, "Remove from queue");
            OnClicked(end, removeFromQueue);
            N.gtk_widget_set_valign(end, Center);
        }
        else
        {
            end = Box(Horizontal, 0);
            N.gtk_widget_set_size_request(end, 24, -1);
        }
        var box = Add(Box(Horizontal, 10), handle, r.Title, r.Time, end);
        N.gtk_list_box_append(list, box);
        r.Row = N.gtk_widget_get_parent(box); // the row the list box put it in
        return r;
    }

    Action<nint> removeFromQueue => removeAction ??= button =>
    {
        var row = N.gtk_widget_get_ancestor(button, N.gtk_list_box_row_get_type());
        if (row != 0) player.Order.Queue.RemoveAt(N.gtk_list_box_row_get_index(row));
    };
    Action<nint>? removeAction;
}

using Nattsang.Core;
using static Nattsang.Gui.Ui;

namespace Nattsang.Gui;

// The dock: shuffle, the transport buttons and repeat, elapsed | progress |
// total, and the Pills visualizer. Then the key bar under everything, with
// Nattsang's own volume at its right: + and - step it, and the wheel over
// it, a click or a drag on its bar set it.
sealed unsafe partial class App
{
    nint playButton, elapsed, total, progress, pills;
    nint controls, volume, volumeBar, volumePercent;
    nint shuffleButton, shuffleDot, repeatButton, repeatOne;
    readonly List<nint> eqAreas = [], hints = [];
    // The key hints, to colour their keys again when the colours change.
    readonly List<(nint Label, string Key, string What)> keyHints = [];

    // The dock's gaps, the key bar's, and the least the progress bar may
    // shrink to (its area, the 14 px either side included) before the times
    // beside it hide.
    const int DockGap = 24, KeyGap = 22, ProgressMin = 120;

    nint BuildDock()
    {
        playButton = Button("▶", "Play", player.Toggle, "play", "playing");
        shuffleDot = Box(Horizontal, 0, "dot");
        N.gtk_widget_set_halign(shuffleDot, Center);
        N.gtk_widget_set_valign(shuffleDot, End);
        shuffleButton = Marked("⇄", shuffleDot, ToggleShuffle);
        repeatOne = Label("1", "one");
        N.gtk_widget_set_halign(repeatOne, End);
        N.gtk_widget_set_valign(repeatOne, Start);
        repeatButton = Marked("↻", repeatOne, player.Order.CycleRepeat);
        controls = Add(Box(Horizontal, 6),
            shuffleButton,
            Button("⏮", "Previous", player.Previous, "ctl", "bright"),
            playButton,
            Button("⏭", "Next", player.Next, "ctl", "bright"),
            repeatButton);
        N.gtk_widget_set_valign(controls, Center);
        ShowModes();

        elapsed = Label("0:00", "time");
        N.gtk_label_set_xalign(elapsed, 1);
        N.gtk_label_set_width_chars(elapsed, 5);
        total = Label("0:00", "time");
        N.gtk_label_set_width_chars(total, 5);
        // The bar's area takes in the 14 px gaps either side, so the thumb
        // and its glow can reach past the ends as the design's do.
        progress = Area(-1, ProgressHeight);
        OnDraw(progress, DrawProgress);
        var bar = Add(Wrap.Row(0, 0, Wrap.Align.Stretch), elapsed, Wrap.Item(progress, 1, ProgressMin), total);

        pills = Area(320, 56);
        N.gtk_widget_set_valign(pills, Center);
        OnDraw(pills, DrawPills);

        return Add(Wrap.Row(DockGap, 12, Wrap.Align.Center, "dock"), controls, Wrap.Item(bar, 1), pills);
    }

    // A transport button with a mark over its glyph (shuffle's dot,
    // repeat's 1), shown when it's on.
    static nint Marked(string glyph, nint mark, Action clicked)
    {
        var layers = N.gtk_overlay_new();
        N.gtk_overlay_set_child(layers, Label(glyph));
        N.gtk_widget_set_can_target(mark, false);
        N.gtk_overlay_add_overlay(layers, mark);
        var b = Css(N.gtk_button_new(), "btn", "ctl", "bright");
        N.gtk_button_set_child(b, layers);
        N.gtk_widget_set_halign(N.gtk_widget_get_first_child(layers), Center);
        On(b, "clicked", clicked);
        return b;
    }

    // Shuffle lit with its dot when on; repeat lit for the list, lit with
    // a 1 for one song. Their names say which, for screen readers.
    void ShowModes()
    {
        var order = player.Order;
        Class(shuffleButton, "on", order.Shuffle);
        N.gtk_widget_set_visible(shuffleDot, order.Shuffle);
        Name(shuffleButton, order.Shuffle ? "Shuffle: on" : "Shuffle: off");
        Class(repeatButton, "on", order.Repeat != Order.Repeats.Off);
        N.gtk_widget_set_visible(repeatOne, order.Repeat == Order.Repeats.One);
        Name(repeatButton, order.Repeat switch
        {
            Order.Repeats.List => "Repeat: the list",
            Order.Repeats.One => "Repeat: one song",
            _ => "Repeat: off",
        });
    }

    // Shuffled, the rest of a list it plays loads as it goes, so all of it
    // can come up (MoreOfPlaying).
    void ToggleShuffle()
    {
        player.Order.SetShuffle(!player.Order.Shuffle);
        MoreOfPlaying();
    }

    string KeyMarkup(string key, string what) => $"<span foreground=\"{palette.Hex("accent")}\">{Escape(key)}</span> {what}";

    nint Hint(string key, string what)
    {
        var l = Markup(KeyMarkup(key, what));
        keyHints.Add((l, key, what));
        return l;
    }

    nint BuildKeyBar()
    {
        var vizName = Css(N.gtk_label_new("09 Pills"), "viz-name");
        N.gtk_label_set_width_chars(vizName, 21);
        var viz = Add(Box(Horizontal, 4),
            Hint("v", "visualizer"),
            Button("◂", "Previous visualizer", Nothing, "arrow", "accent"),
            vizName,
            Button("▸", "Next visualizer", Nothing, "arrow", "accent"));

        volumeBar = Area(VolumeWidth, 14);
        N.gtk_widget_set_valign(volumeBar, Center);
        OnDraw(volumeBar, DrawVolume);
        N.gtk_widget_set_cursor_from_name(volumeBar, "pointer");
        OnDrag(volumeBar, (x, _) => { dragFrom = x; SetVolumeAt(x); }, (dx, _) => SetVolumeAt(dragFrom + dx), () => { });
        volumePercent = Label("", "vol-pct");
        N.gtk_label_set_width_chars(volumePercent, 4);
        N.gtk_label_set_xalign(volumePercent, 1);
        volume = Add(Box(Horizontal, 10, "volume"), Label("vol"), volumeBar, volumePercent);
        OnScroll(volume, (_, dy) =>
        {
            if (dy < 0) volumeLevel.Up();
            else if (dy > 0) volumeLevel.Down();
            return dy != 0;
        });
        ShowVolume();

        hints.AddRange([
            Hint("/", "search"),
            Hint("space", "play/pause"),
            Hint("n p", "next/prev"),
            Hint("a", "add to queue"),
            Hint("q", "queue/lyrics"),
            Hint("+ −", "volume")]);
        var keys = Add(Wrap.Row(KeyGap, 6, Wrap.Align.Center, "keys"), [.. hints, Wrap.Item(Spacer(), 1), viz, volume]);
        hints.Add(viz); // the visualizer switcher hides with the hints; the volume stays
        return keys;
    }

    // ---- volume ------------------------------------------------------------------

    const int VolumeWidth = 120;
    double dragFrom;

    void SetVolumeAt(double x) => volumeLevel.Set(Volume.AtX(x, N.gtk_widget_get_width(volumeBar)));

    // The volume changed (keys, the bar, the wheel, MPRIS): the bar, the
    // helper, and the state file a moment later.
    void VolumeChanged()
    {
        ShowVolume();
        player.SetVolume(volumeLevel.Gain);
        remembered.Volume = volumeLevel.Percent;
        Remember();
    }

    void ShowVolume()
    {
        N.gtk_label_set_text(volumePercent, $"{volumeLevel.Percent}%");
        Name(volumeBar, $"Volume {volumeLevel.Percent}%");
        N.gtk_widget_queue_draw(volumeBar);
    }

    // A 2 px line, the part up to the volume in the accent, and a 10 px knob.
    void DrawVolume(nint cr, int width, int height)
    {
        var cy = height / 2.0;
        var x = width * volumeLevel.Percent / (double)Volume.Full;
        Rgb(cr, palette.Border);
        N.cairo_rectangle(cr, 0, cy - 1, width, 2);
        N.cairo_fill(cr);
        Rgb(cr, palette.Accent);
        N.cairo_rectangle(cr, 0, cy - 1, x, 2);
        N.cairo_fill(cr);
        Rgb(cr, palette.Bright);
        N.cairo_new_path(cr);
        N.cairo_arc(cr, Math.Clamp(x, 5, width - 5), cy, 5, 0, 2 * Math.PI);
        N.cairo_fill(cr);
    }

    // ---- progress ----------------------------------------------------------------

    const int ProgressHeight = 40, Gap = 14;
    double jumpFrom = -1, jumpStart;
    uint jumpTick;

    int ProgressWidth => Math.Max(0, N.gtk_widget_get_width(progress) - 2 * Gap);

    double Fraction()
    {
        var t = player.Track;
        return t.Seconds > 0 ? Math.Clamp(player.Position / t.Seconds, 0, 1) : 0;
    }

    // The bar slides to where the track is now over a second, as the
    // design's 1 s width transition does when the time jumps back.
    void ProgressJump()
    {
        StopTicking(progress, ref jumpTick);
        if (!animations) { jumpFrom = -1; return; }
        jumpFrom = shownFraction;
        jumpStart = Clock;
        jumpTick = Ticking(progress, now =>
        {
            N.gtk_widget_queue_draw(progress);
            if (now - jumpStart < 1) return true;
            jumpTick = 0;
            jumpFrom = -1;
            return false;
        });
    }

    double shownFraction;

    void DrawProgress(nint cr, int width, int height)
    {
        var f = Fraction();
        if (jumpFrom >= 0)
        {
            var p = Math.Clamp(Clock - jumpStart, 0, 1);
            f = jumpFrom + (f - jumpFrom) * p;
        }
        shownFraction = f;
        double w = width - 2 * Gap, cy = height / 2.0, x = Gap + w * f;

        Rgb(cr, palette.Groove);
        N.cairo_rectangle(cr, Gap, cy - 1, w, 2);
        N.cairo_fill(cr);

        if (x > Gap)
        {
            var g = N.cairo_pattern_create_linear(Gap, 0, x, 0);
            Stop(g, 0, palette.Accent);
            Stop(g, 1, palette.Playing);
            N.cairo_set_source(cr, g);
            N.cairo_rectangle(cr, Gap, cy - 1, x - Gap, 2);
            N.cairo_fill(cr);
            N.cairo_pattern_destroy(g);
        }

        // box-shadow: 0 0 10px: the 6 px disc blurred (sigma 5) under it
        var glow = N.cairo_pattern_create_radial(x, cy, 0, x, cy, GlowRadius);
        foreach (var (r, a) in glowStops) Stop(glow, r / GlowRadius, palette.Playing, a);
        N.cairo_set_source(cr, glow);
        N.cairo_rectangle(cr, x - GlowRadius, cy - GlowRadius, 2 * GlowRadius, 2 * GlowRadius);
        N.cairo_fill(cr);
        N.cairo_pattern_destroy(glow);

        Rgb(cr, palette.Playing);
        N.cairo_new_path(cr);
        N.cairo_arc(cr, x, cy, 6, 0, 2 * Math.PI);
        N.cairo_fill(cr);
    }

    const double GlowRadius = 21;
    static readonly (double R, double A)[] glowStops = [.. Enumerable.Range(0, 15).Select(i =>
    {
        var r = i * 1.5;
        return (r, 0.5 * Erfc((r - 6) / (5 * Math.Sqrt(2))));
    })];

    // Abramowitz and Stegun 7.1.26
    static double Erfc(double x)
    {
        var sign = x < 0 ? -1 : 1;
        x = Math.Abs(x);
        var t = 1 / (1 + 0.3275911 * x);
        var y = 1 - ((((1.061405429 * t - 1.453152027) * t + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        return 1 - sign * y;
    }

    // ---- motion: the visualizer and the playing marks --------------------------

    // Seconds the animations have run: it moves only while playing with the
    // visualizer on, so paused or off, the frame clock stops and everything
    // holds still where it was.
    double motionTime, motionLast;
    uint motionTick;

    // With levels from the helper, each pill springs to its band's level
    // as it's heard; LevelLead looks a little ahead, for the time the
    // spring takes to get there.
    const double LevelLead = 0.03;
    readonly Pills.Spring[] springs = new Pills.Spring[Pills.Count];
    readonly double[] targets = new double[Pills.Count];
    bool Measured => levelFeed?.Measures == true;

    void Motion()
    {
        var run = player.Playing && vizOn && animations;
        if (run == (motionTick != 0)) return;
        if (!run)
        {
            StopTicking(window, ref motionTick);
            return;
        }
        motionLast = -1;
        motionTick = Ticking(window, now =>
        {
            var dt = motionLast >= 0 ? Math.Min(now - motionLast, 0.1) : 0;
            motionTime += dt;
            motionLast = now;
            if (Measured)
            {
                levelFeed!.Levels.At(now + LevelLead, targets);
                for (int i = 0; i < springs.Length; i++) springs[i].Step(targets[i], dt);
            }
            N.gtk_widget_queue_draw(pills);
            foreach (var a in eqAreas) if (N.gtk_widget_get_visible(a)) N.gtk_widget_queue_draw(a);
            return true;
        });
    }

    // Levels are asked of the helper only while the pills can be seen: the
    // visualizer on, not hidden for room, and motion allowed. Paused, the
    // helper measures nothing anyway: it only measures what it plays.
    void WantLevels() => levelFeed?.Want(vizOn && animations && (hidden & Room.Part.Visualizer) == 0);

    // The helper turned out to measure, or has gone: the pills switch
    // between the music and their own motion.
    void LevelsChanged() => N.gtk_widget_queue_draw(pills);

    // Off, the visualizer is hidden by its opacity (ToggleViz) and holds still.
    void DrawPills(nint cr, int width, int height)
    {
        var measured = Measured;
        for (int i = 0; i < Pills.Count; i++)
        {
            var s = measured ? Pills.LevelScale(springs[i].X) : Pills.Scale(i, motionTime);
            N.cairo_save(cr);
            N.cairo_translate(cr, (i + .5) / Pills.Count * width, height / 2.0);
            N.cairo_scale(cr, 1, s);
            Capsule(cr, -Pills.Width / 2, -Pills.Height / 2, Pills.Width, Pills.Height);
            N.cairo_restore(cr);
            Rgb(cr, Pills.Band(i) switch { 0 => palette.Accent, 1 => palette.Label, _ => palette.Playing });
            N.cairo_fill(cr);
        }
    }

    // Three bars bouncing on the text's baseline (.7, .5 and .9 s).
    static readonly double[] eqDurations = [.7, .5, .9];

    void DrawEq(nint cr, int width, int height)
    {
        const double baseline = 15.4, bar = 12;
        Rgb(cr, palette.Playing);
        for (int i = 0; i < 3; i++)
        {
            var s = .2 + .8 * Ease.InOut.Alternate(motionTime, eqDurations[i]);
            N.cairo_rectangle(cr, i * 5, baseline - bar * s, 3, bar * s);
        }
        N.cairo_fill(cr);
    }
}

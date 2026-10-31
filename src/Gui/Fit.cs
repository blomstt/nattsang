using System.Runtime.InteropServices;
using Nattsang.Core;
using static Nattsang.Gui.Ui;

namespace Nattsang.Gui;

// The window fits what it shows to its size. Each time it's laid out, it
// works out what every level of hiding (Room) needs at its width and shows
// the first level that fits, without the panel when it's too short; it can
// shrink as far as the last level needs. A part hides by not being
// child-visible: its row (Wrap) leaves it out, and it can still be measured
// for when it comes back. It all happens in the layout pass, and a part
// fades out or in over FadeTime after; nothing runs between resizes.
sealed unsafe partial class App
{
    nint library, nowPanel;
    Room.Part hidden;

    // The gap between the window's rows and between its panels; the panels'
    // flex bases; and the bar's width while the visualizer shows beside it.
    const int Spacing = 16, LibraryBasis = 470, NowBasis = 530, BarBasis = 420;

    static App? fitted; // the window's App, for the layout callbacks

    nint Fit(nint child)
    {
        fitted = this;
        var box = N.gtk_box_new(Vertical, 0);
        N.gtk_widget_set_layout_manager(box, N.gtk_custom_layout_new(
            (nint)(delegate* unmanaged<nint, int>)&FitMode,
            (nint)(delegate* unmanaged<nint, int, int, int*, int*, int*, int*, void>)&MeasureFit,
            (nint)(delegate* unmanaged<nint, int, int, int, void>)&AllocateFit));
        N.gtk_widget_set_overflow(box, 1); // GTK_OVERFLOW_HIDDEN
        return Add(box, child);
    }

    // What the window takes with parts hidden: the width that keeps the key
    // bar, the top row and the dock each on one line, and the least height
    // at width (at that width itself when width is -1 or narrower).
    (int Width, int Height) Need(Room.Part parts, int width)
    {
        bool Shows(Room.Part p) => (parts & p) == 0;

        // The key bar: the hints (and the visualizer switcher), a spacer, the volume.
        var (keysW, keysH) = Size(volume);
        keysW += KeyGap;
        if (Shows(Room.Part.Hints))
            foreach (var h in hints)
            {
                var (w, hh) = Size(h);
                keysW += w + KeyGap;
                keysH = Math.Max(keysH, hh);
            }
        keysW += 2 * Style.KeysSide;
        keysH += 2 * Style.KeysTop;

        // The top row: the library, and now playing at its basis beside it.
        var libraryW = Least(library);
        var nowW = Math.Max(Least(nowPanel), NowBasis);
        var topW = Shows(Room.Part.Panel) ? Math.Max(libraryW, LibraryBasis) + Spacing + nowW : libraryW;

        // The dock: the controls, the bar (the progress bar between the times), the visualizer.
        var (controlsW, dockH) = Size(controls);
        var barW = ProgressMin;
        dockH = Math.Max(dockH, Size(progress).Height);
        if (Shows(Room.Part.Times))
        {
            barW += Size(elapsed).Width + Size(total).Width;
            dockH = Math.Max(dockH, Size(elapsed).Height);
        }
        var dockW = controlsW + DockGap + barW;
        if (Shows(Room.Part.Visualizer))
        {
            var (pillsW, pillsH) = Size(pills);
            dockW += Math.Max(0, BarBasis - barW) + DockGap + pillsW;
            dockH = Math.Max(dockH, pillsH);
        }
        dockW += 2 * (Style.DockSide + Style.Line);
        dockH += 2 * (Style.DockTop + Style.Line);

        var need = Math.Max(keysW, 2 * Style.Side + Math.Max(topW, dockW));
        // Down at width; a level too wide for it is measured at its own width.
        var inner = Math.Max(width, need) - 2 * Style.Side;
        var topH = Shows(Room.Part.Panel)
            ? Math.Max(Down(library, inner - Spacing - nowW), Down(nowPanel, nowW))
            : Down(library, inner);
        return (need, Style.Top + topH + Spacing + dockH + Spacing + keysH);
    }

    // A part's natural width and its least height at that width.
    static (int Width, int Height) Size(nint w)
    {
        int min, nat, low, high;
        N.gtk_widget_measure(w, Horizontal, -1, &min, &nat, null, null);
        N.gtk_widget_measure(w, Vertical, nat, &low, &high, null, null);
        return (nat, low);
    }

    static int Least(nint w)
    {
        int min, nat;
        N.gtk_widget_measure(w, Horizontal, -1, &min, &nat, null, null);
        return min;
    }

    static int Down(nint w, int width)
    {
        int min, nat;
        N.gtk_widget_measure(w, Vertical, width, &min, &nat, null, null);
        return min;
    }

    void Hide(Room.Part parts)
    {
        hidden = parts;
        bool Shows(Room.Part p) => (parts & p) == 0;
        foreach (var h in hints) Show(h, Shows(Room.Part.Hints));
        Show(pills, Shows(Room.Part.Visualizer));
        WantLevels();
        var focus = N.gtk_root_get_focus(window);
        if (Show(nowPanel, Shows(Room.Part.Panel)) && focus != 0 && N.gtk_widget_is_ancestor(focus, nowPanel))
            FocusList(); // the focus mustn't stay on something hidden
        Show(elapsed, Shows(Room.Part.Times));
        Show(total, Shows(Room.Part.Times));
    }

    // ---- fading ------------------------------------------------------------------

    // As the queue's × fades in and out (opacity 200ms, ease).
    const double FadeTime = 0.2;

    // A part's fade: where it is (Shown), and while Running, from where to where since when.
    sealed class Fade
    {
        public double From, To, Start, Shown = 1;
        public bool Running;
    }

    readonly Dictionary<nint, Fade> fades = [];
    uint fadeTick;
    bool fading; // from half a second in (Activate): it opens as it is

    Fade FadeOf(nint part)
    {
        if (!fades.TryGetValue(part, out var f)) fades[part] = f = new();
        return f;
    }

    // Shows or hides a part; true if it changed. Hiding, it leaves its row
    // at once and fades out where it was, then stops being child-visible;
    // showing, it's back in its row at once and fades in.
    bool Show(nint part, bool on)
    {
        if ((N.gtk_widget_get_child_visible(part) && !Wrap.Leaving(part)) == on) return false;
        var fade = fading && animations;
        // Nothing on its way out takes a click or the focus.
        N.gtk_widget_set_can_target(part, on);
        N.gtk_widget_set_can_focus(part, on);
        N.gtk_widget_set_child_visible(part, on || fade);
        Wrap.Leave(part, !on && fade);
        N.gtk_widget_queue_resize(N.gtk_widget_get_parent(part)); // its row lays out without it, or with it again
        var f = FadeOf(part);
        if (fade)
        {
            // From where it is now, so a part that turns back mid-fade doesn't jump.
            (f.From, f.To, f.Start, f.Running) = (f.Shown, on ? 1 : 0, Clock, true);
            if (fadeTick == 0) fadeTick = Ticking(window, FadeStep);
        }
        else
        {
            f.Running = false;
            Opacity(part, 1);
        }
        return true;
    }

    // One frame of every running fade; done, a part faded out is hidden.
    bool FadeStep(double now)
    {
        var running = false;
        foreach (var (part, f) in fades)
        {
            if (!f.Running) continue;
            var p = Math.Clamp((now - f.Start) / FadeTime, 0, 1);
            Opacity(part, f.From + (f.To - f.From) * Ease.Css.At(p));
            if (p < 1) { running = true; continue; }
            f.Running = false;
            if (f.To > 0) continue;
            Wrap.Leave(part, false);
            N.gtk_widget_set_child_visible(part, false);
        }
        if (!running) fadeTick = 0;
        return running;
    }

    // A part's fade; the visualizer, off (v), stays out of sight as it fades.
    void Opacity(nint part, double shown)
    {
        FadeOf(part).Shown = shown;
        N.gtk_widget_set_opacity(part, part == pills && !vizOn ? 0 : shown);
    }

    // The window's least size is the last level's, whatever its width.
    [UnmanagedCallersOnly] static int FitMode(nint widget) => 2; // GTK_SIZE_REQUEST_CONSTANT_SIZE

    // As Wrap's, no exception gets into GTK: a failed measure asks for nothing.
    [UnmanagedCallersOnly]
    static void MeasureFit(nint widget, int orientation, int forSize, int* min, int* nat, int* minBaseline, int* natBaseline)
    {
        *min = *nat = 0;
        *minBaseline = *natBaseline = -1;
        try
        {
            var a = fitted!;
            var least = a.Need(Room.Hidden(Room.Levels - 1), -1);
            if (orientation == Horizontal) *min = least.Width;
            else
            {
                // Too short, only the panel hides: tall enough for what each
                // level by width still shows without it.
                *min = least.Height;
                for (int l = 0; l < Room.Levels - 1; l++)
                    *min = Math.Max(*min, a.Need(Room.Hidden(l) | Room.Part.Panel, a.Need(Room.Hidden(l), -1).Width).Height);
            }
            int m, n;
            N.gtk_widget_measure(N.gtk_widget_get_first_child(widget), orientation, -1, &m, &n, null, null);
            *nat = Math.Max(*min, n);
        }
        catch (Exception e) { Console.Error.WriteLine(e); }
    }

    // Picks what to hide for this size and shows the rest before laying out:
    // the rows they're in measure again in this same pass.
    [UnmanagedCallersOnly]
    static void AllocateFit(nint widget, int width, int height, int baseline)
    {
        try
        {
            var a = fitted!;
            Span<(int Width, int Height)> needs = stackalloc (int, int)[Room.Levels];
            for (int l = 0; l < needs.Length; l++) needs[l] = a.Need(Room.Hidden(l), width);
            var parts = Room.Hides(needs, width, height);
            if (parts != a.hidden) a.Hide(parts);
            var child = N.gtk_widget_get_first_child(widget);
            int m, n;
            N.gtk_widget_measure(child, Vertical, width, &m, &n, null, null); // GTK wants children measured before they're allocated
            N.gtk_widget_allocate(child, width, Math.Max(height, m), -1, 0);
        }
        catch (Exception e) { Console.Error.WriteLine(e); }
    }
}

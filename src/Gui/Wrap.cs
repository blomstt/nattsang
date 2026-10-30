using System.Runtime.InteropServices;
using Nattsang.Core;
using static Nattsang.Gui.Ui;

namespace Nattsang.Gui;

// A row that wraps, as a CSS `display: flex; flex-wrap: wrap` box does:
// children go left to right at their basis width, a child that doesn't fit
// starts a new line, and each line's spare width goes to the children that
// grow, by weight (that part is Core's Flex). Lines stack with a gap; with
// height to spare they share it (align-content: stretch), short of their
// natural heights they give it up toward their least. In a line, children
// fill its height (stretch), sit in the middle of it (center) or at its
// bottom (end). A child the window has hidden (not child-visible, see
// App.Fit) takes no room; one on its way out (Leave) takes none either, and
// stays where it was last laid out while it fades.
static unsafe class Wrap
{
    public enum Align { Stretch, Center, End }

    // A row's settings, and its children and lines as last measured: kept
    // per row and reused, as a row's measure runs its children's (nested
    // rows) before it's done with its own.
    sealed class Spec(int columnGap, int rowGap, Align align)
    {
        public readonly int ColumnGap = columnGap, RowGap = rowGap;
        public readonly Align Align = align;
        public readonly List<nint> Widgets = [];
        public readonly List<Flex.Item> Items = [];
        public readonly List<(int From, int To)> Lines = [];

        public Span<Flex.Item> Children(nint box)
        {
            Widgets.Clear();
            Items.Clear();
            for (var c = N.gtk_widget_get_first_child(box); c != 0; c = N.gtk_widget_get_next_sibling(c))
            {
                if (!N.gtk_widget_get_visible(c) || !N.gtk_widget_get_child_visible(c) || Leaving(c)) continue;
                int min, nat;
                N.gtk_widget_measure(c, Horizontal, -1, &min, &nat, null, null);
                var (grow, basis) = items.GetValueOrDefault(c);
                Widgets.Add(c);
                Items.Add(new Flex.Item { Min = min, Basis = Math.Max(min, basis > 0 ? basis : nat), Grow = grow });
            }
            return CollectionsMarshal.AsSpan(Items);
        }
    }

    static readonly Dictionary<nint, Spec> specs = [];
    static readonly Dictionary<nint, (double Grow, int Basis)> items = [];
    static readonly Dictionary<nint, Spot> spots = [];

    // Where a child was last laid out in its row, and whether it's on its way out.
    sealed class Spot
    {
        public float X, Y;
        public int Width, Height;
        public bool Leaving;
    }

    static Spot SpotOf(nint child)
    {
        if (!spots.TryGetValue(child, out var at)) spots[child] = at = new();
        return at;
    }

    // A child on its way out: the others lay out without it, and it stays
    // where it was, clipped to its row, until it's hidden or comes back.
    public static void Leave(nint child, bool on)
    {
        var at = SpotOf(child);
        if (at.Leaving == on) return;
        at.Leaving = on;
        var row = N.gtk_widget_get_parent(child);
        var any = false;
        for (var c = N.gtk_widget_get_first_child(row); c != 0 && !any; c = N.gtk_widget_get_next_sibling(c)) any = Leaving(c);
        N.gtk_widget_set_overflow(row, any ? 1 : 0); // GTK_OVERFLOW_HIDDEN while one is, so it can't spill past the row
        N.gtk_widget_queue_resize(row);
    }

    public static bool Leaving(nint child) => spots.TryGetValue(child, out var at) && at.Leaving;

    public static nint Row(int columnGap, int rowGap, Align align, params string[] classes)
    {
        var box = Box(Horizontal, 0, classes);
        specs[box] = new(columnGap, rowGap, align);
        N.gtk_widget_set_layout_manager(box, N.gtk_custom_layout_new(
            (nint)(delegate* unmanaged<nint, int>)&RequestMode,
            (nint)(delegate* unmanaged<nint, int, int, int*, int*, int*, int*, void>)&Measure,
            (nint)(delegate* unmanaged<nint, int, int, int, void>)&Allocate));
        return box;
    }

    // flex-grow and flex-basis (0: the child's natural width).
    public static nint Item(nint child, double grow, int basis = 0)
    {
        items[child] = (grow, basis);
        return child;
    }

    static int Height(nint child, int width, bool natural)
    {
        int min, nat;
        N.gtk_widget_measure(child, Vertical, width, &min, &nat, null, null);
        return natural ? nat : min;
    }

    // Each line's least and natural height (its tallest child's), and the
    // row's totals with the gaps between its lines.
    static (int Min, int Nat) LineHeights(Spec spec, Span<Flex.Item> children, Span<int> lows, Span<int> highs)
    {
        int lo = spec.RowGap * (spec.Lines.Count - 1), hi = lo;
        for (int l = 0; l < spec.Lines.Count; l++)
        {
            lows[l] = highs[l] = 0;
            for (int i = spec.Lines[l].From; i < spec.Lines[l].To; i++)
            {
                int min, nat;
                N.gtk_widget_measure(spec.Widgets[i], Vertical, children[i].Width, &min, &nat, null, null);
                lows[l] = Math.Max(lows[l], min);
                highs[l] = Math.Max(highs[l], nat);
            }
            lo += lows[l];
            hi += highs[l];
        }
        return (lo, hi);
    }

    // A row's height depends on its width (where its lines break). The
    // window doesn't take its least size from the rows (App.Fit), so a row
    // asking height-for-width can't hold the window tall.
    [UnmanagedCallersOnly] static int RequestMode(nint widget) => 0; // GTK_SIZE_REQUEST_HEIGHT_FOR_WIDTH

    // Layout callbacks never let an exception into GTK: a row that fails
    // to measure asks for nothing, one that fails to allocate is left as it was.
    [UnmanagedCallersOnly]
    static void Measure(nint widget, int orientation, int forSize, int* min, int* nat, int* minBaseline, int* natBaseline)
    {
        *min = *nat = 0;
        *minBaseline = *natBaseline = -1;
        try
        {
            var spec = specs[widget];
            var children = spec.Children(widget);
            if (children.Length == 0) return;
            if (orientation == Horizontal)
            {
                foreach (var c in children) *min = Math.Max(*min, c.Min);
                *nat = Flex.Natural(children, spec.ColumnGap);
                return;
            }
            var width = forSize >= 0 ? forSize : Flex.Natural(children, spec.ColumnGap);
            Flex.Lines(children, width, spec.ColumnGap, spec.Lines);
            Span<int> lows = stackalloc int[spec.Lines.Count], highs = stackalloc int[spec.Lines.Count];
            var (lo, hi) = LineHeights(spec, children, lows, highs);
            *min = lo;
            *nat = hi;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            *min = *nat = 0;
        }
    }

    [UnmanagedCallersOnly]
    static void Allocate(nint widget, int width, int height, int baseline)
    {
        try
        {
            Leavers(widget);
            var spec = specs[widget];
            var children = spec.Children(widget);
            if (children.Length == 0) return;
            var lines = spec.Lines;
            Flex.Lines(children, width, spec.ColumnGap, lines);
            Span<int> heights = stackalloc int[lines.Count], highs = stackalloc int[lines.Count];
            var (low, high) = LineHeights(spec, children, heights, highs);
            // Each line from its least height: up to its natural one as far
            // as the height goes (by how much each can grow), then an even
            // share of what's left.
            var spare = Math.Max(0, Math.Min(height, high) - low);
            for (int l = 0; l < lines.Count; l++)
                if (high > low) heights[l] += (int)((long)(highs[l] - heights[l]) * spare / (high - low));
            var extra = Math.Max(0, height - high);
            float y = 0;
            var point = stackalloc float[2];
            for (int l = 0; l < lines.Count; l++)
            {
                var lineHeight = heights[l] + extra / lines.Count + (l < extra % lines.Count ? 1 : 0);
                float x = 0;
                for (int i = lines[l].From; i < lines[l].To; i++)
                {
                    var w = spec.Widgets[i];
                    var cw = children[i].Width;
                    var h = spec.Align == Align.Stretch ? lineHeight : Math.Min(lineHeight, Height(w, cw, true));
                    point[0] = x;
                    point[1] = y + (spec.Align == Align.End ? lineHeight - h : (lineHeight - h) / 2f);
                    N.gtk_widget_allocate(w, cw, h, -1, N.gsk_transform_translate(0, point));
                    var at = SpotOf(w);
                    (at.X, at.Y, at.Width, at.Height) = (point[0], point[1], cw, h);
                    x += cw + spec.ColumnGap;
                }
                y += lineHeight + spec.RowGap;
            }
        }
        catch (Exception e) { Console.Error.WriteLine(e); }
    }

    // The children on their way out, where they were last laid out.
    static void Leavers(nint row)
    {
        var point = stackalloc float[2];
        for (var c = N.gtk_widget_get_first_child(row); c != 0; c = N.gtk_widget_get_next_sibling(c))
        {
            if (!spots.TryGetValue(c, out var at) || !at.Leaving || at.Width == 0 || !N.gtk_widget_get_child_visible(c)) continue;
            Height(c, at.Width, false); // GTK wants children measured before they're allocated
            point[0] = at.X;
            point[1] = at.Y;
            N.gtk_widget_allocate(c, at.Width, at.Height, -1, N.gsk_transform_translate(0, point));
        }
    }
}

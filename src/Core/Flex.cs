namespace Nattsang.Core;

// How a CSS `display: flex; flex-wrap: wrap` row breaks into lines and sizes
// its children: children go left to right at their basis width, a child that
// doesn't fit starts a new line, and each line's spare width goes to the
// children that grow, by weight. The window's Wrap rows lay out with it.
public static class Flex
{
    public struct Item
    {
        public int Min, Basis;
        public double Grow;
        public int Width; // set by Lines
    }

    // The width that fits every item on one line.
    public static int Natural(ReadOnlySpan<Item> items, int gap)
    {
        if (items.Length == 0) return 0;
        var sum = gap * (items.Length - 1);
        foreach (var c in items) sum += c.Basis;
        return sum;
    }

    // Splits the items into lines for width (into lines, cleared first) and
    // sets each one's width.
    public static void Lines(Span<Item> items, int width, int gap, List<(int From, int To)> lines)
    {
        lines.Clear();
        int start = 0, used = 0;
        for (int i = 0; i < items.Length; i++)
        {
            var w = items[i].Basis;
            if (i > start && used + gap + w > width)
            {
                lines.Add((start, i));
                start = i;
                used = 0;
            }
            used += (i > start ? gap : 0) + w;
        }
        if (start < items.Length) lines.Add((start, items.Length));

        foreach (var (from, to) in lines)
        {
            int sum = 0;
            double grow = 0;
            for (int i = from; i < to; i++) { sum += items[i].Basis; grow += items[i].Grow; }
            var free = width - sum - gap * (to - from - 1);
            for (int i = from; i < to; i++)
            {
                ref var c = ref items[i];
                if (free >= 0) c.Width = c.Basis + (grow > 0 ? (int)Math.Floor(free * c.Grow / grow) : 0);
                else c.Width = Math.Max(c.Min, c.Basis + free * c.Basis / Math.Max(1, sum)); // shrink, as flex-shrink: 1 does
            }
        }
    }
}

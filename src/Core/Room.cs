namespace Nattsang.Core;

// What the window has room for. When it's too narrow for everything, parts
// hide one at a time, always in this order, and come back in reverse as it
// grows: the key hints, the visualizer, the lyrics | queue panel, then the
// times beside the progress bar. When it's too short, only the panel hides:
// it's what holds the window tall. The library never hides.
public static class Room
{
    [Flags] public enum Part { None = 0, Hints = 1, Visualizer = 2, Panel = 4, Times = 8 }

    // Levels 0 (everything shows) to 4 (all four parts hidden).
    public const int Levels = 5;

    // The parts a level hides: the first level of them, in the order.
    public static Part Hidden(int level) => (Part)((1 << Math.Clamp(level, 0, Levels - 1)) - 1);

    // The fewest parts to hide in width × height, given what each level
    // needs (the window's size with that level's parts hidden): the first
    // level whose width fits (the last when none does), and the panel too
    // when that level is taller than height.
    public static Part Hides(ReadOnlySpan<(int Width, int Height)> needs, int width, int height)
    {
        var level = 0;
        while (level < needs.Length - 1 && needs[level].Width > width) level++;
        var parts = Hidden(level);
        if (needs[level].Height > height) parts |= Part.Panel;
        return parts;
    }
}

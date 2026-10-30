namespace Nattsang.Core;

// Synced lyrics as the design shows them: one line every four seconds, the
// current one held on the seventh of fourteen visible rows, the others toned
// down by how far they are from it. The lines are placeholders until real
// lyrics come.
public static class Lyrics
{
    public const double LineSeconds = 4;
    public const int LineHeight = 28, Anchor = 6;

    public enum Tone { Current, Next, Near, Far }

    // Enough lines to cover the track: 48 for 3:07.
    public static string[] Placeholder(int trackSeconds)
    {
        var n = Math.Max(0, trackSeconds) / (int)LineSeconds + 2;
        var lines = new string[n];
        for (int i = 0; i < n; i++) lines[i] = $"[lyric line {i + 1}]";
        return lines;
    }

    public static int LineAt(double position, int lineCount)
    {
        if (lineCount <= 0) return 0;
        if (!(position > 0)) return 0;
        return (int)Math.Min(lineCount - 1, Math.Floor(position / LineSeconds));
    }

    // distance = line - current line
    public static Tone ToneAt(int distance) => distance switch
    {
        0 => Tone.Current,
        1 => Tone.Next,
        -1 or 2 => Tone.Near,
        _ => Tone.Far,
    };

    // How far the lines move up so the current one sits on the anchor row.
    public static double Offset(int line) => -(line - Anchor) * LineHeight;
}

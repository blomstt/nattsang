using System.Globalization;

namespace Nattsang.Core;

// Lyrics as the design shows them: the current line held on the seventh of
// fourteen visible rows, the others toned down by how far they are from it.
// Synced lyrics (LRC, from LRCLIB) say when each line comes; the sample
// album's placeholders come one every four seconds.
public static class Lyrics
{
    public const double LineSeconds = 4;
    public const int LineHeight = 28, Anchor = 6;

    // What stands for a line with no words: a break in the song, or its intro.
    public const string Break = "♪";

    public enum Tone { Current, Next, Near, Far }

    // What LRCLIB has for a song: lines with their times, lines without, a
    // song with no words, or nothing.
    public enum Kind { Synced, Plain, Instrumental, None }

    // A song's lyrics, ready to show: Times[i] is when Lines[i] starts, in
    // seconds (synced only; empty for the others).
    public sealed record Song(Kind Kind, string[] Lines, double[] Times)
    {
        public static readonly Song Nothing = new(Kind.None, [], []);
        public static readonly Song Instrumental = new(Kind.Instrumental, [], []);
    }

    // Enough lines to cover the track: 48 for 3:07.
    public static string[] Placeholder(int trackSeconds)
    {
        var n = Math.Max(0, trackSeconds) / (int)LineSeconds + 2;
        var lines = new string[n];
        for (int i = 0; i < n; i++) lines[i] = $"[lyric line {i + 1}]";
        return lines;
    }

    // The placeholders' line: one every four seconds.
    public static int LineAt(double position, int lineCount)
    {
        if (lineCount <= 0) return 0;
        if (!(position > 0)) return 0;
        return (int)Math.Min(lineCount - 1, Math.Floor(position / LineSeconds));
    }

    // A synced song's line at position: the last one that has started, the
    // first before any has.
    public static int LineAt(double position, double[] times)
    {
        if (times.Length == 0 || !(position >= times[0])) return 0;
        int lo = 0, hi = times.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (times[mid] <= position) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    // Plain lyrics don't say when a line comes: they go by evenly, from the
    // first line at the start to the last at the end.
    public static int PacedAt(double position, double trackSeconds, int lineCount)
    {
        if (lineCount <= 0 || !(trackSeconds > 0) || !(position > 0)) return 0;
        return (int)Math.Min(lineCount - 1, Math.Floor(position / trackSeconds * lineCount));
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

    // ---- LRC ------------------------------------------------------------------------

    // LRC text: "[mm:ss.xx]words" a line, a line with several times shown at
    // each, in time order. [offset:±ms] moves them all (+ is earlier); other
    // tags ([ar:…]) and word times (<mm:ss.xx>) are left out. A line without
    // words is a break (♪), and so is the time before the first line, when
    // that's a second or more. Nothing timed in it: Kind.None.
    public static Song Synced(string lrc)
    {
        var found = new List<(double Time, int Order, string Text)>();
        double offset = 0;
        foreach (var raw in lrc.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var at = 0;
            var times = new List<double>();
            while (at < line.Length && line[at] == '[')
            {
                var close = line.IndexOf(']', at);
                if (close < 0) break;
                var tag = line.AsSpan(at + 1, close - at - 1);
                if (Stamp(tag) is { } t) times.Add(t);
                else if (tag.StartsWith("offset:", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(tag[7..].Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var ms))
                    offset = ms / 1000;
                at = close + 1;
            }
            if (times.Count == 0) continue;
            var text = Words(line.AsSpan(at));
            foreach (var t in times) found.Add((t, found.Count, text.Length > 0 ? text : Break));
        }
        if (found.Count == 0) return Song.Nothing;
        found.Sort((a, b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : a.Order.CompareTo(b.Order));
        var lines = new List<string>(found.Count + 1);
        var stamps = new List<double>(found.Count + 1);
        foreach (var (t, _, text) in found)
        {
            lines.Add(text);
            stamps.Add(Math.Max(0, t - offset));
        }
        if (stamps[0] >= 1 && lines[0] != Break)
        {
            lines.Insert(0, Break);
            stamps.Insert(0, 0);
        }
        return new Song(Kind.Synced, [.. lines], [.. stamps]);
    }

    // Plain lyrics: their lines as they are, without the blank ones at the
    // start and the end. Nothing in them: Kind.None.
    public static Song Plain(string text)
    {
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r').Trim()).ToList();
        while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines.Count == 0 ? Song.Nothing : new Song(Kind.Plain, [.. lines], []);
    }

    // "mm:ss", "mm:ss.x…" or "mm:ss:xx" → seconds; null for any other tag.
    static double? Stamp(ReadOnlySpan<char> tag)
    {
        var colon = tag.IndexOf(':');
        if (colon < 1 || !int.TryParse(tag[..colon], NumberStyles.None, CultureInfo.InvariantCulture, out var m)) return null;
        var rest = tag[(colon + 1)..];
        var cut = rest.IndexOfAny('.', ':');
        var whole = cut < 0 ? rest : rest[..cut];
        if (whole.Length is < 1 or > 2 || !int.TryParse(whole, NumberStyles.None, CultureInfo.InvariantCulture, out var s) || s > 59) return null;
        double fraction = 0;
        if (cut >= 0)
        {
            var digits = rest[(cut + 1)..];
            if (digits.Length is < 1 or > 3 || !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var f)) return null;
            fraction = f / Math.Pow(10, digits.Length);
        }
        return m * 60 + s + fraction;
    }

    // A line's words without the word times of enhanced LRC.
    static string Words(ReadOnlySpan<char> text)
    {
        if (text.IndexOf('<') < 0) return text.Trim().ToString();
        var b = new System.Text.StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '<')
            {
                var close = text[i..].IndexOf('>');
                if (close > 0 && Stamp(text.Slice(i + 1, close - 1)) != null) { i += close; continue; }
            }
            b.Append(text[i]);
        }
        return string.Join(' ', b.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

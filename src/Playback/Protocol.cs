using System.Globalization;
using Nattsang.Core;

namespace Nattsang.Playback;

// The lines between the window and the helper (docs/protocol.md): one
// message a line, its fields separated by tabs, the first field saying what
// it is. A field can't hold a tab or a line break: Line makes them spaces.
public static class Protocol
{
    public const string Version = "1";

    // A command, ready to write: the fields and the line break.
    public static string Line(params string[] fields) => string.Join('\t', fields.Select(Clean)) + "\n";

    static string Clean(string field) =>
        field.AsSpan().IndexOfAny('\t', '\n', '\r') < 0 ? field : field.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    // An event's fields, without the line break.
    public static string[] Fields(string line) => line.TrimEnd('\n', '\r').Split('\t');

    // Milliseconds as the helper writes them ("72000") → seconds; false for
    // anything else (negative, a fraction, a sign, empty).
    public static bool Seconds(string ms, out double seconds)
    {
        var ok = long.TryParse(ms, NumberStyles.None, CultureInfo.InvariantCulture, out var v);
        seconds = ok ? v / 1000.0 : 0;
        return ok;
    }

    public static string Milliseconds(double seconds) =>
        ((long)Math.Max(0, Math.Round(seconds * 1000))).ToString(CultureInfo.InvariantCulture);

    // item-track <uri> <title> <artist> <album> <ms> [<album uri> <artist uri>],
    // the number'th of its list; null when a field is missing or the length
    // isn't a number.
    public static Track? ItemTrack(string[] f, int number)
    {
        if (f.Length < 6 || f[1].Length == 0 || !Seconds(f[5], out var seconds)) return null;
        string F(int i) => i < f.Length ? f[i] : "";
        return new Track(f[1], number, f[2], f[3], f[4], (int)seconds, F(6), F(7));
    }

    // levels <heard at, µs on the monotonic clock> <28 levels, 0 to 100>:
    // when it's heard in seconds, and the levels from 0 to 1; false when a
    // field is missing or isn't a number in range.
    public static bool Levels(string[] f, out double at, out float[] values)
    {
        at = 0;
        values = [];
        if (f.Length < 2 + Core.Levels.Bands || !long.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out var us)) return false;
        var v = new float[Core.Levels.Bands];
        for (int i = 0; i < v.Length; i++)
        {
            if (!int.TryParse(f[2 + i], NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n > 100) return false;
            v[i] = n / 100f;
        }
        at = us / 1e6;
        values = v;
        return true;
    }

    // item-album <uri> <name> <artist> <year>, item-artist <uri> <name>,
    // item-playlist <uri> <name> <owner> <tracks>: a row that opens
    // something; null without a uri.
    public static Entry? ItemEntry(string[] f)
    {
        if (f.Length < 3 || f[1].Length == 0) return null;
        string F(int i) => i < f.Length ? f[i] : "";
        return new Entry(f[1], f[2], F(3), F(4));
    }
}

namespace Nattsang.Core;

// Times as the window shows them: m:ss, minutes never wrapped into hours.
public static class Time
{
    public static string Format(double seconds)
    {
        var s = seconds <= 0 || double.IsNaN(seconds) ? 0 : (long)seconds;
        return $"{s / 60}:{s % 60:00}";
    }

    // "3:07" → 187. Throws on anything else: only the sample data comes in.
    public static int Parse(string text)
    {
        var colon = text.IndexOf(':');
        if (colon < 1 || text.Length - colon != 3) throw new FormatException($"not m:ss: {text}");
        var m = int.Parse(text.AsSpan(0, colon));
        var s = int.Parse(text.AsSpan(colon + 1));
        if (m < 0 || s is < 0 or > 59) throw new FormatException($"not m:ss: {text}");
        return m * 60 + s;
    }
}

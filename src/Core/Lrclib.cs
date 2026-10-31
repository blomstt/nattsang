using System.Text;

namespace Nattsang.Core;

// LRCLIB (lrclib.net), the free lyrics database: no key, a User-Agent that
// says who asks. A song is looked up by its exact name, artist, album and
// length first (/api/get, which matches lengths within two seconds); when
// it isn't found that way, by a search on its name and first artist, and
// the answer picked from what that finds (Choose).
public static class Lrclib
{
    public const string Site = "https://lrclib.net";
    public static readonly string UserAgent =
        $"Nattsang/{typeof(Lrclib).Assembly.GetName().Version?.ToString(3) ?? "0"} (https://github.com/blomstt/nattsang)";

    // What LRCLIB said about a song, as it said it: the kind, and the LRC or
    // plain text ("" for instrumental and none). The cache keeps this.
    public sealed record Answer(Lyrics.Kind Kind, string Text)
    {
        public static readonly Answer None = new(Lyrics.Kind.None, "");

        public Lyrics.Song Song() => Kind switch
        {
            Lyrics.Kind.Synced => Lyrics.Synced(Text),
            Lyrics.Kind.Plain => Lyrics.Plain(Text),
            Lyrics.Kind.Instrumental => Lyrics.Song.Instrumental,
            _ => Lyrics.Song.Nothing,
        };
    }

    // /api/get needs all four; without an album or a length it can't match.
    public static bool CanGet(Track t) => t.Title.Length > 0 && t.Artist.Length > 0 && t.Album.Length > 0 && t.Seconds > 0;

    public static string GetUrl(Track t) =>
        $"{Site}/api/get?track_name={E(t.Title)}&artist_name={E(t.Artist)}&album_name={E(t.Album)}&duration={t.Seconds}";

    // The search goes by the name without what the service adds to it
    // (" - Remastered 2011", "(feat. …)") and by the first artist only.
    public static string SearchUrl(Track t) =>
        $"{Site}/api/search?track_name={E(SearchName(t.Title))}&artist_name={E(FirstArtist(t.Artist))}";

    // Percent-encoded UTF-8: letters, digits and -_.~ as they are.
    static string E(string s)
    {
        var b = new StringBuilder();
        foreach (var c in Encoding.UTF8.GetBytes(s))
            if (char.IsAsciiLetterOrDigit((char)c) || c is (byte)'-' or (byte)'_' or (byte)'.' or (byte)'~') b.Append((char)c);
            else b.Append('%').Append(c.ToString("X2"));
        return b.ToString();
    }

    // One record (/api/get's answer, or one of a search's): synced lyrics if
    // it has some that parse, else plain, else instrumental; null when it
    // has none of them.
    public static Answer? FromRecord(object? record)
    {
        var a = Words(record);
        // Some records are the one word "Instrumental", timed or not.
        return a != null && a.Song().Lines.Where(l => l != Lyrics.Break).ToList() is [var only]
            && string.Concat(only.Where(char.IsLetter)).Equals("instrumental", StringComparison.OrdinalIgnoreCase)
            ? new(Lyrics.Kind.Instrumental, "") : a;
    }

    static Answer? Words(object? record)
    {
        var synced = Json.Text(record, "syncedLyrics");
        if (!string.IsNullOrWhiteSpace(synced) && Lyrics.Synced(synced).Kind == Lyrics.Kind.Synced) return new(Lyrics.Kind.Synced, synced);
        var plain = Json.Text(record, "plainLyrics");
        if (!string.IsNullOrWhiteSpace(plain)) return new(Lyrics.Kind.Plain, plain);
        if (!string.IsNullOrWhiteSpace(synced)) return new(Lyrics.Kind.Plain, StripTimes(synced));
        return Json.Flag(record, "instrumental") ? new(Lyrics.Kind.Instrumental, "") : null;
    }

    // What a search found, for the track: only records of the same name
    // count. One as long as it (within three seconds) gives its lyrics as
    // they are; if none is, the words of any other, unsynced (a live or an
    // edited version has the same words at other times); else nothing.
    public static Answer Choose(Track t, object? found)
    {
        if (found is not List<object?> records) return Answer.None;
        var name = Normal(t.Title);
        var same = records.Where(r => name.Length == 0 || Normal(Json.Text(r, "trackName") ?? "") == name).ToList();
        var close = same.Where(r => Json.Number(r, "duration") is { } d && Math.Abs(d - t.Seconds) <= 3);
        var best = close.Select(FromRecord).Where(a => a != null).OrderBy(a => Rank(a!.Kind)).FirstOrDefault();
        if (best != null) return best;
        foreach (var r in same)
            if (FromRecord(r) is { Kind: Lyrics.Kind.Synced or Lyrics.Kind.Plain } words)
                return words.Kind == Lyrics.Kind.Plain ? words : new(Lyrics.Kind.Plain, StripTimes(words.Text));
        return same.Any(r => Json.Flag(r, "instrumental")) ? new(Lyrics.Kind.Instrumental, "") : Answer.None;
    }

    static int Rank(Lyrics.Kind k) => k switch { Lyrics.Kind.Synced => 0, Lyrics.Kind.Plain => 1, _ => 2 };

    // LRC without its times: the words, a line each.
    static string StripTimes(string lrc) => string.Join('\n', Lyrics.Synced(lrc).Lines.Where(l => l != Lyrics.Break));

    // A name to compare: what's in brackets and after " - " left out, then
    // only its letters and digits, lower case. "Don’t Stay - Live" and
    // "Don't Stay" are both "dontstay".
    public static string Normal(string title)
    {
        var t = Cut(title, " - ");
        var b = new StringBuilder();
        var depth = 0;
        foreach (var c in t)
        {
            if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth = Math.Max(0, depth - 1);
            else if (depth == 0 && char.IsLetterOrDigit(c)) b.Append(char.ToLowerInvariant(c));
        }
        return b.ToString();
    }

    // The name without " - …" and without a bracket that names who it's with.
    public static string SearchName(string title)
    {
        var t = Cut(title, " - ");
        foreach (var open in new[] { " (feat", " [feat", " (with ", " [with ", " (ft.", " [ft." })
        {
            var at = t.IndexOf(open, StringComparison.OrdinalIgnoreCase);
            if (at > 0) t = t[..at];
        }
        return t.Trim();
    }

    // "A, B" → "A": the service joins a track's artists with ", ".
    public static string FirstArtist(string artists) => Cut(artists, ", ").Trim();

    static string Cut(string s, string at)
    {
        var i = s.IndexOf(at, StringComparison.Ordinal);
        return i > 0 ? s[..i] : s;
    }
}

// What LRCLIB said about each song, on disk, so a song is asked about once:
// a file a track in ~/.cache/nattsang/lyrics (or $XDG_CACHE_HOME's), its
// first line the kind, the rest the text. That nothing was found is kept a
// week, then asked again, in case someone has added it since: its first
// line says the day it was asked ("none 20392", days since 1970), as the
// file's own time would bring time zones into the binary.
public sealed class LyricsCache(string dir)
{
    public const int MissDays = 7;

    public string Dir => dir;

    public static string? Default()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var home = Environment.GetEnvironmentVariable("HOME");
        var bas = !string.IsNullOrEmpty(xdg) ? xdg : !string.IsNullOrEmpty(home) ? Path.Combine(home, ".cache") : null;
        return bas == null ? null : Path.Combine(bas, "nattsang", "lyrics");
    }

    // A file name for the track: its id for one of the service's
    // ("spotify-track-<id>"), else a hash of what it's looked up by.
    public static string Key(Track t)
    {
        const string prefix = "spotify:track:";
        if (t.Uri.StartsWith(prefix, StringComparison.Ordinal) && t.Uri.Length > prefix.Length
            && t.Uri.AsSpan(prefix.Length).IndexOfAnyExcept("0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ") < 0)
            return "spotify-track-" + t.Uri[prefix.Length..];
        // FNV-1a over the UTF-8 of name, artist, album and length.
        ulong h = 14695981039346656037;
        foreach (var b in Encoding.UTF8.GetBytes($"{t.Title}\n{t.Artist}\n{t.Album}\n{t.Seconds}"))
            h = (h ^ b) * 1099511628211;
        return $"song-{h:x16}";
    }

    public static string Format(Lrclib.Answer a, long day) =>
        a.Kind == Lyrics.Kind.None ? $"none {day}\n" : $"{Name(a.Kind)}\n{a.Text}";

    // The answer, and for a miss the day it was asked (else 0).
    public static Lrclib.Answer? Parse(string file, out long day)
    {
        var nl = file.IndexOf('\n');
        var head = nl < 0 ? file : file[..nl];
        var text = nl < 0 ? "" : file[(nl + 1)..];
        day = 0;
        if (head.StartsWith("none ", StringComparison.Ordinal))
        {
            if (!long.TryParse(head.AsSpan(5), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out day)) return null;
            head = "none";
        }
        Lyrics.Kind? kind = head switch
        {
            "synced" => Lyrics.Kind.Synced,
            "plain" => Lyrics.Kind.Plain,
            "instrumental" => Lyrics.Kind.Instrumental,
            "none" => Lyrics.Kind.None,
            _ => null,
        };
        return kind is { } k ? new(k, text) : null;
    }

    static string Name(Lyrics.Kind k) => k switch
    {
        Lyrics.Kind.Synced => "synced",
        Lyrics.Kind.Plain => "plain",
        Lyrics.Kind.Instrumental => "instrumental",
        _ => "none",
    };

    // What's kept for the track; null when nothing is, it can't be read, or
    // it's a miss older than MissKept.
    public Lrclib.Answer? Load(Track t, DateTime nowUtc)
    {
        var path = Path.Combine(dir, Key(t));
        try
        {
            if (!File.Exists(path)) return null;
            var a = Parse(File.ReadAllText(path), out var day);
            if (a?.Kind == Lyrics.Kind.None && Day(nowUtc) - day > MissDays) return null;
            return a;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    // Written whole or not at all: to a file beside it, then renamed.
    static long Day(DateTime utc) => utc.Ticks / TimeSpan.TicksPerDay - 719162; // 0001-01-01 to 1970-01-01

    public void Save(Track t, Lrclib.Answer a) => Save(t, a, DateTime.UtcNow);

    public void Save(Track t, Lrclib.Answer a, DateTime nowUtc)
    {
        try
        {
            Directory.CreateDirectory(dir); // in ~/.cache/nattsang, which the helper keeps to its owner
            var path = Path.Combine(dir, Key(t));
            var part = path + ".part";
            File.WriteAllText(part, Format(a, Day(nowUtc)));
            File.Move(part, path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Console.Error.WriteLine($"nattsang: can't keep the lyrics: {e.Message}"); }
    }
}

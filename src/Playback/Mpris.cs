using Nattsang.Core;

namespace Nattsang.Playback;

// What the window tells the desktop over MPRIS (Gui/Mpris.cs puts it on
// D-Bus): media keys, playerctl and bars that show what plays. This is the
// mapping, without D-Bus: names, the state, the track's metadata.
public static class Mpris
{
    public const string Name = "org.mpris.MediaPlayer2.nattsang";
    public const string Identity = "Nattsang";
    public const string NoTrack = "/org/mpris/MediaPlayer2/TrackList/NoTrack";
    const string TrackPath = "/dev/blomst/Nattsang/track/";

    // The bus name: a second window (NATTSANG_APP_ID) is another instance
    // of the same player, so `playerctl -p nattsang` finds either.
    public static string BusName(bool second, int pid) => second ? $"{Name}.instance{pid}" : Name;

    // An object path, as D-Bus types it apart from a string.
    public readonly record struct ObjectPath(string Path);

    public static string Status(IPlayback p) => p.Playing ? "Playing" : p.Stopped ? "Stopped" : "Paused";

    // A track's id as an object path: its URI's letters, digits and
    // underscores, anything else made an underscore.
    public static string TrackId(Track t)
    {
        if (t.Uri.Length == 0) return NoTrack;
        var id = t.Uri.StartsWith("spotify:track:", StringComparison.Ordinal) ? t.Uri["spotify:track:".Length..] : t.Uri;
        return TrackPath + new string([.. id.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_')]);
    }

    public static long Microseconds(double seconds) => (long)Math.Round(Math.Max(0, seconds) * 1e6);

    // The track's page on the web, for a track of the service's; null for the sample.
    public static string? Url(Track t) =>
        t.Uri.StartsWith("spotify:track:", StringComparison.Ordinal) ? "https://open.spotify.com/track/" + t.Uri["spotify:track:".Length..] : null;

    // Metadata's a{sv}: each value a string, a long (x), a string[] (as) or
    // an ObjectPath (o). Nothing playing: only the trackid, NoTrack. The
    // artists stay one string as the service joins them ("A, B"): a name
    // can have a comma in it.
    public static List<(string Key, object Value)> Metadata(Track t, string cover)
    {
        var m = new List<(string, object)> { ("mpris:trackid", new ObjectPath(TrackId(t))) };
        if (t.Uri.Length == 0) return m;
        if (t.Seconds > 0) m.Add(("mpris:length", Microseconds(t.Seconds)));
        if (t.Title.Length > 0) m.Add(("xesam:title", t.Title));
        if (t.Artist.Length > 0) m.Add(("xesam:artist", new[] { t.Artist }));
        if (t.Album.Length > 0) m.Add(("xesam:album", t.Album));
        if (Url(t) is { } url) m.Add(("xesam:url", url));
        if (cover.Length > 0) m.Add(("mpris:artUrl", cover));
        return m;
    }

    // Seek(offset): where to go, or null for the next track (past the end,
    // as the spec says); before the start is the start.
    public static double? SeekTo(double position, long offsetMicroseconds, int trackSeconds)
    {
        var to = position + offsetMicroseconds / 1e6;
        if (trackSeconds > 0 && to > trackSeconds) return null;
        return Math.Max(0, to);
    }

    // SetPosition(trackid, position): only for the track that plays, and
    // only within it; null to do nothing.
    public static double? SetTo(Track t, string trackId, long microseconds)
    {
        if (t.Uri.Length == 0 || trackId != TrackId(t) || microseconds < 0) return null;
        var to = microseconds / 1e6;
        return t.Seconds > 0 && to > t.Seconds ? null : to;
    }
}

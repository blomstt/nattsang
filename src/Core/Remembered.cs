using System.Globalization;
using System.Text;

namespace Nattsang.Core;

// What Nattsang remembers between launches: its volume and how wide the
// playlist names are beside an open playlist. Kept in
// ~/.local/state/nattsang/state (or $XDG_STATE_HOME's), a line each:
//
//     volume 70
//     playlist-width 270
//
// A line it doesn't know, or a value that isn't a whole number, is left
// out, and a value out of range is brought into it, so a damaged file
// never stops it starting.
public sealed class Remembered
{
    public int Volume { get; set; } = Core.Volume.Full;
    public int PlaylistWidth { get; set; } = PlaylistColumn.Default;

    public static Remembered Parse(string text)
    {
        var r = new Remembered();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var space = line.IndexOf(' ');
            if (space < 0) continue;
            if (!int.TryParse(line.AsSpan(space + 1).Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v)) continue;
            switch (line[..space])
            {
                case "volume": r.Volume = Core.Volume.Clamp(v); break;
                case "playlist-width": r.PlaylistWidth = PlaylistColumn.Clamp(v); break;
            }
        }
        return r;
    }

    public string Format() => new StringBuilder()
        .Append("volume ").Append(Volume.ToString(CultureInfo.InvariantCulture)).Append('\n')
        .Append("playlist-width ").Append(PlaylistWidth.ToString(CultureInfo.InvariantCulture)).Append('\n')
        .ToString();

    public static string? FilePath()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        var home = Environment.GetEnvironmentVariable("HOME");
        var bas = !string.IsNullOrEmpty(xdg) ? xdg : !string.IsNullOrEmpty(home) ? Path.Combine(home, ".local", "state") : null;
        return bas == null ? null : Path.Combine(bas, "nattsang", "state");
    }

    // The defaults when there's no file or it can't be read.
    public static Remembered Load(string? path)
    {
        try { return path != null && File.Exists(path) ? Parse(File.ReadAllText(path)) : new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new(); }
    }

    // Written whole or not at all: to a file beside it, then renamed.
    public void Save(string? path)
    {
        if (path == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var part = path + ".part";
            File.WriteAllText(part, Format());
            File.Move(part, path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Console.Error.WriteLine($"nattsang: can't remember the volume and widths: {e.Message}"); }
    }
}

// The playlist names beside an open playlist: as wide as the divider is
// dragged, within Min and Max; narrower when the panel hasn't room for
// that and SongsMin of songs beside it.
public static class PlaylistColumn
{
    public const int Min = 180, Max = 520, Default = 270, SongsMin = 240;

    public static int Clamp(int width) => Math.Clamp(width, Min, Max);

    // How wide it's drawn in a split of width (the divider's Grip included).
    public static int Shown(int wanted, int width, int grip) => Math.Max(Min, Math.Min(Clamp(wanted), width - grip - SongsMin));
}

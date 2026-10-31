using System.Text;

namespace Nattsang.Core;

// The window's colours. Built in: the values the design was drawn with
// (matugen's colours on 2026-10-30). ~/.config/nattsang/colors.css, when it
// exists, overrides any of them, one GTK colour definition per name:
//
//     @define-color accent #80d4d9;
//
// Unknown names and values that aren't #rgb or #rrggbb are ignored, so a
// half-written file never breaks the window. The window reads it again
// whenever it changes (a matugen template can write it as the wallpaper
// changes: see the README). Comments are CSS's: /* to */,
// and one that's never closed runs to the end of the file.
public sealed class Palette
{
    // name → 0xRRGGBB, in the order the file documents them
    public static readonly (string Name, uint Rgb)[] Defaults =
    [
        ("background", 0x00010b), // window
        ("text", 0x958f92),       // body text
        ("bright", 0xada6a9),     // titles, hovered text
        ("accent", 0x80d4d9),     // keys, the selected tab, repeat
        ("playing", 0xffb1c4),    // what's playing: its title, row, play button, lyric line
        ("label", 0xf8bb71),      // the open library section, queue headings
        ("border", 0x34323a),     // panel borders and rules
        ("raised", 0x17161d),     // the key bar, hovered buttons, the playing row
        ("hover", 0x111018),      // hovered and selected list rows
        ("groove", 0x26252c),     // the progress bar's empty track
        ("dim", 0x7d787b),        // far lyric lines, the artist in the queue
        ("handle", 0x635f65),     // the queue's drag handles
        ("muted", 0x4a484e),      // the empty part of the volume meter
    ];

    readonly Dictionary<string, uint> colors;

    Palette(Dictionary<string, uint> colors) => this.colors = colors;

    public static Palette Default => new(Defaults.ToDictionary(d => d.Name, d => d.Rgb));

    public string Hex(string name) => $"#{colors[name]:x6}";

    public uint Text => colors["text"];
    public uint Bright => colors["bright"];
    public uint Accent => colors["accent"];
    public uint Playing => colors["playing"];
    public uint Label => colors["label"];
    public uint Groove => colors["groove"];
    public uint Dim => colors["dim"];
    public uint Border => colors["border"];
    public uint Background => colors["background"];

    // Every colour the same as other's: a file that changed but says the
    // same needs nothing done.
    public bool Same(Palette other) => Defaults.All(d => colors[d.Name] == other.colors[d.Name]);

    const string Define = "@define-color";

    // The defaults with css's definitions over them.
    public static Palette Parse(string css)
    {
        var p = Default;
        var text = WithoutComments(css);
        for (var at = text.IndexOf(Define, StringComparison.Ordinal); at >= 0; at = text.IndexOf(Define, at, StringComparison.Ordinal))
        {
            // @define-color, space, a name, space, a value, maybe space, ';'
            var i = at + Define.Length;
            at++;
            if (Skip(text, ref i, char.IsWhiteSpace) == 0) continue;
            var name = i;
            if (Skip(text, ref i, IsNameChar) == 0) continue;
            var nameEnd = i;
            if (Skip(text, ref i, char.IsWhiteSpace) == 0) continue;
            var value = i;
            if (Skip(text, ref i, c => c != ';' && !char.IsWhiteSpace(c)) == 0) continue;
            var valueEnd = i;
            Skip(text, ref i, char.IsWhiteSpace);
            if (i == text.Length || text[i] != ';') continue;
            at = i + 1;
            var key = text[name..nameEnd];
            if (p.colors.ContainsKey(key) && TryColor(text[value..valueEnd], out var rgb)) p.colors[key] = rgb;
        }
        return p;
    }

    static bool IsNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-';

    // Moves i past the chars that match; how many it passed.
    static int Skip(string text, ref int i, Func<char, bool> match)
    {
        var from = i;
        while (i < text.Length && match(text[i])) i++;
        return i - from;
    }

    // Each comment becomes a space, as CSS reads it.
    static string WithoutComments(string css)
    {
        var open = css.IndexOf("/*", StringComparison.Ordinal);
        if (open < 0) return css;
        var sb = new StringBuilder(css.Length);
        var i = 0;
        while (open >= 0)
        {
            sb.Append(css, i, open - i).Append(' ');
            var close = css.IndexOf("*/", open + 2, StringComparison.Ordinal);
            if (close < 0) return sb.ToString(); // never closed: the rest is comment
            i = close + 2;
            open = css.IndexOf("/*", i, StringComparison.Ordinal);
        }
        return sb.Append(css, i, css.Length - i).ToString();
    }

    public static string FilePath()
    {
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(config)) config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(config, "nattsang", "colors.css");
    }

    public static Palette Load()
    {
        var path = FilePath();
        try { return File.Exists(path) ? Parse(File.ReadAllText(path)) : Default; }
        catch (IOException) { return Default; }
        catch (UnauthorizedAccessException) { return Default; }
    }

    // #rgb or #rrggbb
    public static bool TryColor(string text, out uint rgb)
    {
        rgb = 0;
        if (text.Length is not (4 or 7) || text[0] != '#') return false;
        uint v = 0;
        for (int i = 1; i < text.Length; i++)
        {
            var d = HexDigit(text[i]);
            if (d < 0) return false;
            v = v << 4 | (uint)d;
        }
        if (text.Length == 4) v = (v >> 8 & 0xf) * 0x110000 | (v >> 4 & 0xf) * 0x1100 | (v & 0xf) * 0x11;
        rgb = v;
        return true;
    }

    static int HexDigit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };
}

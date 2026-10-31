using System.Globalization;
using System.Text;

namespace Nattsang.Core;

// Reads JSON into plain values: an object is a Dictionary<string, object?>,
// an array a List<object?>, then string, double, bool and null. Enough for
// LRCLIB's answers, without System.Text.Json's size in the binary.
public static class Json
{
    // The value text holds; FormatException when it isn't JSON.
    public static object? Parse(string text)
    {
        var at = 0;
        var value = Value(text, ref at);
        Space(text, ref at);
        if (at != text.Length) throw Bad(at);
        return value;
    }

    // A field of an object as a string, or null when it's missing or isn't one.
    public static string? Text(object? obj, string name) =>
        obj is Dictionary<string, object?> o && o.TryGetValue(name, out var v) ? v as string : null;

    public static double? Number(object? obj, string name) =>
        obj is Dictionary<string, object?> o && o.TryGetValue(name, out var v) && v is double d ? d : null;

    public static bool Flag(object? obj, string name) =>
        obj is Dictionary<string, object?> o && o.TryGetValue(name, out var v) && v is true;

    static object? Value(string s, ref int at)
    {
        Space(s, ref at);
        if (at >= s.Length) throw Bad(at);
        switch (s[at])
        {
            case '{':
                var obj = new Dictionary<string, object?>();
                at++;
                Space(s, ref at);
                if (Take(s, ref at, '}')) return obj;
                do
                {
                    Space(s, ref at);
                    if (at >= s.Length || s[at] != '"') throw Bad(at);
                    var key = String(s, ref at);
                    Space(s, ref at);
                    if (!Take(s, ref at, ':')) throw Bad(at);
                    obj[key] = Value(s, ref at);
                    Space(s, ref at);
                } while (Take(s, ref at, ','));
                if (!Take(s, ref at, '}')) throw Bad(at);
                return obj;
            case '[':
                var list = new List<object?>();
                at++;
                Space(s, ref at);
                if (Take(s, ref at, ']')) return list;
                do
                {
                    list.Add(Value(s, ref at));
                    Space(s, ref at);
                } while (Take(s, ref at, ','));
                if (!Take(s, ref at, ']')) throw Bad(at);
                return list;
            case '"':
                return String(s, ref at);
            case 't': return Word(s, ref at, "true", true);
            case 'f': return Word(s, ref at, "false", false);
            case 'n': return Word(s, ref at, "null", null);
            default:
                var start = at;
                while (at < s.Length && "+-0123456789.eE".Contains(s[at])) at++;
                if (double.TryParse(s.AsSpan(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return n;
                throw Bad(start);
        }
    }

    static string String(string s, ref int at)
    {
        at++; // the opening quote
        var b = new StringBuilder();
        while (at < s.Length)
        {
            var c = s[at++];
            if (c == '"') return b.ToString();
            if (c != '\\') { b.Append(c); continue; }
            if (at >= s.Length) break;
            switch (s[at++])
            {
                case '"': b.Append('"'); break;
                case '\\': b.Append('\\'); break;
                case '/': b.Append('/'); break;
                case 'b': b.Append('\b'); break;
                case 'f': b.Append('\f'); break;
                case 'n': b.Append('\n'); break;
                case 'r': b.Append('\r'); break;
                case 't': b.Append('\t'); break;
                case 'u':
                    // Surrogate pairs come as two of these, one after the other: appended as they are.
                    if (at + 4 > s.Length || !ushort.TryParse(s.AsSpan(at, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var u)) throw Bad(at);
                    b.Append((char)u);
                    at += 4;
                    break;
                default: throw Bad(at - 1);
            }
        }
        throw Bad(at);
    }

    static object? Word(string s, ref int at, string word, object? value)
    {
        if (string.CompareOrdinal(s, at, word, 0, word.Length) != 0) throw Bad(at);
        at += word.Length;
        return value;
    }

    static bool Take(string s, ref int at, char c)
    {
        if (at >= s.Length || s[at] != c) return false;
        at++;
        return true;
    }

    static void Space(string s, ref int at)
    {
        while (at < s.Length && s[at] is ' ' or '\t' or '\n' or '\r') at++;
    }

    static FormatException Bad(int at) => new($"not JSON at {at}");
}

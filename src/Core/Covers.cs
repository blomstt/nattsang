using System.Text;

namespace Nattsang.Core;

// The covers the now playing panel has shown, on disk, so an album's is
// fetched once: a file a cover in ~/.cache/nattsang/covers (or
// $XDG_CACHE_HOME's), named for its URL, the image's bytes as they came.
public static class Covers
{
    public static string? Dir()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var home = Environment.GetEnvironmentVariable("HOME");
        var bas = !string.IsNullOrEmpty(xdg) ? xdg : !string.IsNullOrEmpty(home) ? Path.Combine(home, ".cache") : null;
        return bas == null ? null : Path.Combine(bas, "nattsang", "covers");
    }

    // A file name for the URL: the service's covers end in their image's
    // id (https://i.scdn.co/image/<hex>), which is used as it is; any
    // other URL is a hash of itself. Empty for no URL.
    public static string Key(string url)
    {
        if (url.Length == 0) return "";
        var slash = url.LastIndexOf('/');
        var last = slash >= 0 ? url[(slash + 1)..] : url;
        if (url.StartsWith("https://", StringComparison.Ordinal) && last.Length is >= 16 and <= 80
            && last.AsSpan().IndexOfAnyExcept("0123456789abcdef") < 0)
            return last;
        // FNV-1a over the URL's UTF-8.
        ulong h = 14695981039346656037;
        foreach (var b in Encoding.UTF8.GetBytes(url)) h = (h ^ b) * 1099511628211;
        return $"url-{h:x16}";
    }

    public static byte[]? Load(string dir, string url)
    {
        try
        {
            var path = Path.Combine(dir, Key(url));
            return url.Length > 0 && File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    // Written whole or not at all: to a file beside it, then renamed.
    public static void Save(string dir, string url, byte[] image)
    {
        if (url.Length == 0) return;
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, Key(url));
            File.WriteAllBytes(path + ".part", image);
            File.Move(path + ".part", path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Console.Error.WriteLine($"nattsang: can't keep the cover: {e.Message}"); }
    }
}

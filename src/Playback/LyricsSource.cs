using Nattsang.Core;

namespace Nattsang.Playback;

// Finds the lyrics of the track that plays: from the cache when they were
// found before, else from LRCLIB (Core/Lrclib.cs), and keeps what it
// found. It asks only when it's told to (a new track), never by itself.
//
// get(url, done) makes the request and calls done(status, body) on the
// GLib main thread: status 0 when there was no answer, body then saying why.
// Find's found(track, song, error) also comes on the main thread, at once
// for a cached song, and only for the newest Find: an answer that comes
// after another track was asked for is kept, but not shown.
public sealed class LyricsSource(LyricsCache? cache, Action<string, Action<int, string>> get)
{
    int asked; // which Find is the newest

    public void Find(Track t, Action<Track, Lyrics.Song?, string?> found)
    {
        var ask = ++asked;
        bool Current() => ask == asked;
        if (cache?.Load(t, DateTime.UtcNow) is { } kept)
        {
            found(t, kept.Song(), null);
            return;
        }
        if (Lrclib.CanGet(t))
            get(Lrclib.GetUrl(t), (status, body) =>
            {
                // Found by its exact details: kept even when it's no longer the one asked for.
                if (status == 200 && Read(body) is { } exact) { Done(t, exact, Current() ? found : null); return; }
                // Not found, or LRCLIB too busy to look it up (its /api/get
                // can answer 503 while its search still works): the search.
                if (!Current()) return;
                if (status is 200 or 404 || status >= 500) Search(t, Current, found);
                else found(t, null, Why(status, body));
            });
        else Search(t, Current, found);
    }

    void Search(Track t, Func<bool> current, Action<Track, Lyrics.Song?, string?> found) =>
        get(Lrclib.SearchUrl(t), (status, body) =>
        {
            if (status != 200) { if (current()) found(t, null, Why(status, body)); return; }
            object? json;
            try { json = Json.Parse(body); }
            catch (FormatException) { if (current()) found(t, null, "LRCLIB's answer didn't read"); return; }
            Done(t, Lrclib.Choose(t, json), current() ? found : null);
        });

    void Done(Track t, Lrclib.Answer a, Action<Track, Lyrics.Song?, string?>? found)
    {
        cache?.Save(t, a);
        found?.Invoke(t, a.Song(), null);
    }

    // /api/get's answer: a record with lyrics (or instrumental), else null.
    static Lrclib.Answer? Read(string body)
    {
        try { return Lrclib.FromRecord(Json.Parse(body)); }
        catch (FormatException) { return null; }
    }

    static string Why(int status, string body) => status switch
    {
        0 => body,
        503 => "LRCLIB is busy just now (503)",
        429 => "LRCLIB is turning requests away for now (429)",
        _ => $"LRCLIB answered {status}",
    };
}

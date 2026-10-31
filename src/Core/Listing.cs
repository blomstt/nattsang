namespace Nattsang.Core;

// What a list's rows are: tracks to play, or albums, artists or playlists
// to open.
public enum Rows { Tracks, Albums, Artists, Playlists }

// A row that opens something: an album (its artist, its year), an artist,
// or a playlist (its owner when that isn't you, its number of tracks).
public sealed record Entry(string Uri, string Name, string Detail, string Side);

// One list from the library, by its key (Keys): it comes in pages, so it
// can be shown, and played from, before all of it is in. Pages only ever
// add to the end, so a position in it stays the same track.
public sealed class Listing(string key, string title)
{
    public string Key { get; } = key;

    // Its name, as the row it was opened from says; the player's own name
    // for it once that arrives.
    public string Title { get; set; } = title;

    public Rows Rows { get; } = Keys.RowsOf(key);
    public List<Track> Tracks { get; } = [];
    public List<Entry> Entries { get; } = [];
    public int Count => Rows == Rows.Tracks ? Tracks.Count : Entries.Count;

    public bool Loading { get; set; }  // a page is on its way
    public bool More { get; set; }     // there's more after what's in
    public string? Error { get; set; } // why the last page didn't come

    public void Clear()
    {
        Tracks.Clear();
        Entries.Clear();
        More = false;
        Error = null;
    }
}

// The lists the library has, by key: the four tabs, what a row opens (its
// URI), and a search.
public static class Keys
{
    public const string Liked = "liked", Playlists = "playlists", Albums = "albums", Artists = "artists";

    // The tabs, in the order the design shows them.
    public static readonly string[] Tabs = [Liked, Playlists, Albums, Artists];

    // Only songs, for now: an album or artist search needs another endpoint.
    // "search:albums:" and "search:artists:" would go here.
    public const string SearchSongs = "search:songs:";

    public static string Search(string words) => SearchSongs + words.Trim();

    public static bool IsSearch(string key) => key.StartsWith("search:", StringComparison.Ordinal);

    public static Rows RowsOf(string key) => key switch
    {
        Playlists => Rows.Playlists,
        Albums => Rows.Albums,
        Artists => Rows.Artists,
        _ when key.Contains(":artist:", StringComparison.Ordinal) => Rows.Albums, // an artist's albums
        _ => Rows.Tracks, // liked, a playlist, an album, a search
    };

    // What a list says when there's nothing in it.
    public static string Empty(string key) => key switch
    {
        Liked => "no liked songs yet",
        Playlists => "no playlists yet",
        Albums => "no saved albums yet",
        Artists => "no followed artists yet",
        _ when IsSearch(key) => "no songs found",
        _ => "nothing in it",
    };
}

using Nattsang.Core;

namespace Nattsang.Playback;

// Where the library panel's lists come from: the account, through the
// helper (SpotifyLibrary), or the sample album (SampleLibrary). Main
// thread only, as IPlayback.
public interface ILibrary
{
    // The list with that key (Keys), named name until the player says
    // otherwise. Each is asked for once and kept for the session; one that
    // failed is asked for again.
    Listing Open(string key, string name);

    // Its next page, when it has more and none is on its way.
    void More(Listing listing);

    // A list got a page, came to its end or failed. Raised on the GLib main thread.
    event Action<Listing>? Changed;
}

// The sample: Meteora as the one saved album, its tracks as the liked
// songs and a playlist, Linkin Park as the one followed artist. Nothing
// loads; a search looks through the album.
public sealed class SampleLibrary : ILibrary
{
    readonly Dictionary<string, Listing> lists = [];

    public event Action<Listing>? Changed;

    public SampleLibrary()
    {
        Add(Keys.Liked, "liked").Tracks.AddRange(Sample.Tracks.Reverse());
        Add(Keys.Albums, "albums").Entries.Add(new(Sample.AlbumUri, Sample.Album, Sample.Artist, "2003"));
        Add(Keys.Artists, "artists").Entries.Add(new(Sample.ArtistUri, Sample.Artist, "", ""));
        Add(Keys.Playlists, "playlists").Entries.Add(new(Sample.PlaylistUri, "night songs", "", "5"));
        Add(Sample.AlbumUri, Sample.Album).Tracks.AddRange(Sample.Tracks);
        Add(Sample.ArtistUri, Sample.Artist).Entries.Add(new(Sample.AlbumUri, Sample.Album, Sample.Artist, "2003"));
        Add(Sample.PlaylistUri, "night songs").Tracks.AddRange(Sample.Tracks.Where((_, i) => i % 3 == 0));
    }

    Listing Add(string key, string title) => lists[key] = new Listing(key, title);

    public Listing Open(string key, string name)
    {
        if (lists.TryGetValue(key, out var found)) return found;
        var l = Add(key, name);
        if (key.StartsWith(Keys.SearchSongs, StringComparison.Ordinal))
        {
            var words = key[Keys.SearchSongs.Length..];
            l.Tracks.AddRange(Sample.Tracks.Where(t => t.Title.Contains(words, StringComparison.OrdinalIgnoreCase)
                || t.Artist.Contains(words, StringComparison.OrdinalIgnoreCase)
                || t.Album.Contains(words, StringComparison.OrdinalIgnoreCase)));
        }
        Changed?.Invoke(l);
        return l;
    }

    public void More(Listing listing) { }
}

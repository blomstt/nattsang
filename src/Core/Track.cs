namespace Nattsang.Core;

// Uri says which track it is: spotify:track:… from the player, sample:… for
// the sample album. Rows, the queue and the player find a track by it.
// AlbumUri and ArtistUri (its first artist) are what `o` and `r` open; ""
// when the player didn't say.
public sealed record Track(string Uri, int Number, string Title, string Artist, string Album, int Seconds,
    string AlbumUri = "", string ArtistUri = "")
{
    // No track: what a player shows before it has any.
    public static readonly Track None = new("", 0, "", "", "", 0);
}

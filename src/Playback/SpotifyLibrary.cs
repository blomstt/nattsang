using System.Globalization;
using Nattsang.Core;

namespace Nattsang.Playback;

// The account's library through the helper (docs/protocol.md, "Lists"):
// `get` asks for a list's first page, `more` for the next; the helper
// answers each with `list`, its items and `list-end` (or `list-error`).
// Lists are kept for the session, so a tab switch asks for nothing. The
// window hands every helper line to Receive as it does to SpotifyPlayback,
// on the GLib main thread.
public sealed class SpotifyLibrary(Action<string> send) : ILibrary
{
    readonly Dictionary<string, Listing> lists = [];
    Listing? receiving; // the list whose page is coming in
    string? gone;       // why the helper isn't there any more

    public event Action<Listing>? Changed;

    public Listing Open(string key, string name)
    {
        if (lists.TryGetValue(key, out var kept) && kept.Error == null) return kept;
        var l = kept ?? (lists[key] = new Listing(key, name));
        l.Clear();
        if (gone != null) l.Error = gone;
        else
        {
            l.Loading = true;
            send(Protocol.Line("get", key));
        }
        return l;
    }

    public void More(Listing listing)
    {
        if (!listing.More || listing.Loading || listing.Error != null || gone != null) return;
        if (!lists.TryGetValue(listing.Key, out var l) || l != listing) return;
        l.Loading = true;
        send(Protocol.Line("more", l.Key));
        Changed?.Invoke(l);
    }

    public void Receive(string line)
    {
        var f = Protocol.Fields(line);
        string F(int i) => i < f.Length ? f[i] : "";
        lists.TryGetValue(F(1), out var l);

        switch (f[0])
        {
            case "list":
                // A page from position from on: the first starts the list
                // over; one that doesn't follow on from what's in is out of step.
                receiving = null;
                if (l == null || !int.TryParse(F(2), NumberStyles.None, CultureInfo.InvariantCulture, out var from)) break;
                if (from == 0) l.Clear();
                else if (from != l.Count) break;
                if (F(3).Length > 0) l.Title = F(3);
                receiving = l;
                break;
            case "item-track":
                if (receiving?.Rows == Rows.Tracks && Protocol.ItemTrack(f, receiving.Count + 1) is { } t) receiving.Tracks.Add(t);
                break;
            case "item-album" or "item-artist" or "item-playlist":
                if (receiving != null && receiving.Rows != Rows.Tracks && Protocol.ItemEntry(f) is { } e) receiving.Entries.Add(e);
                break;
            case "list-end":
                receiving = null;
                if (l == null) break;
                l.Loading = false;
                l.More = F(2) == "1";
                Changed?.Invoke(l);
                break;
            case "list-error":
                receiving = null;
                if (l == null) break;
                l.Loading = false;
                l.More = false;
                l.Error = F(2).Length > 0 ? F(2) : "it didn't come";
                Changed?.Invoke(l);
                break;
        }
    }

    // The helper has gone: what was on its way won't come.
    public void Ended(string why)
    {
        gone = $"The player stopped. {why}";
        receiving = null;
        foreach (var l in lists.Values)
        {
            if (!l.Loading) continue;
            l.Loading = false;
            l.More = false;
            l.Error = gone;
            Changed?.Invoke(l);
        }
    }
}

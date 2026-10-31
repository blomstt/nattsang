namespace Nattsang.Core;

// What plays after what: the queue first, then the list it's playing from
// (its context), on from the last track it played of that list. Playing a
// row starts the list there and keeps the queue; a queued track, once it
// starts, leaves the queue.
public sealed class Order(Queue queue)
{
    static readonly Listing nothing = new("", "");

    public Queue Queue { get; } = queue;

    // The list it plays from, and where it is in it: the last track it
    // played of it (-1 before any). A queued track playing leaves At where
    // it was, so the list goes on from there afterwards.
    public Listing List { get; private set; } = nothing;
    public int At { get; private set; } = -1;

    // The track it's on; Track.None before any.
    public Track Current { get; private set; } = Track.None;

    // That track came from the queue, not the list.
    public bool FromQueue { get; private set; }

    // A new list, or a new place in it. Changed is raised for neither the
    // queue (it has its own) nor a track starting (the player's is).
    public event Action? Changed;

    IReadOnlyList<Track> Tracks => List.Tracks;

    // Plays the list from its index'th track.
    public Track Play(Listing list, int index)
    {
        List = list;
        At = Math.Clamp(index, 0, Math.Max(0, list.Tracks.Count - 1));
        return Set(Tracks.Count > 0 ? Tracks[At] : Track.None, false);
    }

    // What comes after the track it's on: the queue's first, else the
    // list's next; past the list's end, its first again when wrap, else
    // null.
    public Track? Upcoming(bool wrap)
    {
        if (Queue.Count > 0) return Queue.Items[0];
        if (At + 1 < Tracks.Count) return Tracks[At + 1];
        return wrap && Tracks.Count > 0 ? Tracks[0] : null;
    }

    // Moves on to Upcoming, taking it off the queue if it's from there.
    public Track? Advance(bool wrap)
    {
        if (Queue.Count > 0)
        {
            var t = Queue.Items[0];
            Queue.RemoveAt(0);
            return Set(t, true);
        }
        if (At + 1 < Tracks.Count) At++;
        else if (wrap && Tracks.Count > 0) At = 0;
        else return null;
        return Set(Tracks[At], false);
    }

    // Back: from a queued track, to the list's track before it; else the
    // list's track before this one (the last when wrap, at the first).
    public Track? Back(bool wrap)
    {
        if (Tracks.Count == 0) return null;
        if (!FromQueue)
        {
            if (At > 0) At--;
            else if (wrap) At = Tracks.Count - 1;
            else return null;
        }
        return Set(Tracks[Math.Max(0, At)], false);
    }

    // The next tracks of the list after the one it's at, as many as fit in
    // count: what the queue view shows after the queue.
    public IEnumerable<Track> Next(int count) => Tracks.Skip(At + 1).Take(count);

    Track Set(Track track, bool fromQueue)
    {
        Current = track;
        FromQueue = fromQueue;
        Changed?.Invoke();
        return track;
    }
}

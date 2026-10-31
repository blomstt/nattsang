namespace Nattsang.Core;

// What plays after what: the queue first, then the list it's playing from
// (its context), on from the last track it played of that list. Playing a
// row starts the list there and keeps the queue; a queued track, once it
// starts, leaves the queue.
//
// Shuffled, the list plays in a random order: the track it's on first, then
// the others, each once; a page that comes in later is shuffled into what's
// still to come. Repeat says what happens when a track ends: Off stops after
// the list's last, List starts the list again (shuffled again, when it is),
// One plays the same track again. Next and previous go through the order as
// it is, whatever Repeat says.
public sealed class Order(Queue queue, Random? random = null)
{
    public enum Repeats { Off, List, One }

    static readonly Listing nothing = new("", "");

    readonly Random random = random ?? Random.Shared;

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

    public bool Shuffle { get; private set; }

    public Repeats Repeat
    {
        get => repeat;
        set
        {
            if (repeat == value) return;
            repeat = value;
            Changed?.Invoke();
        }
    }
    Repeats repeat;

    // A new list, a new place in it, shuffle or repeat. Changed is raised
    // for neither the queue (it has its own) nor a track starting (the
    // player's is).
    public event Action? Changed;

    IReadOnlyList<Track> Tracks => List.Tracks;

    // Shuffled, the list's indexes in the order they play (null: the list's
    // own order), and where it is in that order: At is the list's index
    // there. A round prepared for when it starts over (Upcoming says what
    // it begins with, before Advance gets there).
    List<int>? shuffled;
    int place = -1;
    List<int>? nextRound;

    int Count => Tracks.Count;
    int IndexAt(int p) => shuffled?[p] ?? p;

    // Plays the list from its index'th track.
    public Track Play(Listing list, int index)
    {
        List = list;
        At = Math.Clamp(index, 0, Math.Max(0, list.Tracks.Count - 1));
        nextRound = null;
        if (Shuffle)
        {
            shuffled = Round(Count > 0 ? At : -1);
            place = 0;
        }
        else place = At;
        return Set(Tracks.Count > 0 ? Tracks[At] : Track.None, false);
    }

    // Shuffle on: the tracks after this one in a new random order; off:
    // the list's own order, on from the track it's at.
    public void SetShuffle(bool on)
    {
        if (on == Shuffle) return;
        Shuffle = on;
        nextRound = null;
        if (on)
        {
            var from = At >= 0 && At < Count ? At : -1;
            shuffled = Round(from);
            place = from >= 0 ? 0 : -1;
        }
        else
        {
            shuffled = null;
            place = At;
        }
        Changed?.Invoke();
    }

    // Off, then the list again, then this track again.
    public void CycleRepeat() => Repeat = Repeat switch
    {
        Repeats.Off => Repeats.List,
        Repeats.List => Repeats.One,
        _ => Repeats.Off,
    };

    // What comes after the track it's on: the queue's first, else the
    // list's next; past the list's end, its first again when wrap, else
    // null.
    public Track? Upcoming(bool wrap)
    {
        if (Queue.Count > 0) return Queue.Items[0];
        Grow();
        if (place + 1 < Count) return Tracks[IndexAt(place + 1)];
        if (!wrap || Count == 0) return null;
        if (!Shuffle) return Tracks[0];
        nextRound ??= Round(-1, At);
        return Tracks[nextRound[0]];
    }

    // What plays when the track it's on ends, as Repeat says: this one
    // again, or Upcoming (round to the start for List).
    public Track? Following() => Repeat == Repeats.One
        ? (Current.Uri.Length > 0 ? Current : null)
        : Upcoming(Repeat == Repeats.List);

    // The track it's on has ended: on to Following.
    public Track? Finish() => Repeat == Repeats.One
        ? (Current.Uri.Length > 0 ? Set(Current, FromQueue) : null)
        : Advance(Repeat == Repeats.List);

    // Moves on to Upcoming, taking it off the queue if it's from there.
    public Track? Advance(bool wrap)
    {
        if (Queue.Count > 0)
        {
            var t = Queue.Items[0];
            Queue.RemoveAt(0);
            return Set(t, true);
        }
        Grow();
        if (place + 1 < Count) place++;
        else if (wrap && Count > 0)
        {
            if (Shuffle) shuffled = nextRound ?? Round(-1, At);
            nextRound = null;
            place = 0;
        }
        else return null;
        At = IndexAt(place);
        return Set(Tracks[At], false);
    }

    // Back: from a queued track, to the list's track before it; else the
    // list's track before this one (the last when wrap, at the first).
    public Track? Back(bool wrap)
    {
        if (Tracks.Count == 0) return null;
        Grow();
        if (!FromQueue)
        {
            if (place > 0) place--;
            else if (wrap) place = Count - 1;
            else return null;
        }
        At = IndexAt(Math.Max(0, place));
        return Set(Tracks[At], false);
    }

    // The next tracks of the list after the one it's at, in the order they
    // play, as many as fit in count: what the queue view shows after the queue.
    public IEnumerable<Track> Next(int count)
    {
        Grow();
        if (shuffled == null) return Tracks.Skip(At + 1).Take(count);
        return shuffled.Skip(place + 1).Take(count).Select(i => Tracks[i]);
    }

    // The list's indexes in a random order (Fisher-Yates): first first when
    // it's one of them, and not avoid first when there's another choice.
    List<int> Round(int first, int avoid = -1)
    {
        var order = new List<int>(Count);
        for (int i = 0; i < Count; i++) if (i != first) order.Add(i);
        for (int i = order.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        if (avoid >= 0 && order.Count > 1 && order[0] == avoid) (order[0], order[^1]) = (order[^1], order[0]);
        if (first >= 0) order.Insert(0, first);
        return order;
    }

    // Pages that came in since: shuffled, each new track goes somewhere
    // in what's still to come (and in a round prepared, at random too).
    void Grow()
    {
        if (shuffled == null) return;
        for (int i = shuffled.Count; i < Count; i++)
        {
            shuffled.Insert(random.Next(place + 1, shuffled.Count + 1), i);
            nextRound?.Insert(random.Next(nextRound.Count + 1), i);
        }
    }

    Track Set(Track track, bool fromQueue)
    {
        Current = track;
        FromQueue = fromQueue;
        Changed?.Invoke();
        return track;
    }
}

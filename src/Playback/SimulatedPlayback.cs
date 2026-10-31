using Nattsang.Core;

namespace Nattsang.Playback;

// Plays nothing: a clock that runs while "playing". At the end of a track it
// starts the same track again, as the design does.
public sealed class SimulatedPlayback : IPlayback
{
    readonly Func<double> clock;
    double anchorPosition, anchorTime;

    public event Action? Changed;

    // Starts on list's index'th track. clock: monotonic seconds
    public SimulatedPlayback(Order order, Listing list, int index, double position, bool playing, Func<double> clock)
    {
        if (list.Tracks.Count == 0) throw new ArgumentException("no tracks", nameof(list));
        Order = order;
        this.clock = clock;
        order.Play(list, index);
        anchorPosition = position;
        anchorTime = clock();
        Playing = playing;
    }

    public Order Order { get; }
    public Track Track => Order.Current;
    public bool Playing { get; private set; }
    public bool Stopped { get; private set; }
    public string Cover => "";
    public Status? Status => null;

    // The design's clock counts 0, 1, … up to the track's length and then
    // back to 0, so the full length shows for one second.
    public double Position
    {
        get
        {
            var p = Playing ? anchorPosition + (clock() - anchorTime) : anchorPosition;
            var loop = Track.Seconds + 1;
            return p < loop ? p : p % loop;
        }
    }

    public void Toggle()
    {
        anchorPosition = Position;
        anchorTime = clock();
        Playing = !Playing;
        Stopped = false;
        Changed?.Invoke();
    }

    public void Stop()
    {
        anchorPosition = 0;
        anchorTime = clock();
        Playing = false;
        Stopped = true;
        Changed?.Invoke();
    }

    // Nothing plays, so there's nothing to make louder or quieter.
    public void SetVolume(double gain) { }

    public void Seek(double seconds)
    {
        anchorPosition = Math.Clamp(seconds, 0, Track.Seconds);
        anchorTime = clock();
        Changed?.Invoke();
    }

    // Next and previous keep playing or paused as they were, and wrap around the list.
    public void Next() => Start(Order.Advance(true), Playing);
    public void Previous() => Start(Order.Back(true), Playing);

    public void Play(Listing list, int index)
    {
        if (index >= 0 && index < list.Tracks.Count) Start(Order.Play(list, index), true);
    }

    void Start(Track? track, bool playing)
    {
        if (track == null) return;
        anchorPosition = 0;
        anchorTime = clock();
        Playing = playing;
        Stopped = false;
        Changed?.Invoke();
    }
}

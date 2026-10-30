using Nattsang.Core;

namespace Nattsang.Playback;

// Plays nothing: a clock that runs while "playing". At the end of a track it
// starts the same track again, as the design does.
public sealed class SimulatedPlayback : IPlayback
{
    readonly Func<double> clock;
    readonly Track[] tracks;
    double anchorPosition, anchorTime;

    public event Action? Changed;

    // clock: monotonic seconds
    public SimulatedPlayback(Track[] tracks, int index, double position, bool playing, Func<double> clock)
    {
        if (tracks.Length == 0) throw new ArgumentException("no tracks", nameof(tracks));
        this.tracks = tracks;
        this.clock = clock;
        Index = Math.Clamp(index, 0, tracks.Length - 1);
        anchorPosition = position;
        anchorTime = clock();
        Playing = playing;
    }

    public IReadOnlyList<Track> Tracks => tracks;
    public int Index { get; private set; }
    public Track Track => tracks[Index];
    public bool Playing { get; private set; }

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
        Changed?.Invoke();
    }

    // Next and previous keep playing or paused as they were, and wrap around the album.
    public void Next() => Load((Index + 1) % tracks.Length, Playing);
    public void Previous() => Load((Index + tracks.Length - 1) % tracks.Length, Playing);

    public void Play(int index) => Load(index, true);

    void Load(int index, bool playing)
    {
        Index = Math.Clamp(index, 0, tracks.Length - 1);
        anchorPosition = 0;
        anchorTime = clock();
        Playing = playing;
        Changed?.Invoke();
    }
}

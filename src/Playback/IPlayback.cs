using Nattsang.Core;

namespace Nattsang.Playback;

// What the window asks of whatever plays the music. For now that's a
// simulated clock over the sample album (SimulatedPlayback); a real player
// (a helper process on a local socket) takes its place behind this.
//
// The window uses it from the GLib main thread only, and a player keeps to
// that too: it raises Changed on the main thread, so a player that hears
// from another thread (a socket reader) hops there first with Gui's
// Ui.Post. Reading a property never waits on the player.
public interface IPlayback
{
    IReadOnlyList<Track> Tracks { get; }
    int Index { get; }
    Track Track { get; }
    bool Playing { get; }

    // Seconds into the track, now; moves on its own while playing. Computed
    // here from the last report (where the track was, and when), never asked
    // of the player: the window reads it on every clock tick.
    double Position { get; }

    // Something changed that the clock alone doesn't explain: a new track,
    // play or pause, a jump. Raised on the GLib main thread.
    event Action? Changed;

    void Toggle();
    void Next();      // keeps playing or paused
    void Previous();  // keeps playing or paused
    void Play(int index); // starts that track, playing
}

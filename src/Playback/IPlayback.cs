using Nattsang.Core;

namespace Nattsang.Playback;

// What the window asks of whatever plays the music: the helper with
// librespot (SpotifyPlayback), or a simulated clock over the sample album
// (SimulatedPlayback).
//
// The window uses it from the GLib main thread only, and a player keeps to
// that too: it raises Changed on the main thread, so a player that hears
// from another thread hops there first with Gui's Ui.Post. Reading a
// property never waits on the player.
public interface IPlayback
{
    // The queue and the list it plays from: what comes next (Order.cs).
    Order Order { get; }

    // The track it's on, playing or not; Track.None before it has any.
    Track Track { get; }
    bool Playing { get; }

    // Nothing to play from where it is: before anything has played, after
    // Stop, or after the list's last track has ended. Paused is not stopped.
    bool Stopped { get; }

    // The track's cover image, a URL; "" when the player didn't say: for
    // the now playing panel and the desktop (MPRIS).
    string Cover { get; }

    // Seconds into the track, now; moves on its own while playing. Computed
    // here from the last report (where the track was, and when), never asked
    // of the player: the window reads it on every clock tick.
    double Position { get; }

    // What the window should say instead of the track while the player can't
    // play yet or has stopped (signing in, an error); null when all is well.
    Status? Status { get; }

    // Something changed that the clock alone doesn't explain: a new track or
    // list, play or pause, a jump, a status. Raised on the GLib main thread.
    event Action? Changed;

    void Toggle();
    void Next();      // the queue's next, else the list's; keeps playing or paused
    void Previous();  // the list's track before; keeps playing or paused
    void Play(Listing list, int index); // starts that track, playing, and the list after it
    void Stop();      // stops the track; Toggle plays it again from the start
    void Seek(double seconds); // within the track, playing or paused
    void SetVolume(double gain); // Nattsang's own volume: what the samples are multiplied by, 0 to 1
}

// A heading and a line under it, shown where the track's title goes.
public sealed record Status(string Title, string Detail);

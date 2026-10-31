using Nattsang.Core;

namespace Nattsang.Playback;

// Plays through the helper (helper/, docs/protocol.md): it sends commands,
// and follows what the helper reports. The window runs the helper process
// (Gui/Helper.cs) and hands every line it reads to Receive, and Ended when
// the helper has gone, both on the GLib main thread, so this class never
// sees another thread.
//
// It plays what Order says: a row of a list, then the queue, then the rest
// of the list, shuffled or not. Near the end of a track it has the helper
// preload what comes next, which starts when the track ends, without a gap;
// after the list's last track it stops, unless it repeats. Its volume is
// the helper's own, set on the samples it plays (SetVolume).
public sealed class SpotifyPlayback : IPlayback
{
    public static readonly Status Starting = new("Starting the player", "");
    public static readonly Status Connecting = new("Signing in", "Connecting to Spotify…");
    public static readonly Status SignIn = new("Sign in in your browser",
        "Spotify's login page has opened in your browser. Sign in there with your Premium account; Nattsang goes on by itself once you have.");

    public static Status NeedsPremium(string account) => new("Playback needs Premium",
        $"{(account.Length > 0 ? $"This is a {account} account" : "This account isn't Premium")}; playing music needs Spotify Premium. To sign in with another account, delete ~/.cache/nattsang and start Nattsang again.");

    public static Status SignedIn(string user) => new($"Signed in as {user}", "Choose a song in the library: Enter or a double click plays it.");

    readonly Action<string> send;
    readonly Func<double> clock;
    bool loaded;     // the helper has the track: loaded, and not stopped or ended since
    bool moving;     // the position runs: playing, not loading
    double anchorPosition, anchorTime;
    string? lastError;
    bool needsPremium; // the account can't play: that stays the status

    public event Action? Changed;

    // send: writes a command line to the helper. clock: monotonic seconds.
    public SpotifyPlayback(Action<string> send, Func<double> clock, Order order)
    {
        this.send = send;
        this.clock = clock;
        Order = order;
    }

    public Order Order { get; }
    public Track Track { get; private set; } = Track.None;
    public bool Playing { get; private set; }
    public bool Stopped => !loaded;
    public string Cover { get; private set; } = "";
    public Status? Status { get; private set; } = Starting;

    public double Position
    {
        get
        {
            var p = anchorPosition + (moving ? clock() - anchorTime : 0);
            return Track.Seconds > 0 ? Math.Clamp(p, 0, Track.Seconds) : Math.Max(0, p);
        }
    }

    // ---- commands -----------------------------------------------------------------

    // Before anything has played, space plays the queue, if there's one.
    public void Toggle()
    {
        if (Track == Track.None)
        {
            if (Order.Advance(false) is { } first) Load(first, true);
            return;
        }
        if (!loaded) Load(Track, true);
        else send(Protocol.Line(Playing ? "pause" : "play"));
    }

    // Next and previous wrap around the list, as the sample's do; nothing
    // before anything has played.
    public void Next()
    {
        if (Track != Track.None && Order.Advance(true) is { } next) Load(next, Playing);
    }

    public void Previous()
    {
        if (Track != Track.None && Order.Back(true) is { } back) Load(back, Playing);
    }

    public void Play(Listing list, int index)
    {
        if (index < 0 || index >= list.Tracks.Count) return;
        var t = Order.Play(list, index);
        if (t.Uri.Length > 0) Load(t, true);
    }

    // The helper's own volume: what its samples are multiplied by (Volume.Gain).
    public void SetVolume(double gain) => send(Protocol.Line("volume", Volume.GainText(gain)));

    public void Seek(double seconds)
    {
        if (loaded) send(Protocol.Line("seek", Protocol.Milliseconds(seconds)));
    }

    // The helper says `stopped`, and the track stays, held at 0:00.
    public void Stop()
    {
        if (loaded) send(Protocol.Line("stop"));
    }

    // It shows the new track at once, held at 0:00 until the helper says it plays.
    void Load(Track track, bool play)
    {
        if (track.Uri != Track.Uri) Cover = "";
        Track = track;
        loaded = true;
        Playing = play;
        Hold(0);
        send(Protocol.Line("load", track.Uri, play ? "1" : "0", "0"));
        Changed?.Invoke();
    }

    // ---- what the helper says -------------------------------------------------------

    public void Receive(string line)
    {
        var f = Protocol.Fields(line);
        string F(int i) => i < f.Length ? f[i] : "";
        // Position reports name their track: one for a track it has since
        // been told to leave is old news.
        bool Ours() => F(1).Length > 0 && F(1) == Track.Uri;

        switch (f[0])
        {
            case "hello":
                if (F(1) != Protocol.Version) Show(new("The player doesn't fit", $"It speaks version {F(1)}; this window speaks {Protocol.Version}. Build both from the same source."));
                break;
            case "connecting":
                Show(Connecting);
                break;
            case "needs-signin":
                Show(SignIn);
                break;
            case "signed-in":
                Show(SignedIn(F(1)));
                break;
            case "account":
                // premium plays; for any other, the helper stops right after.
                if (F(1) == "premium") break;
                needsPremium = true;
                Show(NeedsPremium(F(1)));
                break;
            case "track":
                if (!Ours()) break;
                // The length it really has, if the album's metadata said otherwise.
                var changed = false;
                if (Protocol.Seconds(F(5), out var length) && (int)length != Track.Seconds)
                {
                    Track = Track with { Seconds = (int)length };
                    changed = true;
                }
                if (F(6) != Cover)
                {
                    Cover = F(6);
                    changed = true;
                }
                if (changed) Changed?.Invoke();
                break;
            case "loading":
                if (Ours() && Protocol.Seconds(F(2), out var at)) { Hold(at); Changed?.Invoke(); }
                break;
            case "playing":
                if (!Ours() || !Protocol.Seconds(F(2), out var from)) break;
                loaded = Playing = true;
                Run(from);
                lastError = null;
                if (Status != null) Show(null); // it plays, so whatever went wrong before is past
                else Changed?.Invoke();
                break;
            case "paused":
                if (!Ours() || !Protocol.Seconds(F(2), out var still)) break;
                Playing = false;
                Hold(still);
                Changed?.Invoke();
                break;
            case "position":
                if (!Ours() || !Protocol.Seconds(F(2), out var to)) break;
                if (moving) Run(to); else Hold(to);
                Changed?.Invoke();
                break;
            case "near-end":
                // Repeating this one, it's loaded already: nothing to preload.
                if (Ours() && Order.Following() is { } upcoming && upcoming.Uri != Track.Uri) send(Protocol.Line("preload", upcoming.Uri));
                break;
            case "end":
                if (!Ours()) break;
                if (Order.Finish() is { } following) Load(following, true);
                else StoppedAt(Track.Seconds);
                break;
            case "stopped":
                if (Ours()) StoppedAt(0);
                break;
            case "unavailable":
                if (!Ours()) break;
                StoppedAt(0);
                Show(new($"Can't play {Track.Title}", "The player says it isn't available to this account."));
                break;
            case "error":
                lastError = F(1);
                Show(new("Something went wrong", lastError));
                break;
        }
    }

    // The helper has gone (why: from the window); nothing plays any more.
    public void Ended(string why)
    {
        loaded = Playing = false;
        Hold(Position);
        Show(needsPremium ? Status : new("The player stopped", lastError ?? why));
    }

    void StoppedAt(double at)
    {
        loaded = Playing = false;
        Hold(at);
        Changed?.Invoke();
    }

    void Show(Status? status)
    {
        Status = status;
        Changed?.Invoke();
    }

    // The position, held still or running from now.
    void Hold(double at)
    {
        anchorPosition = at;
        anchorTime = clock();
        moving = false;
    }

    void Run(double from)
    {
        anchorPosition = from;
        anchorTime = clock();
        moving = true;
    }
}

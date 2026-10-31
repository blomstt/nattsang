using Nattsang.Core;

namespace Nattsang.Playback;

// The music's levels from the helper (docs/protocol.md, "Levels"): its
// hello says whether it measures them; `levels 1` asks it to, `levels 0`
// to stop, and while it's asked and plays it sends a frame of them about
// 30 times a second. Lines come in on the GLib main thread, as the
// player's do.
public sealed class LevelFeed(Action<string> send)
{
    bool wanted;
    bool? asked; // what the helper was last asked; null: nothing yet (it starts off)

    public Levels Levels { get; } = new();

    // The helper measures: the pills follow Levels, not their own motion.
    public bool Measures { get; private set; }

    // Measures changed.
    public event Action? Changed;

    // The window wants levels (playing or not: the helper only measures
    // what it plays); asked of the helper once it has said it can.
    public void Want(bool on)
    {
        wanted = on;
        Ask();
    }

    void Ask()
    {
        if (!Measures || asked == wanted) return;
        asked = wanted;
        send(Protocol.Line("levels", wanted ? "1" : "0"));
        if (!wanted) Levels.Clear();
    }

    public void Receive(string line)
    {
        var f = Protocol.Fields(line);
        switch (f[0])
        {
            case "hello":
                var measures = f.Skip(2).Contains("levels");
                asked = null;
                if (measures != Measures)
                {
                    Measures = measures;
                    Changed?.Invoke();
                }
                Ask();
                break;
            case "levels":
                if (Measures && asked == true && Protocol.Levels(f, out var at, out var values)) Levels.Add(at, values);
                break;
        }
    }

    // The helper has gone: no more levels.
    public void Ended()
    {
        Levels.Clear();
        asked = null;
        if (!Measures) return;
        Measures = false;
        Changed?.Invoke();
    }
}

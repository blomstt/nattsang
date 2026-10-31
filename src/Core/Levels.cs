namespace Nattsang.Core;

// The music's loudness in the Pills' 28 bands, as the player measures it:
// frames of levels (0 to 1), each stamped with when it's heard, on the
// monotonic clock (seconds). They come ahead of time, as far ahead as the
// audio output's buffer, and are kept until they're past; the pills follow
// the level at now, between the two frames around it.
public sealed class Levels
{
    public const int Bands = Pills.Count;
    const int Most = 256;        // frames kept: about 8 s at 30 a second
    const double Stale = 0.25;   // a frame this old with none after it: silence

    readonly List<(double At, float[] Values)> frames = [];

    public int Count => frames.Count;

    // A frame, in time order; one that comes before others (the player
    // started again) replaces those after it.
    public void Add(double at, float[] values)
    {
        if (values.Length != Bands) return;
        var keep = frames.FindLastIndex(f => f.At < at) + 1;
        frames.RemoveRange(keep, frames.Count - keep);
        frames.Add((at, values));
        if (frames.Count > Most) frames.RemoveRange(0, frames.Count - Most);
    }

    public void Clear() => frames.Clear();

    // The levels heard at now, into into[Bands]: between the frames around
    // it, or the last one for a moment after it; 0 before the first and
    // once they've stopped coming. Frames before the one in use are dropped.
    public void At(double now, Span<double> into)
    {
        into.Clear();
        var next = frames.FindIndex(f => f.At > now);
        var last = (next < 0 ? frames.Count : next) - 1; // the last frame at or before now
        if (last < 0) return;
        frames.RemoveRange(0, last); // it's frames[0] now, and frames[1] the next, if there is one
        var (t0, a) = frames[0];
        if (frames.Count == 1)
        {
            if (now - t0 > Stale) return;
            for (int i = 0; i < Bands; i++) into[i] = a[i];
            return;
        }
        var (t1, b) = frames[1];
        var p = t1 > t0 ? Math.Clamp((now - t0) / (t1 - t0), 0, 1) : 1;
        for (int i = 0; i < Bands; i++) into[i] = a[i] + (b[i] - a[i]) * p;
    }
}

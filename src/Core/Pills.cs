namespace Nattsang.Core;

// The Pills visualizer's motion: 28 capsules, each stretching between 18%
// and full height on its own spring, timed as the design's canvas times them
// (its seeded random, so the window moves exactly like the mockup).
public static class Pills
{
    public const int Count = 28;
    public const double Width = 6, Height = 46, Low = .18;

    public static readonly (double Duration, double Delay)[] Timing = MakeTiming();

    static (double, double)[] MakeTiming()
    {
        // The canvas's generator, kind 9: seed = kind * 9973 + 7, an LCG run
        // in JavaScript doubles (so the product rounds before the >>> 0).
        double seed = 9 * 9973 + 7;
        double Next()
        {
            seed = (uint)(ulong)(seed * 1103515245.0 + 12345.0);
            return Math.Floor(seed / 65536) / 65536;
        }
        var timing = new (double, double)[Count];
        for (int i = 0; i < Count; i++)
        {
            var d = Math.Round((.45 + Next() * .45) * 100, MidpointRounding.AwayFromZero) / 100; // CSS got it as "0.63s"
            var delay = Math.Round(Next() * 100, MidpointRounding.AwayFromZero) / 100;           // and "-0.27s"
            timing[i] = (d, delay);
        }
        return timing;
    }

    // Height as a fraction of full, t seconds into playing.
    public static double Scale(int i, double t)
    {
        var (d, delay) = Timing[i];
        return Low + (1 - Low) * Ease.Spring.Alternate(t + delay, d);
    }

    // Colour band by thirds: 0 accent, 1 label, 2 playing.
    public static int Band(int i) => (double)i / Count < .34 ? 0 : (double)i / Count < .67 ? 1 : 2;
}

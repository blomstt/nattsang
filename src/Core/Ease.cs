namespace Nattsang.Core;

// CSS timing functions, so the window moves the way the design's CSS does.
public readonly struct Ease(double x1, double y1, double x2, double y2)
{
    public static readonly Ease Css = new(.25, .1, .25, 1);        // `ease`
    public static readonly Ease InOut = new(.42, 0, .58, 1);       // `ease-in-out`
    public static readonly Ease Out = new(.2, .8, .2, 1);          // lyric scroll, tab swap
    public static readonly Ease Spring = new(.34, 1.56, .64, 1);   // the pills' overshoot

    // y for x in [0, 1]: Newton on the curve's x(t), bisection if it stalls.
    public double At(double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        double t = x;
        for (int i = 0; i < 8; i++)
        {
            var err = X(t) - x;
            if (Math.Abs(err) < 1e-6) return Y(t);
            var d = DX(t);
            if (Math.Abs(d) < 1e-6) break;
            t -= err / d;
        }
        double lo = 0, hi = 1;
        t = x;
        for (int i = 0; i < 40; i++)
        {
            var v = X(t);
            if (Math.Abs(v - x) < 1e-7) break;
            if (v < x) lo = t; else hi = t;
            t = (lo + hi) / 2;
        }
        return Y(t);
    }

    double X(double t) => Bezier(t, x1, x2);
    double Y(double t) => Bezier(t, y1, y2);
    double DX(double t) => 3 * (1 - t) * (1 - t) * x1 + 6 * (1 - t) * t * (x2 - x1) + 3 * t * t * (1 - x2);
    static double Bezier(double t, double p1, double p2) => 3 * (1 - t) * (1 - t) * t * p1 + 3 * (1 - t) * t * t * p2 + t * t * t;

    // An `infinite alternate` animation's eased progress at time t: forward
    // on even rounds, back on odd ones, the curve run backwards as CSS does.
    public double Alternate(double t, double duration)
    {
        if (duration <= 0) return 0;
        var rounds = t / duration;
        var round = Math.Floor(rounds);
        var p = rounds - round;
        if (((long)round & 1) == 1) p = 1 - p;
        return At(p);
    }
}

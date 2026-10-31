using System.Globalization;

namespace Nattsang.Core;

// Nattsang's own volume, 0 to 100 %, as the key bar's bar shows it; the
// system's is left alone. The helper multiplies the samples it plays by
// Gain: the percentage cubed, as PulseAudio's own volume sliders map
// theirs, so each step sounds about as big as the last (50 % is about
// -18 dB, 10 % -60 dB). MPRIS has it as 0 to 1, the percentage over 100.
public sealed class Volume(int percent = Volume.Full)
{
    public const int Full = 100, Step = 5;

    public int Percent { get; private set; } = Clamp(percent);

    // A new percentage, from anywhere: the keys, the bar, MPRIS.
    public event Action? Changed;

    public static int Clamp(int percent) => Math.Clamp(percent, 0, Full);

    public void Set(int percent)
    {
        percent = Clamp(percent);
        if (percent == Percent) return;
        Percent = percent;
        Changed?.Invoke();
    }

    public void Up() => Set(Percent + Step);
    public void Down() => Set(Percent - Step);

    public double Gain => GainOf(Percent);

    public static double GainOf(int percent)
    {
        var f = Clamp(percent) / (double)Full;
        return f * f * f;
    }

    // The gain as the helper's `volume` command takes it: a plain decimal.
    public static string GainText(double gain) => Math.Clamp(gain, 0, 1).ToString("0.######", CultureInfo.InvariantCulture);

    // Where along a bar of width a point at x is, as a percentage.
    public static int AtX(double x, double width) => width <= 0 ? 0 : Clamp((int)Math.Round(x / width * Full));

    public double Mpris => Percent / (double)Full;

    // MPRIS's 0 to 1 (more is full, less is silent).
    public static int FromMpris(double v) => double.IsNaN(v) ? 0 : Clamp((int)Math.Round(Math.Clamp(v, 0, 1) * Full));
}

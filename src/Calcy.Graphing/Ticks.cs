using System.Globalization;

namespace Calcy.Graphing;

/// <summary>How axis ticks are spaced and labelled.</summary>
public enum TickStyle
{
    /// <summary>1, 2 or 5 times a power of ten.</summary>
    Decimal,

    /// <summary>Multiples of π/2 (π/4 when zoomed in), labelled with π.</summary>
    Pi,
}

/// <summary>An axis tick at a graph coordinate, with its label.</summary>
public readonly record struct Tick(double Value, string Label);

/// <summary>Nice tick spacing and labels for graph axes.</summary>
public static class Ticks
{
    /// <summary>Default minimum distance between labelled ticks, in pixels.</summary>
    public const double DefaultMinPixels = 56;

    /// <summary>The smallest step of the form 1, 2 or 5 × 10^k that keeps ticks at least <paramref name="minPixels"/> apart.</summary>
    public static double NiceStep(double span, double pixels, double minPixels = DefaultMinPixels)
    {
        if (!(span > 0) || !(pixels > 0)) return 1;
        double raw = span * minPixels / pixels;
        double p = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        foreach (var m in new[] { 1.0, 2.0, 5.0, 10.0 })
            if (m * p >= raw * (1 - 1e-9)) return m * p;
        return 10 * p;
    }

    /// <summary>The smallest step of the form π/4, π/2, π, 2π, 4π, ... keeping ticks at least <paramref name="minPixels"/> apart.</summary>
    public static double PiStep(double span, double pixels, double minPixels = DefaultMinPixels)
    {
        if (!(span > 0) || !(pixels > 0)) return Math.PI / 2;
        double raw = span * minPixels / pixels;
        double step = Math.PI / 4;
        while (step < raw * (1 - 1e-9) && step < 1e15) step *= 2;
        return step;
    }

    /// <summary>Ticks between min and max (inclusive) for an axis drawn over <paramref name="pixels"/> pixels.</summary>
    public static IReadOnlyList<Tick> Generate(double min, double max, double pixels, TickStyle style = TickStyle.Decimal, double minPixels = DefaultMinPixels)
    {
        var list = new List<Tick>();
        if (!(max > min) || !double.IsFinite(min) || !double.IsFinite(max)) return list;
        double step = style == TickStyle.Pi ? PiStep(max - min, pixels, minPixels) : NiceStep(max - min, pixels, minPixels);
        double first = Math.Ceiling(min / step - 1e-9);
        double last = Math.Floor(max / step + 1e-9);
        if (last - first > 2000) return list;
        for (double k = first; k <= last; k++)
        {
            double v = k * step;
            if (Math.Abs(v) < step * 1e-9) v = 0;
            list.Add(new Tick(v, style == TickStyle.Pi ? FormatPi(k, step) : Format(v, step)));
        }
        return list;
    }

    /// <summary>The step that <see cref="Generate"/> uses.</summary>
    public static double StepFor(double min, double max, double pixels, TickStyle style = TickStyle.Decimal, double minPixels = DefaultMinPixels) =>
        style == TickStyle.Pi ? PiStep(max - min, pixels, minPixels) : NiceStep(max - min, pixels, minPixels);

    /// <summary>A short label for a decimal tick value, with as many decimals as the step needs.</summary>
    public static string Format(double v, double step)
    {
        if (v == 0) return "0";
        double av = Math.Abs(v);
        if (av >= 1e6 || av < 1e-4)
        {
            int e = (int)Math.Floor(Math.Log10(av));
            double m = v / Math.Pow(10, e);
            int stepDigits = Math.Max(0, (int)Math.Ceiling(-Math.Log10(step / Math.Pow(10, e)) - 1e-9));
            string ms = Math.Round(m, Math.Min(stepDigits, 6)).ToString("0.######", CultureInfo.InvariantCulture);
            return ms + "e" + e.ToString(CultureInfo.InvariantCulture);
        }
        int decimals = Math.Max(0, (int)Math.Ceiling(-Math.Log10(step) - 1e-9));
        return Math.Round(v, Math.Min(decimals, 12)).ToString("F" + Math.Min(decimals, 12), CultureInfo.InvariantCulture);
    }

    /// <summary>A label such as "π/2", "-π" or "3π/2" for tick number <paramref name="k"/> of a π-based step.</summary>
    public static string FormatPi(double k, double step)
    {
        if (k == 0) return "0";
        // step = π·p/q with q a power of two (q ≤ 4) or an integer multiple of π.
        double ratio = step / Math.PI;
        int den = ratio < 0.3 ? 4 : ratio < 0.7 ? 2 : 1;
        long num = (long)Math.Round(k * ratio * den);
        long g = Gcd(Math.Abs(num), den);
        num /= g;
        long d = den / g;
        string sign = num < 0 ? "-" : "";
        long an = Math.Abs(num);
        string coef = an == 1 ? "" : an.ToString(CultureInfo.InvariantCulture);
        return d == 1 ? $"{sign}{coef}π" : $"{sign}{coef}π/{d}";
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a == 0 ? 1 : a;
    }
}

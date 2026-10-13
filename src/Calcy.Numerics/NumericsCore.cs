namespace Calcy.Numerics;

/// <summary>Thrown when an iterative numerical algorithm fails to converge within its iteration cap.</summary>
public sealed class ConvergenceException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public ConvergenceException(string message) : base(message) { }
}

/// <summary>Small deterministic pseudo-random generator (SplitMix64), reproducible across machines.</summary>
internal sealed class SplitMix64
{
    private ulong _state;

    public SplitMix64(ulong seed) => _state = seed;

    public ulong NextUInt64()
    {
        ulong z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform double in [0, 1).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);
}

internal static class NumericsCore
{
    public const double Eps = 2.220446049250313e-16;
    public static readonly double SqrtEps = Math.Sqrt(Eps);
    public static readonly double CbrtEps = Math.Cbrt(Eps);

    public static bool IsFinite(double x) => double.IsFinite(x);

    /// <summary>Neumaier compensated summation.</summary>
    public struct CompensatedSum
    {
        private double _sum;
        private double _c;

        public void Add(double x)
        {
            double t = _sum + x;
            if (Math.Abs(_sum) >= Math.Abs(x)) _c += (_sum - t) + x;
            else _c += (x - t) + _sum;
            _sum = t;
        }

        public readonly double Value => _sum + _c;
    }

    public static double Dot(double[] a, double[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }

    public static double Norm2(double[] a)
    {
        double scale = 0;
        for (int i = 0; i < a.Length; i++) scale = Math.Max(scale, Math.Abs(a[i]));
        if (scale == 0 || !double.IsFinite(scale)) return scale;
        double s = 0;
        for (int i = 0; i < a.Length; i++)
        {
            double v = a[i] / scale;
            s += v * v;
        }
        return scale * Math.Sqrt(s);
    }

    public static double NormInf(double[] a)
    {
        double m = 0;
        for (int i = 0; i < a.Length; i++)
        {
            double v = Math.Abs(a[i]);
            if (double.IsNaN(v)) return double.NaN;
            if (v > m) m = v;
        }
        return m;
    }

    public static bool AllFinite(double[] a)
    {
        for (int i = 0; i < a.Length; i++)
            if (!double.IsFinite(a[i])) return false;
        return true;
    }

    public static double[] Add(double[] a, double[] b, double scaleB = 1)
    {
        var r = new double[a.Length];
        for (int i = 0; i < a.Length; i++) r[i] = a[i] + scaleB * b[i];
        return r;
    }

    public static double[] Copy(double[] a) => (double[])a.Clone();
}

namespace Calcy.Statistics.Internal;

/// <summary>Low level numeric helpers shared by the library (accurate log1p, expm1, sinpi and friends).</summary>
internal static class Num
{
    public const double Eps = 2.220446049250313e-16;
    public const double MinNormal = 2.2250738585072014e-308;
    public const double LnSqrt2Pi = 0.91893853320467274178032973640562;
    public const double Sqrt2Pi = 2.5066282746310005024157652848110;
    public const double InvSqrt2Pi = 0.39894228040143267793994605993438;
    public const double Sqrt2 = 1.4142135623730950488016887242097;
    public const double InvSqrt2 = 0.70710678118654752440084436210485;
    public const double SqrtPi = 1.7724538509055160272981674833411;
    public const double InvSqrtPi = 0.56418958354775628694807945156077;
    public const double TwoOverSqrtPi = 1.1283791670955125738961589031215;
    public const double SqrtHalfPi = 1.2533141373155002512078826424055;
    public const double Ln2 = 0.69314718055994530941723212145818;
    public const double LnPi = 1.1447298858494001741434273513531;

    /// <summary>log(1 + x) accurate for small x (Kahan's method).</summary>
    public static double Log1p(double x)
    {
        if (double.IsNaN(x) || x < -1) return double.NaN;
        if (x == -1) return double.NegativeInfinity;
        if (double.IsPositiveInfinity(x)) return x;
        double u = 1.0 + x;
        if (u == 1.0) return x;
        return Math.Log(u) * x / (u - 1.0);
    }

    /// <summary>exp(x) - 1 accurate for small x (Kahan's method).</summary>
    public static double Expm1(double x)
    {
        if (double.IsNaN(x)) return double.NaN;
        double ax = Math.Abs(x);
        if (ax < 1e-5) return x + 0.5 * x * x + x * x * x / 6.0;
        if (ax > 0.5) return Math.Exp(x) - 1.0;
        double u = Math.Exp(x);
        double um1 = u - 1.0;
        if (um1 == 0.0) return x;
        return um1 * x / Math.Log(u);
    }

    /// <summary>log(1 + x) - x, accurate near zero.</summary>
    public static double Log1pmx(double x)
    {
        if (double.IsNaN(x) || x < -1) return double.NaN;
        if (x == -1) return double.NegativeInfinity;
        if (Math.Abs(x) > 0.5) return Log1p(x) - x;
        double s = x / (2.0 + x);
        double s2 = s * s;
        double term = 2.0 * s * s2;
        double sum = 0.0;
        for (int k = 3; k < 400; k += 2)
        {
            double t = term / k;
            sum += t;
            if (Math.Abs(t) <= 1e-18 * Math.Abs(sum)) break;
            term *= s2;
        }
        return -x * s + sum;
    }

    /// <summary>sin(pi x) with exact zeros at integers.</summary>
    public static double SinPi(double x)
    {
        if (!double.IsFinite(x)) return double.NaN;
        double r = Math.IEEERemainder(x, 2.0);
        double sign = 1.0;
        if (r < 0) { r = -r; sign = -1.0; }
        double v;
        if (r <= 0.25) v = Math.Sin(Math.PI * r);
        else if (r <= 0.75) v = Math.Cos(Math.PI * (r - 0.5));
        else v = Math.Sin(Math.PI * (1.0 - r));
        return sign * v;
    }

    /// <summary>cos(pi x) with exact zeros at half integers.</summary>
    public static double CosPi(double x)
    {
        if (!double.IsFinite(x)) return double.NaN;
        double r = Math.Abs(Math.IEEERemainder(x, 2.0));
        if (r <= 0.25) return Math.Cos(Math.PI * r);
        if (r <= 0.75) return Math.Sin(Math.PI * (0.5 - r));
        return -Math.Cos(Math.PI * (1.0 - r));
    }

    /// <summary>tan(pi x).</summary>
    public static double TanPi(double x) => SinPi(x) / CosPi(x);

    /// <summary>exp(-z*z/2) without the rounding error of forming z*z.</summary>
    public static double ExpMinusHalfSquare(double z)
    {
        z = Math.Abs(z);
        if (double.IsNaN(z)) return double.NaN;
        if (z < 1.0) return Math.Exp(-0.5 * z * z);
        if (z > 40.0) return 0.0;
        double zh = Math.Round(z * 65536.0) / 65536.0;
        double zl = z - zh;
        return Math.Exp(-0.5 * zh * zh) * Math.Exp(-zl * (zh + 0.5 * zl));
    }

    /// <summary>exp(-x*x) without the rounding error of forming x*x.</summary>
    public static double ExpMinusSquare(double x)
    {
        x = Math.Abs(x);
        if (double.IsNaN(x)) return double.NaN;
        if (x < 0.7) return Math.Exp(-x * x);
        if (x > 28.0) return 0.0;
        double xh = Math.Round(x * 65536.0) / 65536.0;
        double xl = x - xh;
        return Math.Exp(-xh * xh) * Math.Exp(-xl * (2.0 * xh + xl));
    }

    /// <summary>True when x is a finite integer value.</summary>
    public static bool IsInteger(double x) => double.IsFinite(x) && Math.Floor(x) == x;

    /// <summary>Compensated (Neumaier) sum.</summary>
    public static double Sum(IReadOnlyList<double> values)
    {
        var acc = new Accumulator();
        for (int i = 0; i < values.Count; i++) acc.Add(values[i]);
        return acc.Value;
    }

    /// <summary>Neumaier compensated accumulator.</summary>
    public struct Accumulator
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
}

/// <summary>Argument validation helpers that throw ArgumentException with clear messages.</summary>
internal static class Guard
{
    public static void Finite(double v, string what, string param)
    {
        if (!double.IsFinite(v)) throw new ArgumentException($"{what} must be a finite number", param);
    }

    public static void NotNaN(double v, string what, string param)
    {
        if (double.IsNaN(v)) throw new ArgumentException($"{what} must be a number", param);
    }

    public static void Positive(double v, string what, string param)
    {
        if (!(v > 0) || double.IsNaN(v)) throw new ArgumentException($"{what} must be positive", param);
    }

    public static void PositiveFinite(double v, string what, string param)
    {
        if (!(v > 0) || !double.IsFinite(v)) throw new ArgumentException($"{what} must be a positive finite number", param);
    }

    public static void NonNegative(double v, string what, string param)
    {
        if (!(v >= 0) || !double.IsFinite(v)) throw new ArgumentException($"{what} must be a non-negative finite number", param);
    }

    public static void Probability(double p, string what, string param)
    {
        if (!(p >= 0 && p <= 1)) throw new ArgumentException($"{what} must be between 0 and 1", param);
    }

    public static void NonNegativeInteger(double v, string what, string param)
    {
        if (!(v >= 0) || !Num.IsInteger(v)) throw new ArgumentException($"{what} must be a non-negative integer", param);
    }

    public static void PositiveInteger(double v, string what, string param)
    {
        if (!(v >= 1) || !Num.IsInteger(v)) throw new ArgumentException($"{what} must be a positive integer", param);
    }

    public static void Integer(double v, string what, string param)
    {
        if (!Num.IsInteger(v)) throw new ArgumentException($"{what} must be an integer", param);
    }

    public static void NotNull(object? o, string param)
    {
        if (o is null) throw new ArgumentException($"{param} must not be null", param);
    }

    public static void AllFinite(IReadOnlyList<double> values, string param)
    {
        Guard.NotNull(values, param);
        for (int i = 0; i < values.Count; i++)
        {
            if (!double.IsFinite(values[i])) throw new ArgumentException($"{param} must contain only finite numbers (element {i} is {values[i]})", param);
        }
    }

    public static void SameLength(int a, int b, string what)
    {
        if (a != b) throw new ArgumentException($"{what} must have the same length ({a} and {b})");
    }
}

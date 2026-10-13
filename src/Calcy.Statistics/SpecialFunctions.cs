using System.Numerics;
using Calcy.Statistics.Internal;

namespace Calcy.Statistics;

/// <summary>
/// Special functions of real arguments (gamma family, error functions, incomplete gamma and beta,
/// Lambert W, Riemann zeta, binomial coefficients). All are implemented from scratch; relative
/// accuracy is close to double precision (typically better than 1e-14) over the usual domains.
/// </summary>
public static partial class SpecialFunctions
{
    /// <summary>The Euler-Mascheroni constant.</summary>
    public const double EulerGamma = 0.57721566490153286060651209008240243;

    // Lanczos approximation, g = 6.024680040776729583740234375, 13 terms (rational form as in CPython).
    internal const double LanczosG = 6.024680040776729583740234375;
    internal const double LanczosGMinusHalf = 5.524680040776729583740234375;

    private static readonly double[] LanczosNum =
    [
        23531376880.410759688572007674451636754734846804940,
        42919803642.649098768957899047001988850926355848959,
        35711959237.355668049440185451547166705960488635843,
        17921034426.037209699919755754458931112671403265390,
        6039542586.3520280050642916443072979210699388420708,
        1439720407.3117216736632230727949123939715485786772,
        248874557.86205415651146038641322942321632125127801,
        31426415.585400194380614231628318205362874684987640,
        2876370.6289353724412254090516208496135991145378768,
        186056.26539522349504029498971604569928220784236328,
        8071.6720023658162106380029022722506138218516325024,
        210.82427775157934587250973392071336271166969580291,
        2.5066282746310002701649081771338373386264310793408,
    ];

    private static readonly double[] LanczosDen =
    [
        0.0, 39916800.0, 120543840.0, 150917976.0, 105258076.0, 45995730.0,
        13339535.0, 2637558.0, 357423.0, 32670.0, 1925.0, 66.0, 1.0,
    ];

    private static readonly double[] FactorialTable = BuildFactorials();

    private static double[] BuildFactorials()
    {
        var t = new double[171];
        BigInteger f = BigInteger.One;
        t[0] = 1.0;
        for (int i = 1; i <= 170; i++)
        {
            f *= i;
            t[i] = (double)f;
        }
        return t;
    }

    /// <summary>Lanczos rational sum: Gamma(x) = L(x) (x+g-1/2)^(x-1/2) / exp(x+g-1/2).</summary>
    internal static double LanczosSum(double x)
    {
        double num = 0, den = 0;
        if (x < 5.0)
        {
            for (int i = LanczosNum.Length - 1; i >= 0; i--)
            {
                num = num * x + LanczosNum[i];
                den = den * x + LanczosDen[i];
            }
        }
        else
        {
            for (int i = 0; i < LanczosNum.Length; i++)
            {
                num = num / x + LanczosNum[i];
                den = den / x + LanczosDen[i];
            }
        }
        return num / den;
    }

    // ------------------------------------------------------------------ Gamma family

    /// <summary>The gamma function. Returns NaN at the poles (non-positive integers).</summary>
    public static double Gamma(double x)
    {
        if (double.IsNaN(x) || double.IsPositiveInfinity(x)) return x;
        if (double.IsNegativeInfinity(x)) return double.NaN;
        if (x == 0) return 1.0 / x;
        if (Num.IsInteger(x))
        {
            if (x < 0) return double.NaN;
            if (x <= 171) return FactorialTable[(int)x - 1];
            return double.PositiveInfinity;
        }
        double absx = Math.Abs(x);
        if (absx < 1e-20) return 1.0 / x;
        if (absx > 200)
        {
            if (x < 0) return 0.0 / Num.SinPi(x);
            return double.PositiveInfinity;
        }
        double y = absx + LanczosGMinusHalf;
        double q, z;
        if (absx > LanczosGMinusHalf)
        {
            q = y - absx;
            z = q - LanczosGMinusHalf;
        }
        else
        {
            q = y - LanczosGMinusHalf;
            z = q - absx;
        }
        z = z * LanczosG / y;
        double r;
        if (x < 0)
        {
            r = -Math.PI / Num.SinPi(absx) / absx * Math.Exp(y) / LanczosSum(absx);
            r -= z * r;
            if (absx < 140)
            {
                r /= Math.Pow(y, absx - 0.5);
            }
            else
            {
                double sqrtpow = Math.Pow(y, absx / 2.0 - 0.25);
                r /= sqrtpow;
                r /= sqrtpow;
            }
        }
        else
        {
            r = LanczosSum(absx) / Math.Exp(y);
            r += z * r;
            if (absx < 140)
            {
                r *= Math.Pow(y, absx - 0.5);
            }
            else
            {
                double sqrtpow = Math.Pow(y, absx / 2.0 - 0.25);
                r *= sqrtpow;
                r *= sqrtpow;
            }
        }
        return r;
    }

    /// <summary>Natural logarithm of |Gamma(x)|. Returns +infinity at the poles.</summary>
    public static double LogGamma(double x)
    {
        if (double.IsNaN(x)) return x;
        if (double.IsInfinity(x)) return double.PositiveInfinity;
        if (x <= 0 && Num.IsInteger(x)) return double.PositiveInfinity;
        if (x == 1 || x == 2) return 0.0;
        if (Math.Abs(x - 1) < 0.2) return LogGamma1p(x - 1);
        if (Math.Abs(x - 2) < 0.2)
        {
            double z = x - 2;
            return LogGamma1p(z) + Num.Log1p(z);
        }
        double absx = Math.Abs(x);
        if (absx < 1e-20) return -Math.Log(absx);
        double r = Math.Log(LanczosSum(absx)) - LanczosG;
        r += (absx - 0.5) * (Math.Log(absx + LanczosG - 0.5) - 1);
        if (x < 0) r = Num.LnPi - Math.Log(Math.Abs(Num.SinPi(absx))) - Math.Log(absx) - r;
        return r;
    }

    /// <summary>log Gamma(1 + z) for |z| up to about 0.25 by its Taylor series.</summary>
    internal static double LogGamma1p(double z)
    {
        double sum = 0;
        for (int k = LogGammaTaylor.Length - 1; k >= 0; k--) sum = sum * z + LogGammaTaylor[k];
        return sum * z;
    }

    /// <summary>Gamma(1 + a) - 1, accurate for small |a|.</summary>
    internal static double Gamma1pm1(double a)
    {
        if (Math.Abs(a) < 0.2) return Num.Expm1(LogGamma1p(a));
        return Gamma(1 + a) - 1;
    }

    /// <summary>Sign of Gamma(x) (0 at poles).</summary>
    internal static double GammaSign(double x)
    {
        if (x > 0) return 1;
        if (Num.IsInteger(x)) return 0;
        return Math.Sign(Num.SinPi(x));
    }

    /// <summary>The factorial x! = Gamma(x + 1); exact (correctly rounded) for integers 0..170.</summary>
    public static double Factorial(double x)
    {
        if (double.IsNaN(x)) return x;
        if (Num.IsInteger(x))
        {
            if (x < 0) return double.NaN;
            if (x <= 170) return FactorialTable[(int)x];
            return double.PositiveInfinity;
        }
        return Gamma(x + 1);
    }

    /// <summary>The beta function B(a, b) = Gamma(a) Gamma(b) / Gamma(a + b).</summary>
    public static double Beta(double a, double b)
    {
        if (double.IsNaN(a) || double.IsNaN(b)) return double.NaN;
        if (a <= 0 || b <= 0) return BetaGeneral(a, b);
        if (double.IsPositiveInfinity(a) || double.IsPositiveInfinity(b)) return 0.0;
        if (a < b) (a, b) = (b, a);
        if (b == 1) return 1.0 / a;
        if (a == 1) return 1.0 / b;
        double c = a + b;
        if (c == a && b < Num.Eps) return 1.0 / b;
        double bgh = b + LanczosGMinusHalf, cgh = c + LanczosGMinusHalf;
        double lnPow = (a - 0.5) * Num.Log1p(-b / cgh) + b * Math.Log(bgh / cgh);
        double result = LanczosSum(a) * (LanczosSum(b) / LanczosSum(c));
        return result * Math.Exp(lnPow - LanczosG) * Math.Sqrt(Math.E / bgh);
    }

    /// <summary>Natural logarithm of |B(a, b)|.</summary>
    public static double LogBeta(double a, double b)
    {
        if (double.IsNaN(a) || double.IsNaN(b)) return double.NaN;
        if (a <= 0 || b <= 0)
        {
            double v = BetaGeneral(a, b);
            if (double.IsNaN(v)) return v;
            if (v == 0 || double.IsInfinity(v)) return LogGamma(a) + LogGamma(b) - LogGamma(a + b);
            return Math.Log(Math.Abs(v));
        }
        if (double.IsPositiveInfinity(a) || double.IsPositiveInfinity(b)) return double.NegativeInfinity;
        if (a < b) (a, b) = (b, a);
        if (b == 1) return -Math.Log(a);
        if (a == 1) return -Math.Log(b);
        double c = a + b;
        if (c == a && b < Num.Eps) return -Math.Log(b);
        double bgh = b + LanczosGMinusHalf, cgh = c + LanczosGMinusHalf;
        double lnPow = (a - 0.5) * Num.Log1p(-b / cgh) + b * Math.Log(bgh / cgh);
        return Math.Log(LanczosSum(a)) + Math.Log(LanczosSum(b)) - Math.Log(LanczosSum(c))
            - LanczosG + 0.5 * (1.0 - Math.Log(bgh)) + lnPow;
    }

    private static double BetaGeneral(double a, double b)
    {
        if (double.IsInfinity(a) || double.IsInfinity(b)) return double.NaN;
        bool poleA = a <= 0 && Num.IsInteger(a);
        bool poleB = b <= 0 && Num.IsInteger(b);
        double c = a + b;
        bool poleC = c <= 0 && Num.IsInteger(c);
        if (poleA || poleB) return double.NaN;
        if (poleC) return 0.0;
        double sign = GammaSign(a) * GammaSign(b) * GammaSign(c);
        return sign * Math.Exp(LogGamma(a) + LogGamma(b) - LogGamma(c));
    }

    /// <summary>The digamma function psi(x) = Gamma'(x) / Gamma(x). Returns NaN at the poles.</summary>
    public static double Digamma(double x)
    {
        if (double.IsNaN(x) || double.IsNegativeInfinity(x)) return double.NaN;
        if (double.IsPositiveInfinity(x)) return x;
        if (x <= 0)
        {
            if (Num.IsInteger(x)) return double.NaN;
            // Reflection: psi(x) = psi(1 - x) - pi cot(pi x)
            return Digamma(1 - x) - Math.PI * Num.CosPi(x) / Num.SinPi(x);
        }
        if (Math.Abs(x - DigammaRootHi) < 0.25)
        {
            double h = (x - DigammaRootHi) - DigammaRootLo;
            double s = 0;
            for (int k = DigammaRootTaylor.Length - 1; k >= 0; k--) s = s * h + DigammaRootTaylor[k];
            return s * h;
        }
        double result = 0;
        if (x < 1e-8) return -1.0 / x - EulerGamma + 1.6449340668482264 * x;
        while (x < 10)
        {
            result -= 1.0 / x;
            x += 1;
        }
        double inv = 1.0 / x, inv2 = inv * inv;
        double series = inv2 * (1.0 / 12 - inv2 * (1.0 / 120 - inv2 * (1.0 / 252 - inv2 * (1.0 / 240 - inv2 * (1.0 / 132 - inv2 * (691.0 / 32760 - inv2 / 12))))));
        return result + Math.Log(x) - 0.5 * inv - series;
    }

    // ------------------------------------------------------------------ Error functions

    private static double ErfSeries(double x)
    {
        double x2 = x * x, term = x, sum = x;
        for (int n = 1; n < 60; n++)
        {
            term *= -x2 / n;
            double t = term / (2 * n + 1);
            sum += t;
            if (Math.Abs(t) < 1e-17 * Math.Abs(sum)) break;
        }
        return sum * Num.TwoOverSqrtPi;
    }

    private static double Clenshaw(double[] c, double t)
    {
        double b1 = 0, b2 = 0;
        for (int j = c.Length - 1; j >= 1; j--)
        {
            double tmp = 2 * t * b1 - b2 + c[j];
            b2 = b1;
            b1 = tmp;
        }
        return t * b1 - b2 + 0.5 * c[0];
    }

    /// <summary>The error function erf(x).</summary>
    public static double Erf(double x)
    {
        if (double.IsNaN(x)) return x;
        double ax = Math.Abs(x);
        if (ax < 0.5) return ErfSeries(x);
        if (ax > 6.0) return x < 0 ? -1.0 : 1.0;
        double r = 1.0 - Erfcx(ax) * Num.ExpMinusSquare(ax);
        return x < 0 ? -r : r;
    }

    /// <summary>The complementary error function erfc(x) = 1 - erf(x), accurate in the far right tail.</summary>
    public static double Erfc(double x)
    {
        if (double.IsNaN(x)) return x;
        if (x < -0.5) return 2.0 - Erfc(-x);
        if (x < 0.5) return 1.0 - ErfSeries(x);
        if (x > 27.3) return 0.0;
        return Erfcx(x) * Num.ExpMinusSquare(x);
    }

    /// <summary>The scaled complementary error function erfcx(x) = exp(x^2) erfc(x).</summary>
    public static double Erfcx(double x)
    {
        if (double.IsNaN(x)) return x;
        if (x < 0.5)
        {
            if (x < -26.7) return double.PositiveInfinity;
            if (x < -0.5) return 2.0 / Num.ExpMinusSquare(x) - Erfcx(-x);
            return Math.Exp(x * x) * (1.0 - ErfSeries(x));
        }
        if (x < 4.0) return Clenshaw(ErfcxMid, (x - 2.25) / 1.75);
        if (x > 1e9) return Num.InvSqrtPi / x;
        return Clenshaw(ErfcxTail, 8.0 / x - 1.0) / (x * Num.SqrtPi);
    }

    /// <summary>Inverse error function: erf(ErfInv(y)) = y for -1 &lt;= y &lt;= 1.</summary>
    public static double ErfInv(double y)
    {
        if (!(y >= -1 && y <= 1)) throw new ArgumentException("the argument of ErfInv must be between -1 and 1", nameof(y));
        if (y == 0) return y;
        if (y == 1) return double.PositiveInfinity;
        if (y == -1) return double.NegativeInfinity;
        double ay = Math.Abs(y);
        double x = ErfInvCore(ay, 1.0 - ay);
        return y < 0 ? -x : x;
    }

    /// <summary>Inverse complementary error function: erfc(ErfcInv(z)) = z for 0 &lt;= z &lt;= 2.</summary>
    public static double ErfcInv(double z)
    {
        if (!(z >= 0 && z <= 2)) throw new ArgumentException("the argument of ErfcInv must be between 0 and 2", nameof(z));
        if (z == 0) return double.PositiveInfinity;
        if (z == 2) return double.NegativeInfinity;
        if (z == 1) return 0.0;
        if (z > 1) return -ErfInvCore(z - 1.0, 2.0 - z);
        return ErfInvCore(1.0 - z, z);
    }

    /// <summary>Solves erf(x) = ay (equivalently erfc(x) = z, z = 1 - ay) for x &gt;= 0.</summary>
    private static double ErfInvCore(double ay, double z)
    {
        double x = StdNormal.As241(0.5 * ay, 0.5 * z) * Num.InvSqrt2;
        for (int iter = 0; iter < 4; iter++)
        {
            double u;
            if (ay <= 0.5)
            {
                double f = Erf(x) - ay;
                u = f / (Num.TwoOverSqrtPi * Math.Exp(-x * x));
            }
            else
            {
                double ec = Erfc(x);
                if (ec == 0) break;
                u = (z / ec - 1.0) * Erfcx(x) * (0.5 * Num.SqrtPi);
            }
            double xNew = x - u / (1.0 + x * u);
            if (!double.IsFinite(xNew)) break;
            bool done = Math.Abs(xNew - x) <= 1e-16 * Math.Abs(xNew);
            x = xNew;
            if (done) break;
        }
        return x;
    }

    // ------------------------------------------------------------------ Incomplete gamma

    /// <summary>Regularized lower incomplete gamma function P(a, x) = gamma(a, x) / Gamma(a).</summary>
    public static double RegularizedGammaP(double a, double x)
    {
        CheckGammaArgs(a, x);
        IncompleteGamma(a, x, out double p, out _);
        return p;
    }

    /// <summary>Regularized upper incomplete gamma function Q(a, x) = 1 - P(a, x), accurate when small.</summary>
    public static double RegularizedGammaQ(double a, double x)
    {
        CheckGammaArgs(a, x);
        IncompleteGamma(a, x, out _, out double q);
        return q;
    }

    private static void CheckGammaArgs(double a, double x)
    {
        Guard.PositiveFinite(a, "the shape parameter a", nameof(a));
        if (!(x >= 0)) throw new ArgumentException("x must be non-negative", nameof(x));
    }

    /// <summary>Inverse of P(a, x) in x: returns x with P(a, x) = p.</summary>
    public static double InverseRegularizedGammaP(double a, double p)
    {
        Guard.PositiveFinite(a, "the shape parameter a", nameof(a));
        Guard.Probability(p, "the probability p", nameof(p));
        return GammaPInverse(a, p, 1.0 - p);
    }

    /// <summary>Inverse of Q(a, x) in x: returns x with Q(a, x) = q.</summary>
    public static double InverseRegularizedGammaQ(double a, double q)
    {
        Guard.PositiveFinite(a, "the shape parameter a", nameof(a));
        Guard.Probability(q, "the probability q", nameof(q));
        return GammaPInverse(a, 1.0 - q, q);
    }

    /// <summary>x^a e^-x / Gamma(a), computed with the Lanczos approximation to avoid cancellation.</summary>
    internal static double GammaPrefix(double a, double x)
    {
        if (x == 0) return 0;
        if (double.IsPositiveInfinity(x)) return 0;
        double agh = a + LanczosGMinusHalf;
        double d = ((x - a) - LanczosGMinusHalf) / agh;
        double e;
        if (Math.Abs(d) < 0.5) e = a * Num.Log1pmx(d) - LanczosGMinusHalf * d;
        else e = a * (Math.Log(x) - Math.Log(agh)) + (agh - x);
        return Math.Exp(e) * Math.Sqrt(agh) / LanczosSum(a);
    }

    /// <summary>Computes P(a, x) and Q(a, x) together; the smaller of the two is accurate to full relative precision.</summary>
    internal static void IncompleteGamma(double a, double x, out double p, out double q)
    {
        if (double.IsNaN(x) || double.IsNaN(a)) { p = q = double.NaN; return; }
        if (x <= 0) { p = 0; q = 1; return; }
        if (double.IsPositiveInfinity(x)) { p = 1; q = 0; return; }
        if (a < 0.5 && x < 1.0)
        {
            double ps = GammaSeriesP(a, x);
            if (ps <= 0.5) { p = ps; q = 1.0 - ps; return; }
            double qs = GammaSmallAQ(a, x);
            q = Math.Clamp(qs, 0, 1);
            p = 1.0 - q;
            return;
        }
        bool series = a >= 1 ? x < a + 1 : x < 1;
        if (series)
        {
            p = Math.Clamp(GammaSeriesP(a, x), 0, 1);
            q = 1.0 - p;
        }
        else
        {
            q = Math.Clamp(GammaCfQ(a, x), 0, 1);
            p = 1.0 - q;
        }
    }

    private static int IterationCap(double size) => (int)Math.Min(20_000_000, 2000 + 50 * Math.Sqrt(size));

    private static double GammaSeriesP(double a, double x)
    {
        double prefix = GammaPrefix(a, x);
        if (prefix == 0) return 0;
        double sum = 1, term = 1;
        int cap = IterationCap(a + x);
        for (int n = 1; n < cap; n++)
        {
            term *= x / (a + n);
            sum += term;
            if (term < sum * 1e-17) break;
        }
        return prefix / a * sum;
    }

    private static double GammaCfQ(double a, double x)
    {
        double prefix = GammaPrefix(a, x);
        if (prefix == 0) return 0;
        const double tiny = 1e-300;
        double b = x + 1 - a, c = 1 / tiny, d = 1 / b, h = d;
        int cap = IterationCap(a + x);
        for (int i = 1; i < cap; i++)
        {
            double an = -i * (i - a);
            b += 2;
            d = an * d + b;
            if (Math.Abs(d) < tiny) d = tiny;
            c = b + an / c;
            if (Math.Abs(c) < tiny) c = tiny;
            d = 1 / d;
            double del = d * c;
            h *= del;
            if (Math.Abs(del - 1) < 1e-16) break;
        }
        return prefix * h;
    }

    /// <summary>Q(a, x) for small a and x &lt; 1, avoiding the cancellation in 1 - P.</summary>
    private static double GammaSmallAQ(double a, double x)
    {
        double g1pm1 = Gamma1pm1(a);
        double lnx = Math.Log(x);
        double xa = Math.Exp(a * lnx);
        double powm1 = Num.Expm1(a * lnx);
        double term = 1, s = 0;
        for (int n = 1; n < 500; n++)
        {
            term *= -x / n;
            double t = term / (a + n);
            s += t;
            if (Math.Abs(t) < 1e-17 * Math.Abs(s)) break;
        }
        double num = g1pm1 - powm1 - a * xa * s;
        return num / (1.0 + g1pm1);
    }

    /// <summary>Solves P(a, x) = p (with q = 1 - p supplied for upper tail accuracy).</summary>
    internal static double GammaPInverse(double a, double p, double q)
    {
        if (p <= 0) return 0;
        if (q <= 0) return double.PositiveInfinity;
        bool lower = p <= q;
        double x;
        if (a > 1)
        {
            double z = StdNormal.Quantile(p, q);
            double t = 1 - 1 / (9 * a) + z / (3 * Math.Sqrt(a));
            x = a * t * t * t;
            if (x <= 0) x = Math.Exp((Math.Log(p) + LogGamma(a + 1)) / a);
        }
        else
        {
            double t = 1 - a * (0.253 + a * 0.12);
            if (p < t) x = Math.Pow(p / t, 1 / a);
            else x = 1 - Math.Log(q / (1 - t));
        }
        if (!(x > 0) || !double.IsFinite(x)) x = Math.Max(a, 1e-3);

        double lo = 0, hi = double.PositiveInfinity;
        for (int iter = 0; iter < 300; iter++)
        {
            IncompleteGamma(a, x, out double px, out double qx);
            double f = lower ? px - p : q - qx;
            if (f == 0) return x;
            if (f > 0) hi = x; else lo = x;
            double dens = GammaPrefix(a, x) / x;
            double xNew = double.NaN;
            if (dens > 0 && double.IsFinite(dens))
            {
                double u = f / dens;
                double corr = 0.5 * u * ((a - 1) / x - 1);
                double step = Math.Abs(corr) < 1 ? u / (1 - corr) : u;
                xNew = x - step;
            }
            if (!(xNew > lo && xNew < hi))
            {
                if (double.IsPositiveInfinity(hi)) xNew = 2 * x + 1;
                else if (lo == 0) xNew = hi / 8;
                else xNew = hi / lo > 16 ? Math.Sqrt(lo * hi) : 0.5 * (lo + hi);
            }
            if (Math.Abs(xNew - x) <= 2e-16 * xNew) return xNew;
            x = xNew;
        }
        return x;
    }

    // ------------------------------------------------------------------ Incomplete beta

    /// <summary>Regularized incomplete beta function I_x(a, b).</summary>
    public static double RegularizedIncompleteBeta(double a, double b, double x)
    {
        Guard.PositiveFinite(a, "the parameter a", nameof(a));
        Guard.PositiveFinite(b, "the parameter b", nameof(b));
        if (!(x >= 0 && x <= 1)) throw new ArgumentException("x must be between 0 and 1", nameof(x));
        IncompleteBeta(a, b, x, 1.0 - x, out double ix, out _);
        return ix;
    }

    /// <summary>Inverse of the regularized incomplete beta function: returns x with I_x(a, b) = p.</summary>
    public static double InverseRegularizedIncompleteBeta(double a, double b, double p)
    {
        Guard.PositiveFinite(a, "the parameter a", nameof(a));
        Guard.PositiveFinite(b, "the parameter b", nameof(b));
        Guard.Probability(p, "the probability p", nameof(p));
        return IncompleteBetaInverse(a, b, p, 1.0 - p).X;
    }

    /// <summary>
    /// Computes I_x(a, b) and its complement together. y must equal 1 - x (pass it separately when it is known more accurately).
    /// </summary>
    internal static void IncompleteBeta(double a, double b, double x, double y, out double ix, out double iy)
    {
        if (double.IsNaN(x) || double.IsNaN(y)) { ix = iy = double.NaN; return; }
        if (x <= 0) { ix = 0; iy = 1; return; }
        if (y <= 0) { ix = 1; iy = 0; return; }
        if (b == 1)
        {
            double lnx = x < 0.5 ? Math.Log(x) : Num.Log1p(-y);
            ix = Math.Exp(a * lnx);
            iy = -Num.Expm1(a * lnx);
            return;
        }
        if (a == 1)
        {
            double lny = y < 0.5 ? Math.Log(y) : Num.Log1p(-x);
            iy = Math.Exp(b * lny);
            ix = -Num.Expm1(b * lny);
            return;
        }
        // lambda = (a + b) y - b (or a - (a + b) x), formed from whichever of x, y keeps it exact.
        // lambda >= 0 means x lies below the mean, where the continued fraction for I_x converges well.
        double lambda = a > b ? (a + b) * y - b : a - (a + b) * x;
        if (lambda >= 0)
        {
            double front = IbetaPowerTerms(a, b, x, y);
            ix = front == 0 ? 0 : Math.Clamp(front * Bfrac(a, b, x, y, lambda), 0, 1);
            iy = 1.0 - ix;
        }
        else
        {
            double front = IbetaPowerTerms(b, a, y, x);
            iy = front == 0 ? 0 : Math.Clamp(front * Bfrac(b, a, y, x, -lambda), 0, 1);
            ix = 1.0 - iy;
        }
    }

    /// <summary>
    /// Continued fraction for I_x(a, b) / (x^a y^b / B(a, b)) from Didonato and Morris (TOMS 708, BFRAC).
    /// lambda = (a + b) y - b must be supplied accurately and be non-negative.
    /// </summary>
    private static double Bfrac(double a, double b, double x, double y, double lambda)
    {
        double c = 1 + lambda, c0 = b / a, c1 = 1 + 1 / a, yp1 = y + 1;
        double n = 0, p = 1, s = a + 1;
        double an = 0, bn = 1, anp1 = 1, bnp1 = c / c1;
        double r = c1 / c;
        int cap = IterationCap(a + b);
        for (int it = 0; it < cap; it++)
        {
            n += 1;
            double t = n / a;
            double w = n * (b - n) * x;
            double e = a / s;
            double alpha = (p * (p + c0) * e * e) * (w * x);
            e = (1 + t) / (c1 + t + t);
            double beta = n + w / s + e * (c + n * yp1);
            p = 1 + t;
            s += 2;
            t = alpha * an + beta * anp1;
            an = anp1;
            anp1 = t;
            t = alpha * bn + beta * bnp1;
            bn = bnp1;
            bnp1 = t;
            double r0 = r;
            r = anp1 / bnp1;
            if (Math.Abs(r - r0) <= 4e-16 * r) break;
            an /= bnp1;
            bn /= bnp1;
            anp1 = r;
            bnp1 = 1;
        }
        return r;
    }

    /// <summary>x^a y^b / B(a, b) evaluated with the Lanczos approximation (no cancellation for large a, b).</summary>
    internal static double IbetaPowerTerms(double a, double b, double x, double y)
    {
        if (x == 0 || y == 0) return 0;
        double c = a + b;
        double agh = a + LanczosGMinusHalf, bgh = b + LanczosGMinusHalf, cgh = c + LanczosGMinusHalf;
        // l1 = x cgh / agh - 1 and l2 = y cgh / bgh - 1, formed from the smaller (exactly known) of x and y.
        double l1, l2;
        if (y < x)
        {
            l1 = (b - y * (agh + b)) / agh;
            l2 = (y * (a + bgh) - bgh) / bgh;
        }
        else
        {
            l1 = (x * (agh + b) - agh) / agh;
            l2 = (a - x * (a + bgh)) / bgh;
        }
        double la = Math.Abs(l1) < 0.5 ? Num.Log1p(l1) : Math.Log(x) + Math.Log(cgh / agh);
        double lb = Math.Abs(l2) < 0.5 ? Num.Log1p(l2) : Math.Log(y) + Math.Log(cgh / bgh);
        double e = a * la + b * lb + LanczosGMinusHalf;
        double prefix = LanczosSum(c) / LanczosSum(a) / LanczosSum(b) * Math.Sqrt(agh / cgh) * Math.Sqrt(bgh);
        return prefix * Math.Exp(e);
    }

    /// <summary>Solves I_x(a, b) = p, q = 1 - p. Returns x and 1 - x, the smaller of which is accurate.</summary>
    internal static (double X, double Y) IncompleteBetaInverse(double a, double b, double p, double q)
    {
        if (p <= 0) return (0, 1);
        if (q <= 0) return (1, 0);
        double x0 = BetaInverseGuess(a, b, p, q);
        if (x0 > 0.5)
        {
            var (yy, xx) = SolveBeta(b, a, q, p, 1.0 - x0);
            return (xx, yy);
        }
        return SolveBeta(a, b, p, q, x0);
    }

    private static double BetaInverseGuess(double a, double b, double p, double q)
    {
        double x;
        if (a >= 1 && b >= 1)
        {
            double pp = p < 0.5 ? p : q;
            double t = Math.Sqrt(-2 * Math.Log(pp));
            double z = (2.30753 + t * 0.27061) / (1 + t * (0.99229 + t * 0.04481)) - t;
            if (p < 0.5) z = -z;
            double al = (z * z - 3) / 6;
            double h = 2 / (1 / (2 * a - 1) + 1 / (2 * b - 1));
            double w = z * Math.Sqrt(al + h) / h - (1 / (2 * b - 1) - 1 / (2 * a - 1)) * (al + 5.0 / 6 - 2 / (3 * h));
            x = a / (a + b * Math.Exp(2 * w));
        }
        else
        {
            double lna = Math.Log(a / (a + b)), lnb = Math.Log(b / (a + b));
            double t = Math.Exp(a * lna) / a;
            double u = Math.Exp(b * lnb) / b;
            double w = t + u;
            if (p < t / w) x = Math.Pow(a * w * p, 1 / a);
            else x = 1 - Math.Pow(b * w * q, 1 / b);
        }
        if (!(x > 0) || double.IsNaN(x)) x = 1e-300;
        if (x >= 1) x = 1 - 1e-16;
        return x;
    }

    private static (double X, double Y) SolveBeta(double a, double b, double p, double q, double x)
    {
        bool lower = p <= q;
        double lo = 0, hi = 1;
        if (!(x > 0 && x < 1)) x = 0.5;
        for (int iter = 0; iter < 400; iter++)
        {
            double y = 1.0 - x;
            IncompleteBeta(a, b, x, y, out double ix, out double iy);
            double f = lower ? ix - p : q - iy;
            if (f == 0) return (x, y);
            if (f > 0) hi = x; else lo = x;
            double dens = IbetaPowerTerms(a, b, x, y) / (x * y);
            double xNew = double.NaN;
            if (dens > 0 && double.IsFinite(dens))
            {
                double u = f / dens;
                double corr = 0.5 * u * ((a - 1) / x - (b - 1) / y);
                double step = Math.Abs(corr) < 1 ? u / (1 - corr) : u;
                xNew = x - step;
            }
            if (!(xNew > lo && xNew < hi))
            {
                if (lo == 0) xNew = hi / 8;
                else if (hi == 1) xNew = 0.5 * (lo + 1);
                else xNew = hi / lo > 16 ? Math.Sqrt(lo * hi) : 0.5 * (lo + hi);
            }
            if (Math.Abs(xNew - x) <= 2e-16 * xNew)
            {
                return (xNew, 1.0 - xNew);
            }
            x = xNew;
        }
        return (x, 1.0 - x);
    }

    // ------------------------------------------------------------------ Lambert W

    /// <summary>
    /// Lambert W function: the solution w of w e^w = x. Branch 0 (principal, x &gt;= -1/e, w &gt;= -1) or
    /// branch -1 (-1/e &lt;= x &lt; 0, w &lt;= -1).
    /// </summary>
    public static double LambertW(double x, int branch = 0)
    {
        if (branch != 0 && branch != -1) throw new ArgumentException("the Lambert W branch must be 0 or -1", nameof(branch));
        if (double.IsNaN(x)) return x;
        if (branch == 0)
        {
            if (x == 0) return x;
            if (double.IsPositiveInfinity(x)) return x;
        }
        else
        {
            if (!(x < 0)) throw new ArgumentException("the -1 branch of Lambert W requires -1/e <= x < 0", nameof(x));
        }
        double d = (x + InvEHi) + InvELo;
        if (d < 0)
        {
            if (d > -4 * Num.Eps * InvEHi) return -1.0;
            throw new ArgumentException("Lambert W is real only for x >= -1/e", nameof(x));
        }
        double p = Math.Sqrt(2.0 * Math.E * d);
        if (branch == -1) p = -p;
        if (Math.Abs(p) <= 0.5)
        {
            double s = 0;
            for (int k = LambertBranchSeries.Length - 1; k >= 0; k--) s = s * p + LambertBranchSeries[k];
            return s;
        }
        double w;
        if (branch == -1 || x > 3)
        {
            // Newton on g(w) = w + ln|w| - ln|x|, well conditioned away from the branch point.
            double lx = Math.Log(Math.Abs(x));
            double l2 = Math.Log(Math.Abs(lx));
            w = lx - l2 + l2 / lx;
            if (branch == -1 && w > -1.2) w = -1 + p - p * p / 3;
            for (int i = 0; i < 100; i++)
            {
                double g = w + Math.Log(Math.Abs(w)) - lx;
                double wNew = w - g / (1 + 1 / w);
                if (branch == -1 && wNew >= -1) wNew = 0.5 * (w - 1);
                bool done = Math.Abs(wNew - w) <= 1e-15 * Math.Abs(wNew);
                w = wNew;
                if (done) break;
            }
            return w;
        }
        double l1 = Num.Log1p(x);
        w = l1 * (1 - Num.Log1p(l1) / (2 + l1));
        for (int i = 0; i < 100; i++)
        {
            double ew = Math.Exp(w);
            double f = w * ew - x;
            double wp1 = w + 1;
            double wNew = w - f / (ew * wp1 - (w + 2) * f / (2 * wp1));
            bool done = Math.Abs(wNew - w) <= 1e-15 * Math.Abs(wNew) || f == 0;
            w = wNew;
            if (done) break;
        }
        return w;
    }

    // ------------------------------------------------------------------ Riemann zeta

    private const int BorweinN = 30;
    private static readonly double[] BorweinD = BuildBorwein();

    private static double[] BuildBorwein()
    {
        int n = BorweinN;
        var d = new double[n + 1];
        double t = 1, sum = 1;
        d[0] = 1;
        for (int i = 1; i <= n; i++)
        {
            t *= 4.0 * (n + i - 1) * (n - i + 1) / ((2.0 * i) * (2.0 * i - 1));
            sum += t;
            d[i] = sum;
        }
        return d;
    }

    /// <summary>The Riemann zeta function for real s (s != 1). Returns NaN at the pole s = 1.</summary>
    public static double RiemannZeta(double s)
    {
        if (double.IsNaN(s)) return s;
        if (s == 1) return double.NaN;
        if (double.IsPositiveInfinity(s)) return 1.0;
        if (double.IsNegativeInfinity(s)) return double.NaN;
        if (s == 0) return -0.5;
        if (s >= 50)
        {
            double sum = 1;
            for (int k = 2; k < 20; k++)
            {
                double t = Math.Pow(k, -s);
                sum += t;
                if (t < 1e-18) break;
            }
            return sum;
        }
        if (s > 0)
        {
            double dn = BorweinD[BorweinN];
            double acc = 0;
            for (int k = 0; k < BorweinN; k++)
            {
                double term = (dn - BorweinD[k]) / dn * Math.Pow(k + 1, -s);
                acc += (k % 2 == 0) ? term : -term;
            }
            // acc = eta(s); zeta = eta / (1 - 2^(1-s))
            double denom = -Num.Expm1((1 - s) * Num.Ln2);
            return acc / denom;
        }
        // Functional equation for s < 0.
        double sinTerm = Num.SinPi(s / 2);
        if (sinTerm == 0) return 0.0;
        double oneMinusS = 1 - s;
        double zeta1ms = RiemannZeta(oneMinusS);
        if (oneMinusS < 170)
        {
            return Math.Pow(2, s) * Math.Pow(Math.PI, s - 1) * sinTerm * Gamma(oneMinusS) * zeta1ms;
        }
        double lnMag = s * Num.Ln2 + (s - 1) * Num.LnPi + Math.Log(Math.Abs(sinTerm)) + LogGamma(oneMinusS) + Math.Log(zeta1ms);
        return Math.Sign(sinTerm) * Math.Exp(lnMag);
    }

    // ------------------------------------------------------------------ Binomial coefficients

    /// <summary>
    /// Binomial coefficient C(n, k). Exact (correctly rounded) for integer arguments whose result fits a double,
    /// generalized to real n and k through the gamma function. Negative integer n uses C(n, k) = (-1)^k C(k - n - 1, k).
    /// </summary>
    public static double Binomial(double n, double k)
    {
        if (double.IsNaN(n) || double.IsNaN(k)) return double.NaN;
        if (Num.IsInteger(n) && Num.IsInteger(k))
        {
            if (k < 0) return 0;
            if (n >= 0)
            {
                if (k > n) return 0;
                return BinomialInteger(n, k);
            }
            double v = BinomialInteger(k - n - 1, k);
            return Math.IEEERemainder(k, 2) == 0 ? v : -v;
        }
        if (!double.IsFinite(n) || !double.IsFinite(k)) return double.NaN;
        if (Num.IsInteger(k) && k >= 0 && k <= 60)
        {
            double r = 1;
            for (int i = 1; i <= (int)k; i++) r *= (n - k + i) / i;
            return r;
        }
        if (n > -1 && k > -1 && n - k > -1) return 1.0 / ((n + 1) * Beta(n - k + 1, k + 1));
        double a = n + 1, b = k + 1, c = n - k + 1;
        bool poleA = a <= 0 && Num.IsInteger(a), poleB = b <= 0 && Num.IsInteger(b), poleC = c <= 0 && Num.IsInteger(c);
        if (poleB || poleC) return poleA ? double.NaN : 0.0;
        if (poleA) return double.NaN;
        double sign = GammaSign(a) * GammaSign(b) * GammaSign(c);
        return sign * Math.Exp(LogGamma(a) - LogGamma(b) - LogGamma(c));
    }

    private static double BinomialInteger(double n, double k)
    {
        k = Math.Min(k, n - k);
        if (k == 0) return 1;
        if (k == 1) return n;
        double est = LogGamma(n + 1) - LogGamma(k + 1) - LogGamma(n - k + 1);
        if (est > 720) return double.PositiveInfinity;
        if (k > 5000) return Math.Exp(est);
        var big = new BigInteger(n);
        long kk = (long)k;
        BigInteger start = big - kk;
        BigInteger r = BigInteger.One;
        for (long i = 1; i <= kk; i++) r = r * (start + i) / i;
        return (double)r;
    }

    /// <summary>Number of permutations P(n, r) = n! / (n - r)!. Exact for integers when the result fits a double.</summary>
    public static double Permutations(double n, double r)
    {
        if (double.IsNaN(n) || double.IsNaN(r)) return double.NaN;
        if (Num.IsInteger(n) && Num.IsInteger(r) && n >= 0)
        {
            if (r < 0 || r > n) return 0;
            if (r == 0) return 1;
            double est = LogGamma(n + 1) - LogGamma(n - r + 1);
            if (est > 720) return double.PositiveInfinity;
            var big = new BigInteger(n);
            BigInteger prod = BigInteger.One;
            for (long i = 0; i < (long)r; i++) prod *= big - i;
            return (double)prod;
        }
        return Gamma(n + 1) / Gamma(n - r + 1);
    }
}

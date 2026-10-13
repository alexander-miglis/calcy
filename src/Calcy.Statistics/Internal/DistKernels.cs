namespace Calcy.Statistics.Internal;

/// <summary>
/// Saddle point density kernels (Loader 2000) used for accurate binomial, Poisson, gamma, beta and F densities.
/// </summary>
internal static class DistKernels
{
    private const double Ln2Pi = 1.8378770664093454835606594728112;

    /// <summary>stirlerr(n) = log(n!) - log(sqrt(2 pi n) (n/e)^n).</summary>
    public static double StirlingError(double n)
    {
        const double S0 = 1.0 / 12, S1 = 1.0 / 360, S2 = 1.0 / 1260, S3 = 1.0 / 1680, S4 = 1.0 / 1188;
        if (n <= 15.0)
        {
            double nn2 = n + n;
            if (nn2 == Math.Floor(nn2) && nn2 >= 0) return SpecialFunctions.StirlingErrorHalves[(int)nn2];
            return SpecialFunctions.LogGamma(n + 1.0) - (n + 0.5) * Math.Log(n) + n - Num.LnSqrt2Pi;
        }
        double nn = n * n;
        if (n > 500) return (S0 - S1 / nn) / n;
        if (n > 80) return (S0 - (S1 - S2 / nn) / nn) / n;
        if (n > 35) return (S0 - (S1 - (S2 - S3 / nn) / nn) / nn) / n;
        return (S0 - (S1 - (S2 - (S3 - S4 / nn) / nn) / nn) / nn) / n;
    }

    /// <summary>Deviance term bd0(x, np) = x log(x / np) + np - x, computed without cancellation.</summary>
    public static double Bd0(double x, double np)
    {
        if (Math.Abs(x - np) < 0.1 * (x + np))
        {
            double v = (x - np) / (x + np);
            double s = (x - np) * v;
            if (Math.Abs(s) < Num.MinNormal) return s;
            double ej = 2 * x * v;
            v *= v;
            for (int j = 1; j < 1000; j++)
            {
                ej *= v;
                double s1 = s + ej / (2 * j + 1);
                if (s1 == s) return s1;
                s = s1;
            }
        }
        return x * Math.Log(x / np) + np - x;
    }

    /// <summary>Binomial probability C(n, x) p^x q^(n-x) for real x and n (q = 1 - p).</summary>
    public static double BinomRaw(double x, double n, double p, double q, bool log = false)
    {
        double lc;
        if (p == 0) return x == 0 ? One(log) : Zero(log);
        if (q == 0) return x == n ? One(log) : Zero(log);
        if (x == 0)
        {
            if (n == 0) return One(log);
            lc = p < 0.1 ? -Bd0(n, n * q) - n * p : n * Math.Log(q);
            return log ? lc : Math.Exp(lc);
        }
        if (x == n)
        {
            lc = q < 0.1 ? -Bd0(n, n * p) - n * q : n * Math.Log(p);
            return log ? lc : Math.Exp(lc);
        }
        if (x < 0 || x > n) return Zero(log);
        lc = StirlingError(n) - StirlingError(x) - StirlingError(n - x) - Bd0(x, n * p) - Bd0(n - x, n * q);
        double lf = Ln2Pi + Math.Log(x) + Num.Log1p(-x / n);
        return log ? lc - 0.5 * lf : Math.Exp(lc - 0.5 * lf);
    }

    /// <summary>Poisson probability lambda^x e^-lambda / Gamma(x + 1) for real x.</summary>
    public static double PoissonRaw(double x, double lambda, bool log = false)
    {
        if (lambda == 0) return x == 0 ? One(log) : Zero(log);
        if (!double.IsFinite(lambda)) return Zero(log);
        if (x < 0) return Zero(log);
        if (x <= lambda * Num.MinNormal) return log ? -lambda : Math.Exp(-lambda);
        if (lambda < x * Num.MinNormal)
        {
            if (!double.IsFinite(x)) return Zero(log);
            double v = -lambda + x * Math.Log(lambda) - SpecialFunctions.LogGamma(x + 1);
            return log ? v : Math.Exp(v);
        }
        double e = -StirlingError(x) - Bd0(x, lambda);
        return log ? -0.5 * Math.Log(2 * Math.PI * x) + e : Math.Exp(e) / Math.Sqrt(2 * Math.PI * x);
    }

    private static double One(bool log) => log ? 0.0 : 1.0;
    private static double Zero(bool log) => log ? double.NegativeInfinity : 0.0;
}

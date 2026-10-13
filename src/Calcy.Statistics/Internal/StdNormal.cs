namespace Calcy.Statistics.Internal;

/// <summary>Standard normal distribution kernels with accurate tails.</summary>
internal static class StdNormal
{
    public static double Pdf(double z) => Num.InvSqrt2Pi * Num.ExpMinusHalfSquare(z);

    /// <summary>Phi(z), the standard normal lower tail. Accurate to full relative precision in both tails.</summary>
    public static double Cdf(double z)
    {
        if (double.IsNaN(z)) return z;
        double az = Math.Abs(z);
        if (az < 0.6) return 0.5 + 0.5 * SpecialFunctions.Erf(z * Num.InvSqrt2);
        if (az > 40) return z < 0 ? 0.0 : 1.0;
        double tail = 0.5 * SpecialFunctions.Erfcx(az * Num.InvSqrt2) * Num.ExpMinusHalfSquare(az);
        return z < 0 ? tail : 1.0 - tail;
    }

    /// <summary>Phi(z) / phi(z) for z &lt;= 0 (Mills ratio of the lower tail), never underflows.</summary>
    public static double MillsLower(double z) => Num.SqrtHalfPi * SpecialFunctions.Erfcx(-z * Num.InvSqrt2);

    /// <summary>Quantile of the standard normal with p + q = 1 (both supplied so that tiny upper tails are exact).</summary>
    public static double Quantile(double p, double q)
    {
        if (p <= 0) return double.NegativeInfinity;
        if (q <= 0) return double.PositiveInfinity;
        double qc = p <= 0.5 ? p - 0.5 : 0.5 - q;
        double z = As241(qc, Math.Min(p, q));
        if (z == 0 || !double.IsFinite(z)) return z;
        double u;
        if (z < 0)
        {
            double c = Cdf(z);
            if (c == 0) return z;
            u = MillsLower(z) * (1.0 - p / c);
        }
        else
        {
            double c = Cdf(-z);
            if (c == 0) return z;
            u = MillsLower(-z) * (q / c - 1.0);
        }
        return z - u / (1.0 + 0.5 * z * u);
    }

    public static double Quantile(double p) => Quantile(p, 1.0 - p);

    private static readonly double[] A =
    [
        3.3871328727963666080e0, 1.3314166789178437745e+2, 1.9715909503065514427e+3, 1.3731693765509461125e+4,
        4.5921953931549871457e+4, 6.7265770927008700853e+4, 3.3430575583588128105e+4, 2.5090809287301226727e+3,
    ];
    private static readonly double[] B =
    [
        1.0, 4.2313330701600911252e+1, 6.8718700749205790830e+2, 5.3941960214247511077e+3,
        2.1213794301586595867e+4, 3.9307895800092710610e+4, 2.8729085735721942674e+4, 5.2264952788528545610e+3,
    ];
    private static readonly double[] C =
    [
        1.42343711074968357734e0, 4.63033784615654529590e0, 5.76949722146069140550e0, 3.64784832476320460504e0,
        1.27045825245236838258e0, 2.41780725177450611770e-1, 2.27238449892691845833e-2, 7.74545014278341407640e-4,
    ];
    private static readonly double[] D =
    [
        1.0, 2.05319162663775882187e0, 1.67638483018380384940e0, 6.89767334985100004550e-1,
        1.48103976427480074590e-1, 1.51986665636164571966e-2, 5.47593808499534494600e-4, 1.05075007164441684324e-9,
    ];
    private static readonly double[] E =
    [
        6.65790464350110377720e0, 5.46378491116411436990e0, 1.78482653991729133580e0, 2.96560571828504891230e-1,
        2.65321895265761230930e-2, 1.24266094738807843860e-3, 2.71155556874348757815e-5, 2.01033439929228813265e-7,
    ];
    private static readonly double[] F =
    [
        1.0, 5.99832206555887937690e-1, 1.36929880922735805310e-1, 1.48753612908506148525e-2,
        7.86869131145613259100e-4, 1.84631831751005468180e-5, 1.42151175831644588870e-7, 2.04426310338993978564e-15,
    ];

    private static double Poly(double[] c, double x)
    {
        double s = 0;
        for (int i = c.Length - 1; i >= 0; i--) s = s * x + c[i];
        return s;
    }

    /// <summary>
    /// Wichura's AS 241 (PPND16) normal quantile, about 1e-16 relative accuracy.
    /// qc = p - 0.5 and tail = min(p, 1 - p) are passed separately to keep full precision.
    /// </summary>
    public static double As241(double qc, double tail)
    {
        if (Math.Abs(qc) <= 0.425)
        {
            double r = 0.180625 - qc * qc;
            return qc * Poly(A, r) / Poly(B, r);
        }
        if (tail <= 0) return qc < 0 ? double.NegativeInfinity : double.PositiveInfinity;
        double rr = Math.Sqrt(-Math.Log(tail));
        double val;
        if (rr <= 5)
        {
            rr -= 1.6;
            val = Poly(C, rr) / Poly(D, rr);
        }
        else
        {
            rr -= 5;
            val = Poly(E, rr) / Poly(F, rr);
        }
        return qc < 0 ? -val : val;
    }
}

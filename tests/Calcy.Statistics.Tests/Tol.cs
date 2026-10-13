namespace Calcy.Statistics.Tests;

/// <summary>Floating point assertions with relative and absolute tolerances.</summary>
internal static class Tol
{
    /// <summary>Asserts |actual - expected| &lt;= tol |expected| (exact match required for 0, infinities and NaN).</summary>
    public static void Rel(double expected, double actual, double tol)
    {
        if (double.IsNaN(expected))
        {
            Assert.True(double.IsNaN(actual), $"expected NaN but got {actual:R}");
            return;
        }
        if (double.IsInfinity(expected) || expected == 0)
        {
            Assert.True(expected == actual, $"expected {expected:R} but got {actual:R}");
            return;
        }
        double err = Math.Abs(actual - expected) / Math.Abs(expected);
        Assert.True(err <= tol, $"expected {expected:R} but got {actual:R} (relative error {err:E3}, tolerance {tol:E1})");
    }

    /// <summary>Asserts |actual - expected| &lt;= tol.</summary>
    public static void Abs(double expected, double actual, double tol)
    {
        double err = Math.Abs(actual - expected);
        Assert.True(err <= tol, $"expected {expected:R} but got {actual:R} (absolute error {err:E3}, tolerance {tol:E1})");
    }

    /// <summary>Asserts that actual agrees with a TI-84 display value to the given number of significant digits.</summary>
    public static void Digits(double tiDisplay, double actual, int digits = 10)
    {
        if (tiDisplay == 0)
        {
            Assert.True(Math.Abs(actual) < Math.Pow(10, -digits), $"expected 0 but got {actual:R}");
            return;
        }
        double scale = Math.Pow(10, Math.Floor(Math.Log10(Math.Abs(tiDisplay))) - digits + 1);
        double err = Math.Abs(actual - tiDisplay);
        Assert.True(err <= 0.5 * scale + 1e-15 * Math.Abs(tiDisplay), $"TI shows {tiDisplay:R} but got {actual:R} (difference {err:E3}, allowed {0.5 * scale:E3})");
    }
}

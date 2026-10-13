using System.Globalization;
using System.Text;
using Calcy.Statistics.Internal;

namespace Calcy.Statistics;

/// <summary>Regression models with TI-84 parameterisation.</summary>
public enum RegressionModel
{
    /// <summary>LinReg(ax+b): y = a x + b.</summary>
    LinearAxB,
    /// <summary>LinReg(a+bx): y = a + b x.</summary>
    LinearABx,
    /// <summary>Med-Med: y = a x + b through the median-median summary points.</summary>
    MedianMedian,
    /// <summary>QuadReg: y = a x^2 + b x + c.</summary>
    Quadratic,
    /// <summary>CubicReg: y = a x^3 + b x^2 + c x + d.</summary>
    Cubic,
    /// <summary>QuartReg: y = a x^4 + b x^3 + c x^2 + d x + e.</summary>
    Quartic,
    /// <summary>Polynomial of any degree (use <see cref="Regression.FitPolynomial"/>).</summary>
    Polynomial,
    /// <summary>LnReg: y = a + b ln x.</summary>
    Logarithmic,
    /// <summary>ExpReg: y = a b^x, least squares on ln y.</summary>
    Exponential,
    /// <summary>Casio a e^(bx): y = a e^(b x), least squares on ln y.</summary>
    ExponentialE,
    /// <summary>PwrReg: y = a x^b, least squares on ln y against ln x.</summary>
    Power,
    /// <summary>Logistic: y = c / (1 + a e^(-b x)), nonlinear least squares.</summary>
    Logistic,
    /// <summary>SinReg: y = a sin(b x + c) + d, nonlinear least squares.</summary>
    Sinusoidal,
}

/// <summary>A named fitted parameter with its standard error when available.</summary>
public sealed record RegressionParameter(string Name, double Value, double? StandardError = null);

/// <summary>
/// Result of a regression. For Exponential, ExponentialE and Power the statistics R, RSquared, AdjustedRSquared and
/// StandardError describe the linearised least squares fit (ln y against x or ln x), exactly as the TI-84 reports r
/// and r^2 for ExpReg and PwrReg; Residuals and Fitted are always on the original y scale. For the polynomial,
/// median-median, logistic and sinusoidal models R is null and RSquared is 1 - SSE / SST on the original scale.
/// </summary>
public sealed record RegressionResult
{
    /// <summary>The model that was fitted.</summary>
    public required RegressionModel Model { get; init; }
    /// <summary>Parameters in TI order (a, b, c, ...).</summary>
    public required IReadOnlyList<RegressionParameter> Parameters { get; init; }
    /// <summary>The equation with parameter names, for example "y = a*x + b".</summary>
    public required string EquationTemplate { get; init; }
    /// <summary>The equation with the fitted values substituted (10 significant digits).</summary>
    public required string Equation { get; init; }
    /// <summary>Correlation coefficient r where the TI-84 defines one (linear, logarithmic, exponential, power), else null.</summary>
    public double? R { get; init; }
    /// <summary>Coefficient of determination.</summary>
    public required double RSquared { get; init; }
    /// <summary>Adjusted coefficient of determination 1 - (1 - R^2)(n - 1) / (n - p - 1); NaN when undefined.</summary>
    public required double AdjustedRSquared { get; init; }
    /// <summary>Residual standard error s = sqrt(SSE / df).</summary>
    public required double StandardError { get; init; }
    /// <summary>Standard error of the slope (linear models only).</summary>
    public double? SlopeStandardError { get; init; }
    /// <summary>Standard error of the intercept (linear models only).</summary>
    public double? InterceptStandardError { get; init; }
    /// <summary>Residuals y - f(x) on the original scale (TI RESID list).</summary>
    public required IReadOnlyList<double> Residuals { get; init; }
    /// <summary>Fitted values f(x).</summary>
    public required IReadOnlyList<double> Fitted { get; init; }
    /// <summary>The fitted function.</summary>
    public required Func<double, double> Predict { get; init; }
    /// <summary>Number of observations (sum of the weights).</summary>
    public required double N { get; init; }
    /// <summary>Residual degrees of freedom.</summary>
    public required double DegreesOfFreedom { get; init; }
    /// <summary>Residual sum of squares of the fitted (possibly linearised) problem.</summary>
    public required double ResidualSumOfSquares { get; init; }
    /// <summary>For iterative models, whether the solver converged.</summary>
    public bool Converged { get; init; } = true;
    /// <summary>For iterative models, the number of iterations.</summary>
    public int Iterations { get; init; }

    internal Func<int, string>? Formatter { get; init; }

    /// <summary>Value of the named parameter (for example "a").</summary>
    public double this[string name]
    {
        get
        {
            foreach (var p in Parameters) if (p.Name == name) return p.Value;
            throw new ArgumentException($"the model has no parameter named '{name}'", nameof(name));
        }
    }

    /// <summary>The parameter values in order.</summary>
    public double[] Coefficients => Parameters.Select(p => p.Value).ToArray();

    /// <summary>The equation with values formatted to the given number of significant digits.</summary>
    public string FormatEquation(int significantDigits) => Formatter is null ? Equation : Formatter(significantDigits);
}

/// <summary>Result of a multiple linear regression y = b0 + b1 x1 + ... + bk xk.</summary>
public sealed record MultipleRegressionResult
{
    /// <summary>Coefficients b0 (intercept), b1, ..., bk.</summary>
    public required IReadOnlyList<double> Coefficients { get; init; }
    /// <summary>Standard errors of the coefficients.</summary>
    public required IReadOnlyList<double> StandardErrors { get; init; }
    /// <summary>t statistics b / SE(b).</summary>
    public required IReadOnlyList<double> TStatistics { get; init; }
    /// <summary>Two-sided p-values of the t statistics.</summary>
    public required IReadOnlyList<double> PValues { get; init; }
    /// <summary>Coefficient of determination.</summary>
    public required double RSquared { get; init; }
    /// <summary>Adjusted R^2.</summary>
    public required double AdjustedRSquared { get; init; }
    /// <summary>Overall F statistic.</summary>
    public required double F { get; init; }
    /// <summary>p-value of the F statistic.</summary>
    public required double FPValue { get; init; }
    /// <summary>Residual standard error.</summary>
    public required double StandardError { get; init; }
    /// <summary>Regression degrees of freedom (k).</summary>
    public required double RegressionDegreesOfFreedom { get; init; }
    /// <summary>Residual degrees of freedom (n - k - 1).</summary>
    public required double ResidualDegreesOfFreedom { get; init; }
    /// <summary>Regression sum of squares.</summary>
    public required double RegressionSumOfSquares { get; init; }
    /// <summary>Residual sum of squares.</summary>
    public required double ResidualSumOfSquares { get; init; }
    /// <summary>Total sum of squares about the mean.</summary>
    public required double TotalSumOfSquares { get; init; }
    /// <summary>Residuals.</summary>
    public required IReadOnlyList<double> Residuals { get; init; }
    /// <summary>Fitted values.</summary>
    public required IReadOnlyList<double> Fitted { get; init; }
    /// <summary>Coefficient covariance matrix.</summary>
    public required double[,] Covariance { get; init; }
    /// <summary>Predicts y for one observation of the predictors (x1, ..., xk).</summary>
    public required Func<double[], double> Predict { get; init; }

    /// <summary>t confidence intervals for every coefficient.</summary>
    public IReadOnlyList<ConfidenceInterval> CoefficientIntervals(double level = 0.95)
    {
        double c = ConfidenceInterval.NormalizeLevel(level);
        double tcrit = new StudentTDistribution(ResidualDegreesOfFreedom).InverseSurvival((1 - c) / 2);
        var list = new List<ConfidenceInterval>();
        for (int i = 0; i < Coefficients.Count; i++)
        {
            double me = tcrit * StandardErrors[i];
            list.Add(new ConfidenceInterval
            {
                Name = i == 0 ? "b0" : $"b{i}",
                Lower = Coefficients[i] - me,
                Upper = Coefficients[i] + me,
                PointEstimate = Coefficients[i],
                MarginOfError = me,
                ConfidenceLevel = c,
                DegreesOfFreedom = ResidualDegreesOfFreedom,
            });
        }
        return list;
    }
}

/// <summary>Regression fitting with TI-84 semantics. Optional weights act as frequencies (TI FreqList).</summary>
public static class Regression
{
    /// <summary>
    /// Fits the given model. Use <see cref="FitPolynomial"/> for arbitrary degree and <see cref="FitSinusoidal"/> to
    /// supply a period guess.
    /// </summary>
    public static RegressionResult Fit(RegressionModel model, IReadOnlyList<double> x, IReadOnlyList<double> y, IReadOnlyList<double>? weights = null)
    {
        Check(x, y, weights);
        return model switch
        {
            RegressionModel.LinearAxB => Linear(x, y, weights, axb: true),
            RegressionModel.LinearABx => Linear(x, y, weights, axb: false),
            RegressionModel.MedianMedian => MedianMedian(x, y, weights),
            RegressionModel.Quadratic => PolynomialFit(2, x, y, weights, RegressionModel.Quadratic),
            RegressionModel.Cubic => PolynomialFit(3, x, y, weights, RegressionModel.Cubic),
            RegressionModel.Quartic => PolynomialFit(4, x, y, weights, RegressionModel.Quartic),
            RegressionModel.Polynomial => throw new ArgumentException("use FitPolynomial(degree, x, y) to fit a polynomial of a chosen degree", nameof(model)),
            RegressionModel.Logarithmic => Logarithmic(x, y, weights),
            RegressionModel.Exponential => ExponentialFit(x, y, weights, baseForm: true),
            RegressionModel.ExponentialE => ExponentialFit(x, y, weights, baseForm: false),
            RegressionModel.Power => PowerFit(x, y, weights),
            RegressionModel.Logistic => FitLogistic(x, y, weights),
            RegressionModel.Sinusoidal => FitSinusoidal(x, y, null, weights),
            _ => throw new ArgumentException("unknown regression model", nameof(model)),
        };
    }

    /// <summary>Least squares polynomial of the given degree, coefficients a (highest power) first.</summary>
    public static RegressionResult FitPolynomial(int degree, IReadOnlyList<double> x, IReadOnlyList<double> y, IReadOnlyList<double>? weights = null)
    {
        if (degree < 1) throw new ArgumentException("the polynomial degree must be at least 1", nameof(degree));
        Check(x, y, weights);
        return PolynomialFit(degree, x, y, weights, degree switch
        {
            2 => RegressionModel.Quadratic,
            3 => RegressionModel.Cubic,
            4 => RegressionModel.Quartic,
            _ => RegressionModel.Polynomial,
        });
    }

    private static void Check(IReadOnlyList<double> x, IReadOnlyList<double> y, IReadOnlyList<double>? w)
    {
        Guard.AllFinite(x, nameof(x));
        Guard.AllFinite(y, nameof(y));
        Guard.SameLength(x.Count, y.Count, "x and y");
        if (w is not null) Descriptive.CheckFrequencies(w, x.Count);
        if (x.Count < 2) throw new ArgumentException("at least two data points are required", nameof(x));
    }

    private static double W(IReadOnlyList<double>? w, int i) => w is null ? 1.0 : w[i];

    // ------------------------------------------------------------------ simple linear core

    internal sealed record SimpleFit(double Intercept, double Slope, double N, double MeanX, double Sxx, double Syy, double Sxy,
        double Sse, double Df, double S, double SeSlope, double SeIntercept, double R, double[] Residuals);

    internal static SimpleFit SimpleLinear(IReadOnlyList<double> x, IReadOnlyList<double> y, IReadOnlyList<double>? w)
    {
        int n = x.Count;
        var sw = new Num.Accumulator();
        var sx = new Num.Accumulator();
        var sy = new Num.Accumulator();
        for (int i = 0; i < n; i++)
        {
            double wi = W(w, i);
            sw.Add(wi);
            sx.Add(wi * x[i]);
            sy.Add(wi * y[i]);
        }
        double N = sw.Value, mx = sx.Value / N, my = sy.Value / N;
        var sxx = new Num.Accumulator();
        var syy = new Num.Accumulator();
        var sxy = new Num.Accumulator();
        for (int i = 0; i < n; i++)
        {
            double wi = W(w, i);
            double dx = x[i] - mx, dy = y[i] - my;
            sxx.Add(wi * dx * dx);
            syy.Add(wi * dy * dy);
            sxy.Add(wi * dx * dy);
        }
        double Sxx = sxx.Value, Syy = syy.Value, Sxy = sxy.Value;
        if (!(Sxx > 0)) throw new ArgumentException("the x values must not all be equal");
        double slope = Sxy / Sxx;
        double intercept = my - slope * mx;
        var res = new double[n];
        var sse = new Num.Accumulator();
        for (int i = 0; i < n; i++)
        {
            res[i] = y[i] - (intercept + slope * x[i]);
            sse.Add(W(w, i) * res[i] * res[i]);
        }
        double df = N - 2;
        double s = df > 0 ? Math.Sqrt(sse.Value / df) : double.NaN;
        double r = Syy > 0 ? Math.Clamp(Sxy / Math.Sqrt(Sxx * Syy), -1, 1) : double.NaN;
        return new SimpleFit(intercept, slope, N, mx, Sxx, Syy, Sxy, sse.Value, df, s, s / Math.Sqrt(Sxx), s * Math.Sqrt(1 / N + mx * mx / Sxx), r, res);
    }

    private static double Adjusted(double r2, double n, double predictors)
    {
        double denom = n - predictors - 1;
        return denom > 0 ? 1 - (1 - r2) * (n - 1) / denom : double.NaN;
    }

    private static RegressionResult Linear(IReadOnlyList<double> x, IReadOnlyList<double> y, IReadOnlyList<double>? w, bool axb)
    {
        var f = SimpleLinear(x, y, w);
        double a = f.Intercept, b = f.Slope;
        var fitted = new double[x.Count];
        for (int i = 0; i < x.Count; i++) fitted[i] = a + b * x[i];
        double r2 = f.R * f.R;
        IReadOnlyList<RegressionParameter> pars = axb
            ? [new("a", b, f.SeSlope), new("b", a, f.SeIntercept)]
            : [new("a", a, f.SeIntercept), new("b", b, f.SeSlope)];
        Func<int, string> fmt = axb
            ? d => new Eq(d).Start().Term(b, "*x").Constant(a).ToString()
            : d => new Eq(d).Start().Lead(a).Term(b, "*x").ToString();
        return new RegressionResult
        {
            Model = axb ? RegressionModel.LinearAxB : RegressionModel.LinearABx,
            Parameters = pars,
            EquationTemplate = axb ? "y = a*x + b" : "y = a + b*x",
            Equation = fmt(10),
            Formatter = fmt,
            R = f.R,
            RSquared = r2,
            AdjustedRSquared = Adjusted(r2, f.N, 1),
            StandardError = f.S,
            SlopeStandardError = f.SeSlope,
            InterceptStandardError = f.SeIntercept,
            Residuals = f.Residuals,
            Fitted = fitted,
            Predict = xv => a + b * xv,
            N = f.N,
            DegreesOfFreedom = f.Df,
            ResidualSumOfSquares = f.Sse,
        };
    }

    // ------------------------------------------------------------------ polynomial

    private static string[] PolyNames(int degree)
    {
        var names = new string[degree + 1];
        for (int i = 0; i <= degree; i++) names[i] = degree < 26 ? ((char)('a' + i)).ToString() : $"c{degree - i}";
        return names;
    }

    private static RegressionResult PolynomialFit(int degree, IReadOnlyList<double> x, IReadOnlyList<double> y, IReadOnlyList<double>? w, RegressionModel model)
    {
        int n = x.Count, p = degree + 1;
        if (n < p) throw new ArgumentException($"at least {p} data points are required for a degree {degree} polynomial", nameof(x));
        double wsum = 0, wx = 0, xmin = double.PositiveInfinity, xmax = double.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            wsum += W(w, i);
            wx += W(w, i) * x[i];
            if (W(w, i) > 0) { xmin = Math.Min(xmin, x[i]); xmax = Math.Max(xmax, x[i]); }
        }
        double center = wx / wsum;
        double scale = Math.Max(Math.Abs(xmax - center), Math.Abs(xmin - center));
        if (!(scale > 0)) throw new ArgumentException("the x values must not all be equal");
        var design = new double[n, p];
        for (int i = 0; i < n; i++)
        {
            double t = (x[i] - center) / scale, tk = 1;
            for (int k = 0; k < p; k++) { design[i, k] = tk; tk *= t; }
        }
        var yy = y.ToArray();
        var ls = LinearAlgebra.LeastSquares(design, yy, w?.ToArray());
        if (!ls.FullRank) throw new ArgumentException($"at least {p} distinct x values are required for a degree {degree} polynomial");
        double[] c = ls.Coefficients;

        double Eval(double xv)
        {
            double t = (xv - center) / scale, s = 0;
            for (int k = p - 1; k >= 0; k--) s = s * t + c[k];
            return s;
        }

        var fitted = new double[n];
        var res = new double[n];
        var sse = new Num.Accumulator();
        double ybar = 0;
        for (int i = 0; i < n; i++) ybar += W(w, i) * y[i];
        ybar /= wsum;
        var sst = new Num.Accumulator();
        for (int i = 0; i < n; i++)
        {
            fitted[i] = Eval(x[i]);
            res[i] = y[i] - fitted[i];
            sse.Add(W(w, i) * res[i] * res[i]);
            sst.Add(W(w, i) * (y[i] - ybar) * (y[i] - ybar));
        }
        double df = wsum - p;
        double s2 = df > 0 ? sse.Value / df : double.NaN;

        // Expand to powers of x: a_j = sum_{k>=j} c_k scale^-k C(k, j) (-center)^(k-j), and transform the covariance.
        var T = new double[p, p];
        for (int k = 0; k < p; k++)
        {
            double sk = Math.Pow(scale, -k);
            for (int j = 0; j <= k; j++) T[j, k] = sk * SpecialFunctions.Binomial(k, j) * Math.Pow(-center, k - j);
        }
        var ascending = new double[p];
        for (int j = 0; j < p; j++)
        {
            double s = 0;
            for (int k = j; k < p; k++) s += T[j, k] * c[k];
            ascending[j] = s;
        }
        var covC = ls.UnscaledCovariance();
        var seAsc = new double[p];
        for (int j = 0; j < p; j++)
        {
            double v = 0;
            for (int k = 0; k < p; k++)
                for (int l = 0; l < p; l++) v += T[j, k] * covC[k, l] * T[j, l];
            seAsc[j] = Math.Sqrt(v * s2);
        }
        var names = PolyNames(degree);
        var pars = new RegressionParameter[p];
        for (int i = 0; i < p; i++) pars[i] = new RegressionParameter(names[i], ascending[degree - i], seAsc[degree - i]);

        var template = new StringBuilder("y = ");
        for (int i = 0; i <= degree; i++)
        {
            int power = degree - i;
            if (i > 0) template.Append(" + ");
            template.Append(names[i]);
            if (power == 1) template.Append("*x");
            else if (power > 1) template.Append("*x^").Append(power);
        }
        Func<int, string> fmt = d =>
        {
            var e = new Eq(d).Start();
            for (int i = 0; i <= degree; i++)
            {
                int power = degree - i;
                string suffix = power == 0 ? "" : power == 1 ? "*x" : $"*x^{power}";
                if (power == 0) e.Constant(ascending[0]);
                else e.Term(ascending[power], suffix);
            }
            return e.ToString();
        };
        double r2 = sst.Value > 0 ? 1 - sse.Value / sst.Value : double.NaN;
        return new RegressionResult
        {
            Model = model,
            Parameters = pars,
            EquationTemplate = template.ToString(),
            Equation = fmt(10),
            Formatter = fmt,
            R = null,
            RSquared = r2,
            AdjustedRSquared = Adjusted(r2, wsum, degree),
            StandardError = Math.Sqrt(s2),
            Residuals = res,
            Fitted = fitted,
            Predict = Eval,
            N = wsum,
            DegreesOfFreedom = df,
            ResidualSumOfSquares = sse.Value,
        };
    }

    // ------------------------------------------------------------------ transformed models

    private static RegressionResult Logarithmic(IReadOnlyList<double> x, IReadOnlyList<double> y, IReadOnlyList<double>? w)
    {
        var u = new double[x.Count];
        for (int i = 0; i < x.Count; i++)
        {
            if (!(x[i] > 0)) throw new ArgumentException("all x values must be positive for logarithmic regression", nameof(x));
            u[i] = Math.Log(x[i]);
        }
        var f = SimpleLinear(u, y, w);
        double a = f.Intercept, b = f.Slope;
        var fitted = new double[x.Count];
        for (int i = 0; i < x.Count; i++) fitted[i] = a + b * u[i];
        Func<int, string> fmt = d => new Eq(d).Start().Lead(a).Term(b, "*ln(x)").ToString();
        double r2 = f.R * f.R;
        return new RegressionResult
        {
            Model = RegressionModel.Logarithmic,
            Parameters = [new("a", a, f.SeIntercept), new("b", b, f.SeSlope)],
            EquationTemplate = "y = a + b*ln(x)",
            Equation = fmt(10),
            Formatter = fmt,
            R = f.R,
            RSquared = r2,
            AdjustedRSquared = Adjusted(r2, f.N, 1),
            StandardError = f.S,
            SlopeStandardError = f.SeSlope,
            InterceptStandardError = f.SeIntercept,
            Residuals = f.Residuals,
            Fitted = fitted,
            Predict = xv => a + b * Math.Log(xv),
            N = f.N,
            DegreesOfFreedom = f.Df,
            ResidualSumOfSquares = f.Sse,
        };
    }

    private static double[] LogOfY(IReadOnlyList<double> y, string model, out double sign)
    {
        bool allPos = true, allNeg = true;
        foreach (var v in y)
        {
            if (!(v > 0)) allPos = false;
            if (!(v < 0)) allNeg = false;
        }
        if (!allPos && !allNeg) throw new ArgumentException($"all y values must be positive (or all negative) for {model} regression", nameof(y));
        sign = allPos ? 1 : -1;
        var v2 = new double[y.Count];
        for (int i = 0; i < y.Count; i++) v2[i] = Math.Log(sign * y[i]);
        return v2;
    }

    private static RegressionResult ExponentialFit(IReadOnlyList<double> x, IReadOnlyList<double> y, IReadOnlyList<double>? w, bool baseForm)
    {
        var v = LogOfY(y, "exponential", out double sign);
        var f = SimpleLinear(x, v, w);
        double a = sign * Math.Exp(f.Intercept);
        double b = baseForm ? Math.Exp(f.Slope) : f.Slope;
        double k = f.Slope;
        var fitted = new double[x.Count];
        var res = new double[x.Count];
        for (int i = 0; i < x.Count; i++)
        {
            fitted[i] = a * Math.Exp(k * x[i]);
            res[i] = y[i] - fitted[i];
        }
        Func<int, string> fmt = baseForm
            ? d => $"y = {Eq.Num(a, d)}*{Eq.Wrap(b, d)}^x"
            : d => $"y = {Eq.Num(a, d)}*e^({Eq.Num(b, d)}*x)";
        double r2 = f.R * f.R;
        // Standard errors of a and b by the delta method on the linearised fit.
        double seA = Math.Abs(a) * f.SeIntercept;
        double seB = baseForm ? b * f.SeSlope : f.SeSlope;
        return new RegressionResult
        {
            Model = baseForm ? RegressionModel.Exponential : RegressionModel.ExponentialE,
            Parameters = [new("a", a, seA), new("b", b, seB)],
            EquationTemplate = baseForm ? "y = a*b^x" : "y = a*e^(b*x)",
            Equation = fmt(10),
            Formatter = fmt,
            R = f.R,
            RSquared = r2,
            AdjustedRSquared = Adjusted(r2, f.N, 1),
            StandardError = f.S,
            Residuals = res,
            Fitted = fitted,
            Predict = xv => a * Math.Exp(k * xv),
            N = f.N,
            DegreesOfFreedom = f.Df,
            ResidualSumOfSquares = f.Sse,
        };
    }

    private static RegressionResult PowerFit(IReadOnlyList<double> x, IReadOnlyList<double> y, IReadOnlyList<double>? w)
    {
        var u = new double[x.Count];
        for (int i = 0; i < x.Count; i++)
        {
            if (!(x[i] > 0)) throw new ArgumentException("all x values must be positive for power regression", nameof(x));
            u[i] = Math.Log(x[i]);
        }
        var v = LogOfY(y, "power", out double sign);
        var f = SimpleLinear(u, v, w);
        double a = sign * Math.Exp(f.Intercept), b = f.Slope;
        var fitted = new double[x.Count];
        var res = new double[x.Count];
        for (int i = 0; i < x.Count; i++)
        {
            fitted[i] = a * Math.Pow(x[i], b);
            res[i] = y[i] - fitted[i];
        }
        Func<int, string> fmt = d => $"y = {Eq.Num(a, d)}*x^{Eq.Wrap(b, d)}";
        double r2 = f.R * f.R;
        return new RegressionResult
        {
            Model = RegressionModel.Power,
            Parameters = [new("a", a, Math.Abs(a) * f.SeIntercept), new("b", b, f.SeSlope)],
            EquationTemplate = "y = a*x^b",
            Equation = fmt(10),
            Formatter = fmt,
            R = f.R,
            RSquared = r2,
            AdjustedRSquared = Adjusted(r2, f.N, 1),
            StandardError = f.S,
            Residuals = res,
            Fitted = fitted,
            Predict = xv => a * Math.Pow(xv, b),
            N = f.N,
            DegreesOfFreedom = f.Df,
            ResidualSumOfSquares = f.Sse,
        };
    }

    // ------------------------------------------------------------------ median-median

    private static RegressionResult MedianMedian(IReadOnlyList<double> x, IReadOnlyList<double> y, IReadOnlyList<double>? w)
    {
        var pts = new List<(double X, double Y)>();
        for (int i = 0; i < x.Count; i++)
        {
            double wi = W(w, i);
            if (!Num.IsInteger(wi)) throw new ArgumentException("median-median regression requires integer frequencies", nameof(w));
            for (int k = 0; k < (int)wi; k++) pts.Add((x[i], y[i]));
        }
        int n = pts.Count;
        if (n < 3) throw new ArgumentException("median-median regression requires at least 3 data points", nameof(x));
        pts.Sort((p, q) => p.X != q.X ? p.X.CompareTo(q.X) : p.Y.CompareTo(q.Y));
        int k3 = n / 3, rem = n % 3;
        int n1, n2;
        if (rem == 0) { n1 = k3; n2 = k3; }
        else if (rem == 1) { n1 = k3; n2 = k3 + 1; }
        else { n1 = k3 + 1; n2 = k3; }
        (double, double) Summary(int start, int count)
        {
            var xs = new double[count];
            var ys = new double[count];
            for (int i = 0; i < count; i++) { xs[i] = pts[start + i].X; ys[i] = pts[start + i].Y; }
            return (Descriptive.Median(xs), Descriptive.Median(ys));
        }
        var (x1, y1) = Summary(0, n1);
        var (x2, y2) = Summary(n1, n2);
        var (x3, y3) = Summary(n1 + n2, n - n1 - n2);
        if (x3 == x1) throw new ArgumentException("the outer groups have the same median x, so the median-median slope is undefined");
        double a = (y3 - y1) / (x3 - x1);
        double b = ((y1 - a * x1) + (y2 - a * x2) + (y3 - a * x3)) / 3.0;
        var fitted = new double[x.Count];
        var res = new double[x.Count];
        double wsum = 0, ybar = 0;
        for (int i = 0; i < x.Count; i++) { wsum += W(w, i); ybar += W(w, i) * y[i]; }
        ybar /= wsum;
        double sse = 0, sst = 0;
        for (int i = 0; i < x.Count; i++)
        {
            fitted[i] = a * x[i] + b;
            res[i] = y[i] - fitted[i];
            sse += W(w, i) * res[i] * res[i];
            sst += W(w, i) * (y[i] - ybar) * (y[i] - ybar);
        }
        Func<int, string> fmt = d => new Eq(d).Start().Term(a, "*x").Constant(b).ToString();
        double df = wsum - 2;
        return new RegressionResult
        {
            Model = RegressionModel.MedianMedian,
            Parameters = [new("a", a), new("b", b)],
            EquationTemplate = "y = a*x + b",
            Equation = fmt(10),
            Formatter = fmt,
            R = null,
            RSquared = sst > 0 ? 1 - sse / sst : double.NaN,
            AdjustedRSquared = double.NaN,
            StandardError = df > 0 ? Math.Sqrt(sse / df) : double.NaN,
            Residuals = res,
            Fitted = fitted,
            Predict = xv => a * xv + b,
            N = wsum,
            DegreesOfFreedom = df,
            ResidualSumOfSquares = sse,
        };
    }

    // ------------------------------------------------------------------ logistic

    /// <summary>Logistic regression y = c / (1 + a e^(-b x)) by nonlinear least squares with automatic starting values.</summary>
    public static RegressionResult FitLogistic(IReadOnlyList<double> x, IReadOnlyList<double> y, IReadOnlyList<double>? weights = null, int maxIterations = 500)
    {
        Check(x, y, weights);
        if (x.Count < 3) throw new ArgumentException("logistic regression requires at least 3 data points", nameof(x));
        static double Model(double[] p, double xv) => p[2] / (1 + p[0] * Math.Exp(-p[1] * xv));
        static double[] Grad(double[] p, double xv)
        {
            double e = Math.Exp(-p[1] * xv);
            double den = 1 + p[0] * e;
            double f = p[2] / den;
            return [-f * e / den, f * p[0] * xv * e / den, 1 / den];
        }
        double ymax = y.Max(), ymin = y.Min();
        double xmin = x.Min(), xmax = x.Max();
        double span = Math.Max(xmax - xmin, 1e-300);
        var starts = new List<double[]>();
        if (ymin > 0)
        {
            foreach (double factor in new[] { 1.001, 1.01, 1.05, 1.2, 1.5, 2.0, 4.0 })
            {
                double c0 = ymax * factor;
                var z = new double[x.Count];
                for (int i = 0; i < x.Count; i++) z[i] = Math.Log(c0 / y[i] - 1);
                try
                {
                    var f = SimpleLinear(x, z, weights);
                    double a0 = Math.Exp(f.Intercept), b0 = -f.Slope;
                    if (double.IsFinite(a0) && double.IsFinite(b0)) starts.Add([a0, b0, c0]);
                }
                catch (ArgumentException)
                {
                }
            }
        }
        double mid = 0.5 * (xmin + xmax);
        foreach (double sgn in new[] { 1.0, -1.0 })
        {
            double b0 = sgn * 8 / span;
            starts.Add([Math.Exp(b0 * mid), b0, ymax > 0 ? ymax * 1.05 : (ymax - ymin + 1)]);
        }
        NonlinearFitResult? best = null;
        foreach (var s0 in starts)
        {
            try
            {
                var fit = NonlinearFit.Core(Model, x, y, s0, weights, maxIterations, 1e-12, Grad);
                if (double.IsFinite(fit.ResidualSumOfSquares) && (best is null || fit.ResidualSumOfSquares < best.ResidualSumOfSquares)) best = fit;
            }
            catch (ArgumentException)
            {
            }
        }
        if (best is null) throw new ArgumentException("the logistic model could not be fitted to these data");
        double a = best.Parameters[0], b = best.Parameters[1], c = best.Parameters[2];
        Func<int, string> fmt = d => $"y = {Eq.Num(c, d)}/(1 + {Eq.Num(a, d)}*e^({Eq.Num(-b, d)}*x))";
        return NonlinearResult(RegressionModel.Logistic, best, ["a", "b", "c"], "y = c/(1 + a*e^(-b*x))", fmt, x, weights);
    }

    private static RegressionResult NonlinearResult(RegressionModel model, NonlinearFitResult fit, string[] names, string template,
        Func<int, string> fmt, IReadOnlyList<double> x, IReadOnlyList<double>? weights)
    {
        var pars = new RegressionParameter[names.Length];
        for (int i = 0; i < names.Length; i++) pars[i] = new RegressionParameter(names[i], fit.Parameters[i], fit.StandardErrors[i]);
        var fitted = new double[x.Count];
        for (int i = 0; i < x.Count; i++) fitted[i] = fit.Predict(x[i]);
        double n = weights is null ? x.Count : Num.Sum(weights);
        return new RegressionResult
        {
            Model = model,
            Parameters = pars,
            EquationTemplate = template,
            Equation = fmt(10),
            Formatter = fmt,
            R = null,
            RSquared = fit.RSquared,
            AdjustedRSquared = Adjusted(fit.RSquared, n, names.Length - 1),
            StandardError = fit.StandardError,
            Residuals = fit.Residuals,
            Fitted = fitted,
            Predict = fit.Predict,
            N = n,
            DegreesOfFreedom = fit.DegreesOfFreedom,
            ResidualSumOfSquares = fit.ResidualSumOfSquares,
            Converged = fit.Converged,
            Iterations = fit.Iterations,
        };
    }

    // ------------------------------------------------------------------ sinusoidal

    /// <summary>
    /// Sinusoidal regression y = a sin(b x + c) + d (TI SinReg). Without a period guess the angular frequency is found
    /// automatically by scanning a least squares periodogram (works for unevenly spaced x). The result is normalised
    /// to a &gt; 0, b &gt; 0 and -pi &lt; c &lt;= pi.
    /// </summary>
    public static RegressionResult FitSinusoidal(IReadOnlyList<double> x, IReadOnlyList<double> y, double? period = null, IReadOnlyList<double>? weights = null, int maxIterations = 500)
    {
        Check(x, y, weights);
        if (x.Count < 4) throw new ArgumentException("sinusoidal regression requires at least 4 data points", nameof(x));
        if (period is not null && !(period > 0 && double.IsFinite(period.Value))) throw new ArgumentException("the period must be positive", nameof(period));
        int n = x.Count;
        var sorted = x.Distinct().OrderBy(v => v).ToArray();
        if (sorted.Length < 2) throw new ArgumentException("the x values must not all be equal");
        double span = sorted[^1] - sorted[0];

        // Linear least squares for fixed omega: y ~ A sin(wx) + B cos(wx) + D.
        (double Sse, double A, double B, double D) Fixed(double omega)
        {
            var m = new double[3, 3];
            var rhs = new double[3];
            for (int i = 0; i < n; i++)
            {
                double wi = W(weights, i);
                double s = Math.Sin(omega * x[i]), c = Math.Cos(omega * x[i]);
                double[] row = [s, c, 1];
                for (int a = 0; a < 3; a++)
                {
                    rhs[a] += wi * row[a] * y[i];
                    for (int b = 0; b < 3; b++) m[a, b] += wi * row[a] * row[b];
                }
            }
            if (!LinearAlgebra.TryCholeskySolve(m, rhs, out var sol)) return (double.PositiveInfinity, 0, 0, 0);
            double sse = 0;
            for (int i = 0; i < n; i++)
            {
                double r = y[i] - (sol[0] * Math.Sin(omega * x[i]) + sol[1] * Math.Cos(omega * x[i]) + sol[2]);
                sse += W(weights, i) * r * r;
            }
            return (sse, sol[0], sol[1], sol[2]);
        }

        var candidates = new List<double>();
        if (period is not null)
        {
            candidates.Add(2 * Math.PI / period.Value);
        }
        else
        {
            var gaps = new double[sorted.Length - 1];
            for (int i = 0; i < gaps.Length; i++) gaps[i] = sorted[i + 1] - sorted[i];
            Array.Sort(gaps);
            double typical = gaps[gaps.Length / 2];
            double wmin = Math.PI / span, wmax = Math.PI / typical;
            if (wmax <= wmin * 1.5) wmax = 4 * wmin;
            int count = (int)Math.Clamp(Math.Ceiling((wmax - wmin) / (wmin / 8)), 64, 4000);
            var grid = new double[count + 1];
            var sse = new double[count + 1];
            for (int k = 0; k <= count; k++)
            {
                grid[k] = wmin + (wmax - wmin) * k / count;
                sse[k] = Fixed(grid[k]).Sse;
            }
            var minima = new List<int>();
            for (int k = 0; k <= count; k++)
            {
                bool left = k == 0 || sse[k] <= sse[k - 1];
                bool right = k == count || sse[k] <= sse[k + 1];
                if (left && right) minima.Add(k);
            }
            foreach (int k in minima.OrderBy(k => sse[k]).Take(6))
            {
                double lo = grid[Math.Max(0, k - 1)], hi = grid[Math.Min(count, k + 1)];
                const double gr = 0.6180339887498949;
                double c1 = hi - gr * (hi - lo), c2 = lo + gr * (hi - lo);
                double f1 = Fixed(c1).Sse, f2 = Fixed(c2).Sse;
                for (int it = 0; it < 60; it++)
                {
                    if (f1 < f2) { hi = c2; c2 = c1; f2 = f1; c1 = hi - gr * (hi - lo); f1 = Fixed(c1).Sse; }
                    else { lo = c1; c1 = c2; f1 = f2; c2 = lo + gr * (hi - lo); f2 = Fixed(c2).Sse; }
                }
                candidates.Add(0.5 * (lo + hi));
            }
        }

        static double Model(double[] p, double xv) => p[0] * Math.Sin(p[1] * xv + p[2]) + p[3];
        static double[] Grad(double[] p, double xv)
        {
            double arg = p[1] * xv + p[2];
            double s = Math.Sin(arg), c = Math.Cos(arg);
            return [s, p[0] * xv * c, p[0] * c, 1];
        }

        NonlinearFitResult? best = null;
        foreach (double omega in candidates)
        {
            var (_, A, B, D) = Fixed(omega);
            double amp = Math.Sqrt(A * A + B * B);
            if (!(amp > 0)) amp = 1e-3 * (y.Max() - y.Min() + 1);
            double[] start = [amp, omega, Math.Atan2(B, A), D];
            try
            {
                var fit = NonlinearFit.Core(Model, x, y, start, weights, maxIterations, 1e-12, Grad);
                if (double.IsFinite(fit.ResidualSumOfSquares) && (best is null || fit.ResidualSumOfSquares < best.ResidualSumOfSquares)) best = fit;
            }
            catch (ArgumentException)
            {
            }
        }
        if (best is null) throw new ArgumentException("the sinusoidal model could not be fitted to these data");

        double a = best.Parameters[0], b = best.Parameters[1], c = best.Parameters[2], d = best.Parameters[3];
        if (b < 0) { b = -b; c = Math.PI - c; }
        if (a < 0) { a = -a; c += Math.PI; }
        c = Math.IEEERemainder(c, 2 * Math.PI);
        if (c <= -Math.PI) c += 2 * Math.PI;
        double fa = a, fb = b, fc = c, fd = d;
        var norm = best with
        {
            Parameters = [fa, fb, fc, fd],
            Predict = xv => fa * Math.Sin(fb * xv + fc) + fd,
        };
        Func<int, string> fmt = dg =>
        {
            var inner = new Eq(dg).Bare().Term(fb, "*x").Constant(fc).ToString();
            return new Eq(dg).Start().Term(fa, $"*sin({inner})").Constant(fd).ToString();
        };
        return NonlinearResult(RegressionModel.Sinusoidal, norm, ["a", "b", "c", "d"], "y = a*sin(b*x + c) + d", fmt, x, weights);
    }

    // ------------------------------------------------------------------ multiple linear regression

    /// <summary>
    /// Multiple linear regression y = b0 + b1 x1 + ... + bk xk. <paramref name="predictors"/> holds one array per
    /// predictor variable (columns, like calculator lists), each with one value per observation.
    /// </summary>
    public static MultipleRegressionResult MultipleLinear(IReadOnlyList<double[]> predictors, IReadOnlyList<double> y, IReadOnlyList<double>? weights = null)
    {
        Guard.NotNull(predictors, nameof(predictors));
        int k = predictors.Count;
        if (k == 0) throw new ArgumentException("at least one predictor is required", nameof(predictors));
        Guard.AllFinite(y, nameof(y));
        int n = y.Count;
        foreach (var col in predictors)
        {
            Guard.AllFinite(col, nameof(predictors));
            Guard.SameLength(col.Length, n, "every predictor and y");
        }
        if (weights is not null) Descriptive.CheckFrequencies(weights, n);
        double wsum = 0;
        for (int i = 0; i < n; i++) wsum += W(weights, i);
        if (wsum - k - 1 <= 0 || n < k + 1) throw new ArgumentException($"more than {k + 1} observations are required for {k} predictors", nameof(y));
        var means = new double[k];
        for (int j = 0; j < k; j++)
        {
            double s = 0;
            for (int i = 0; i < n; i++) s += W(weights, i) * predictors[j][i];
            means[j] = s / wsum;
        }
        var design = new double[n, k + 1];
        for (int i = 0; i < n; i++)
        {
            design[i, 0] = 1;
            for (int j = 0; j < k; j++) design[i, j + 1] = predictors[j][i] - means[j];
        }
        var ls = LinearAlgebra.LeastSquares(design, y.ToArray(), weights?.ToArray());
        if (!ls.FullRank) throw new ArgumentException("the predictors are collinear (or constant), so the coefficients are not determined");
        var cc = ls.Coefficients;
        var coef = new double[k + 1];
        coef[0] = cc[0];
        for (int j = 0; j < k; j++)
        {
            coef[j + 1] = cc[j + 1];
            coef[0] -= cc[j + 1] * means[j];
        }
        var fitted = new double[n];
        var res = new double[n];
        double ybar = 0;
        for (int i = 0; i < n; i++) ybar += W(weights, i) * y[i];
        ybar /= wsum;
        var sse = new Num.Accumulator();
        var sst = new Num.Accumulator();
        for (int i = 0; i < n; i++)
        {
            double f = coef[0];
            for (int j = 0; j < k; j++) f += coef[j + 1] * predictors[j][i];
            fitted[i] = f;
            res[i] = y[i] - f;
            sse.Add(W(weights, i) * res[i] * res[i]);
            sst.Add(W(weights, i) * (y[i] - ybar) * (y[i] - ybar));
        }
        double dfRes = wsum - k - 1;
        double s2 = sse.Value / dfRes;
        var uc = ls.UnscaledCovariance();
        // Transform the covariance from the centred parameterisation: b0 = c0 - sum c_j m_j.
        var T = new double[k + 1, k + 1];
        T[0, 0] = 1;
        for (int j = 0; j < k; j++) { T[0, j + 1] = -means[j]; T[j + 1, j + 1] = 1; }
        var cov = new double[k + 1, k + 1];
        for (int a = 0; a <= k; a++)
            for (int b = 0; b <= k; b++)
            {
                double v = 0;
                for (int p = 0; p <= k; p++)
                    for (int q = 0; q <= k; q++) v += T[a, p] * uc[p, q] * T[b, q];
                cov[a, b] = v * s2;
            }
        var se = new double[k + 1];
        var tstat = new double[k + 1];
        var pval = new double[k + 1];
        var tdist = new StudentTDistribution(dfRes);
        for (int j = 0; j <= k; j++)
        {
            se[j] = Math.Sqrt(cov[j, j]);
            tstat[j] = coef[j] / se[j];
            pval[j] = 2 * tdist.Survival(Math.Abs(tstat[j]));
        }
        double ssr = Math.Max(0, sst.Value - sse.Value);
        double r2 = sst.Value > 0 ? 1 - sse.Value / sst.Value : double.NaN;
        double F = (ssr / k) / s2;
        var final = coef;
        return new MultipleRegressionResult
        {
            Coefficients = coef,
            StandardErrors = se,
            TStatistics = tstat,
            PValues = pval,
            RSquared = r2,
            AdjustedRSquared = Adjusted(r2, wsum, k),
            F = F,
            FPValue = new FDistribution(k, dfRes).Survival(F),
            StandardError = Math.Sqrt(s2),
            RegressionDegreesOfFreedom = k,
            ResidualDegreesOfFreedom = dfRes,
            RegressionSumOfSquares = ssr,
            ResidualSumOfSquares = sse.Value,
            TotalSumOfSquares = sst.Value,
            Residuals = res,
            Fitted = fitted,
            Covariance = cov,
            Predict = xs =>
            {
                if (xs is null || xs.Length != k) throw new ArgumentException($"exactly {k} predictor values are required");
                double f = final[0];
                for (int j = 0; j < k; j++) f += final[j + 1] * xs[j];
                return f;
            },
        };
    }

    // ------------------------------------------------------------------ equation text

    /// <summary>Builds equation strings with sign aware joining ("y = 2*x - 3").</summary>
    private sealed class Eq
    {
        private readonly StringBuilder _sb = new();
        private readonly int _digits;
        private bool _first = true;

        public Eq(int digits) => _digits = Math.Clamp(digits, 1, 17);

        public static string Num(double v, int digits)
        {
            string s = v.ToString("G" + Math.Clamp(digits, 1, 17), CultureInfo.InvariantCulture);
            int e = s.IndexOf('E');
            if (e >= 0)
            {
                string mant = s[..e];
                string exp = s[(e + 1)..];
                bool neg = exp.StartsWith('-');
                exp = exp.TrimStart('+', '-').TrimStart('0');
                if (exp.Length == 0) exp = "0";
                s = mant + "E" + (neg ? "-" : "") + exp;
            }
            return s;
        }

        /// <summary>Wraps negative or exponent numbers in parentheses (for use as a base or exponent).</summary>
        public static string Wrap(double v, int digits)
        {
            string s = Num(v, digits);
            return v < 0 || s.Contains('E') ? "(" + s + ")" : s;
        }

        public Eq Start()
        {
            _sb.Append("y = ");
            return this;
        }

        public Eq Bare() => this;

        public Eq Lead(double v)
        {
            _sb.Append(Num(v, _digits));
            _first = false;
            return this;
        }

        public Eq Term(double coef, string suffix)
        {
            if (_first)
            {
                _sb.Append(Num(coef, _digits)).Append(suffix);
                _first = false;
            }
            else
            {
                _sb.Append(coef < 0 || (coef == 0 && double.IsNegative(coef)) ? " - " : " + ").Append(Num(Math.Abs(coef), _digits)).Append(suffix);
            }
            return this;
        }

        public Eq Constant(double v) => Term(v, "");

        public override string ToString() => _sb.ToString();
    }
}

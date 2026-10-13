using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Modes.Solver.Logic;

/// <summary>A local maximum or minimum of a polynomial.</summary>
/// <param name="X">Where it is.</param>
/// <param name="Y">The value there.</param>
/// <param name="IsMaximum">True for a local maximum, false for a local minimum.</param>
public sealed record TurningPoint(SolutionValue X, SolutionValue Y, bool IsMaximum);

/// <summary>The result of the POLYNOMIAL tool.</summary>
public sealed class PolynomialOutcome : SolveOutcome
{
    /// <summary>The inputs that were solved.</summary>
    public required PolynomialRequest Request { get; init; }

    /// <summary>The real degree (lower than asked when leading coefficients are 0; −1 for the zero polynomial).</summary>
    public int Degree { get; set; }

    /// <summary>The polynomial p(x) (null on errors).</summary>
    public Expr? Polynomial { get; set; }

    /// <summary>p(x) = 0 as on-screen text.</summary>
    public string EquationText { get; set; } = "";

    /// <summary>The coefficients as numbers, constant term first (for drawing the graph).</summary>
    public double[] Ascending { get; set; } = [];

    /// <summary>The distinct roots: real ones left to right, then complex pairs.</summary>
    public List<SolutionValue> Roots { get; } = [];

    /// <summary>True when every x solves it (all coefficients 0).</summary>
    public bool AllValues { get; set; }

    /// <summary>The discriminant b² − 4ac (quadratics only).</summary>
    public Expr? Discriminant { get; set; }

    /// <summary>The vertex of a parabola (quadratics only).</summary>
    public TurningPoint? Vertex { get; set; }

    /// <summary>Local maxima and minima (cubics and up).</summary>
    public List<TurningPoint> TurningPoints { get; } = [];

    /// <summary>The number of distinct real roots.</summary>
    public int RealCount => Roots.Count(r => r.IsReal);

    /// <summary>The number of distinct complex (non-real) roots.</summary>
    public int ComplexCount => Roots.Count(r => !r.IsReal);
}

public sealed partial class SolverEngine
{
    /// <summary>
    /// Solves a polynomial equation from its coefficients: every root with its multiplicity, exact where the engine
    /// finds an exact form (quadratics always), complex roots as a + bi, plus the discriminant and vertex of a
    /// quadratic or the turning points of a higher degree polynomial.
    /// </summary>
    public PolynomialOutcome SolvePolynomial(PolynomialRequest request, CancellationToken ct)
    {
        var r = request.Clone().Normalize();
        var snapshot = new SolverSnapshot { Polynomial = r.Clone() };
        var o = new PolynomialOutcome { Request = r };
        try
        {
            var coeffs = new Expr[r.Degree + 1];
            for (int k = 0; k <= r.Degree; k++) coeffs[k] = Coefficient(r[k], CoefficientName(k), ct);
            int d = r.Degree;
            while (d >= 0 && IsZeroValue(coeffs[d], ct)) d--;
            o.Degree = d;
            var xs = new SymbolExpr("x");
            var terms = new List<Expr>();
            for (int k = 0; k <= Math.Max(d, 0); k++)
                terms.Add(k == 0 ? coeffs[0] : Algebra.Mul(coeffs[k], k == 1 ? xs : Algebra.Pow(xs, Expr.Int(k))));
            var p = Evaluate(Algebra.Add(terms), ct, free: ["x"]);
            o.Polynomial = p;
            o.EquationText = Show(p) + " = 0";
            o.Ascending = [.. coeffs.Take(Math.Max(d, 0) + 1).Select(c => Real(c, ct))];
            if (d < r.Degree && d >= 0)
                o.Notes.Add($"The {CoefficientName(r.Degree)} is 0, so this is a polynomial of degree {d}.");

            if (d < 0)
            {
                o.AllValues = true;
                o.Summary = "EVERY X IS A SOLUTION";
                o.Notes.Add("All coefficients are 0, so the equation reads 0 = 0.");
            }
            else if (d == 0)
            {
                o.Summary = "NO SOLUTION";
                o.Notes.Add($"Only the constant term is left: {Show(coeffs[0])} = 0 is never true.");
            }
            else
            {
                FindRoots(o, p, coeffs, d, ct);
                if (d == 2) Quadratic(o, coeffs, p, ct);
                else TurningPoints(o, p, ct);
                o.Summary = o.ComplexCount == 0 ? SolveOutcome.Count(o.RealCount, "REAL ROOT")
                    : o.RealCount == 0 ? $"NO REAL ROOTS, {o.ComplexCount} COMPLEX"
                    : $"{SolveOutcome.Count(o.RealCount, "REAL ROOT")}, {o.ComplexCount} COMPLEX";
                if (o.Roots.Any(v => v.IsNumeric))
                    o.Notes.Add("Some roots have no simple exact form, so they are given as decimals.");
                foreach (var root in o.Roots.Where(v => v.Multiplicity > 1))
                    o.Notes.Add($"x = {root.Text} is a {MultiplicityWord(root.Multiplicity)} root: the graph {(root.Multiplicity % 2 == 0 ? "touches the axis there without crossing" : "flattens as it crosses the axis there")}.");
            }
            o.PlotTexts = d >= 1 ? ["y = " + MathText.Input(p)] : [];
            o.Card = PolynomialCard(o, snapshot);
            return o;
        }
        catch (SolverException ex)
        {
            return new PolynomialOutcome { Request = r, Error = ex, Card = SolveOutcome.ErrorCard(SolverTool.Polynomial, PolynomialPreview(r), ex, snapshot) };
        }
    }

    /// <summary>The name of a coefficient box: "coefficient of x²", "constant term".</summary>
    public static string CoefficientName(int power) => power switch
    {
        0 => "constant term",
        1 => "coefficient of x",
        _ => "coefficient of x" + Superscript(power),
    };

    /// <summary>x², x³ ... as superscript text.</summary>
    public static string Superscript(int power)
    {
        const string digits = "⁰¹²³⁴⁵⁶⁷⁸⁹";
        return string.Concat(power.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(c => digits[c - '0']));
    }

    private static string MultiplicityWord(int m) => m switch { 2 => "double", 3 => "triple", 4 => "quadruple", _ => $"{m}-fold" };

    /// <summary>The polynomial as typed (for error cards, where it could not be built).</summary>
    private static string PolynomialPreview(PolynomialRequest r)
    {
        var parts = new List<string>();
        for (int k = r.Degree; k >= 0; k--)
        {
            var c = string.IsNullOrWhiteSpace(r[k]) ? "0" : r[k].Trim();
            parts.Add(k switch { 0 => $"({c})", 1 => $"({c})x", _ => $"({c})x{Superscript(k)}" });
        }
        return MathText.Typographic(string.Join(" + ", parts) + " = 0");
    }

    private Expr Coefficient(string text, string field, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return Expr.Zero;
        var e = Evaluate(Parse(text, field), ct);
        var free = e.FreeSymbols();
        if (free.Count > 0) throw new SolverException($"The {field} must be a number, but it contains {string.Join(", ", free)}", field);
        var n = Numeric(e, ct) ?? throw new SolverException($"The {field} is not a number", field);
        if (Math.Abs(n.Im) > 1e-12 * Math.Max(1, Math.Abs(n.Re))) throw new SolverException($"The {field} must be a real number", field);
        return e;
    }

    private bool IsZeroValue(Expr e, CancellationToken ct)
    {
        if (e.IsZero) return true;
        if (e is NumberExpr { Value.IsExact: true }) return false;
        var n = Numeric(e, ct);
        return n is { } v && v.AbsDouble() == 0;
    }

    private void FindRoots(PolynomialOutcome o, Expr p, Expr[] coeffs, int d, CancellationToken ct)
    {
        var known = new HashSet<string> { "x" };
        IReadOnlyList<Expr> values = [];
        try
        {
            var read = SolutionReader.ReadSingle(Evaluate(CallExpr.Of("solve", new RelationExpr(RelOp.Equal, p, Expr.Zero), new SymbolExpr("x")), ct, complex: true), "x");
            if (read.Kind == SolutionKind.Values) values = read.Values;
        }
        catch (SolverException)
        {
            // Fall back to the numeric root finder below.
        }
        if (values.Count == 0)
        {
            var list = new ListExpr([.. coeffs.Take(d + 1).Reverse()]);
            if (Evaluate(CallExpr.Of("polyroots", list), ct) is ListExpr roots) values = roots.Items;
        }
        var described = values.Select(v => Describe(v, known, ct)).ToList();
        foreach (var v in described)
            v.Multiplicity = double.IsFinite(v.Re) ? PolynomialMath.Multiplicity(o.Ascending, v.Re, double.IsFinite(v.Im) ? v.Im : 0) : 1;
        o.Roots.AddRange(described.Where(v => v.IsReal).OrderBy(v => v.Re));
        o.Roots.AddRange(described.Where(v => !v.IsReal).OrderBy(v => v.Re).ThenBy(v => v.Im));
        if (o.Roots.Count == 0) throw new SolverException("The roots could not be found");
    }

    private void Quadratic(PolynomialOutcome o, Expr[] coeffs, Expr p, CancellationToken ct)
    {
        Expr a = coeffs[2], b = coeffs[1], c = coeffs[0];
        var disc = Evaluate(Algebra.Sub(Algebra.Mul(b, b), Algebra.Mul(Expr.Int(4), a, c)), ct);
        o.Discriminant = disc;
        double dv = Real(disc, ct);
        string start = $"Δ = b² {MathText.Minus} 4ac = {Show(disc)}";
        o.Notes.Insert(0, dv > 0 ? $"{start} > 0: two real roots."
            : dv < 0 ? $"{start} < 0: no real roots, two complex conjugate roots."
            : $"{start}: one double root.");
        var h = Evaluate(Algebra.Neg(Algebra.Div(b, Algebra.Mul(Expr.Two, a))), ct);
        var k = ValueOf(p, h, ct);
        bool max = Real(a, ct) < 0;
        o.Vertex = new TurningPoint(Describe(h, ["x"], ct), Describe(k, ["x"], ct), max);
    }

    private void TurningPoints(PolynomialOutcome o, Expr p, CancellationToken ct)
    {
        try
        {
            var dp = Evaluate(CallExpr.Of("diff", p, new SymbolExpr("x")), ct, free: ["x"]);
            var read = SolutionReader.ReadSingle(Evaluate(CallExpr.Of("solve", new RelationExpr(RelOp.Equal, dp, Expr.Zero), new SymbolExpr("x")), ct), "x");
            if (read.Kind != SolutionKind.Values) return;
            var slope = PolynomialMath.Derivative(o.Ascending);
            foreach (var c in read.Values)
            {
                var cx = Describe(c, ["x"], ct);
                if (!cx.IsReal || !double.IsFinite(cx.Re)) continue;
                double step = 1e-4 * Math.Max(1, Math.Abs(cx.Re));
                double left = PolynomialMath.Evaluate(slope, cx.Re - step), right = PolynomialMath.Evaluate(slope, cx.Re + step);
                if (Math.Sign(left) == Math.Sign(right) || left == 0 || right == 0) continue;
                o.TurningPoints.Add(new TurningPoint(cx, Describe(ValueOf(p, c, ct), ["x"], ct), left > 0));
            }
            o.TurningPoints.Sort((a, b) => a.X.Re.CompareTo(b.X.Re));
        }
        catch (SolverException)
        {
            // Turning points are extra information; leave them out when they cannot be found.
        }
    }

    /// <summary>
    /// p(at), multiplied out so radicals combine: p((6 − √3)/3) for x³ − 6x² + 11x − 6 is 2√3/9. When the exact
    /// form stays long it is replaced by its decimal value.
    /// </summary>
    private Expr ValueOf(Expr p, Expr at, CancellationToken ct)
    {
        var substituted = p.Map(e => e is SymbolExpr { Name: "x" } ? at : e);
        var value = Evaluate(CallExpr.Of("expand", substituted), ct);
        if (value.NodeCount() <= 24) return value;
        var n = Numeric(value, ct);
        return n is { } v ? new NumberExpr(Number.Approx(v.Re)) : value;
    }

    private SolverCard PolynomialCard(PolynomialOutcome o, SolverSnapshot snapshot)
    {
        var card = new SolverCard
        {
            Tool = SolverTool.Polynomial,
            Title = "POLYNOMIAL",
            Input = o.EquationText,
            Request = snapshot,
            PlotTexts = o.PlotTexts,
        };
        if (o.AllValues || o.Degree <= 0)
        {
            card.Lines = [o.AllValues ? "every x is a solution" : "no solution"];
            card.CopyText = card.Lines[0];
            card.LatexText = o.AllValues ? @"x \in \mathbb{R}" : @"\text{no solution}";
            return card;
        }
        card.Lines = ValueLines("x", o.Roots, numbered: true);
        card.Approx = ApproxLine(o.Roots);
        card.CopyText = string.Join(Environment.NewLine, card.Lines);
        card.LatexText = string.Join(@",\quad ", o.Roots.Select((v, i) => $"x_{{{i + 1}}} = {v.Latex}"));
        card.UseText = "{" + string.Join(", ", o.Roots.Select(v => v.Input)) + "}";
        var notes = new List<string>();
        if (o.Discriminant is { } disc) notes.Add($"Δ = {Show(disc)}");
        if (o.Vertex is { } vx) notes.Add($"vertex ({vx.X.Text}, {vx.Y.Text}), {(vx.IsMaximum ? "maximum" : "minimum")}");
        if (o.RealCount == 0) notes.Add("no real roots");
        card.Note = notes.Count == 0 ? null : string.Join(";  ", notes);
        return card;
    }
}

/// <summary>Plain double-precision polynomial helpers (coefficients constant term first).</summary>
public static class PolynomialMath
{
    /// <summary>p(x) by Horner's rule.</summary>
    public static double Evaluate(IReadOnlyList<double> ascending, double x)
    {
        double acc = 0;
        for (int i = ascending.Count - 1; i >= 0; i--) acc = acc * x + ascending[i];
        return acc;
    }

    /// <summary>The coefficients of p'(x).</summary>
    public static double[] Derivative(IReadOnlyList<double> ascending)
    {
        if (ascending.Count <= 1) return [0];
        var d = new double[ascending.Count - 1];
        for (int i = 1; i < ascending.Count; i++) d[i - 1] = i * ascending[i];
        return d;
    }

    /// <summary>
    /// How many times z = re + i·im is a root: the number of derivatives (starting with p itself) that vanish there,
    /// relative to the size of the terms.
    /// </summary>
    public static int Multiplicity(IReadOnlyList<double> ascending, double re, double im)
    {
        var c = ascending.ToArray();
        int m = 0;
        while (c.Length > 1)
        {
            double vr = 0, vi = 0, scale = 0, mod = Math.Sqrt(re * re + im * im), power = 1;
            for (int i = c.Length - 1; i >= 0; i--)
            {
                (vr, vi) = (vr * re - vi * im + c[i], vr * im + vi * re);
            }
            for (int i = 0; i < c.Length; i++)
            {
                scale += Math.Abs(c[i]) * power;
                power *= mod;
            }
            if (Math.Sqrt(vr * vr + vi * vi) > 1e-7 * Math.Max(scale, double.Epsilon)) break;
            m++;
            c = Derivative(c);
        }
        return Math.Max(1, m);
    }
}

using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Modes.Solver.Logic;

/// <summary>
/// The Solver's link to the engine: builds solve, nsolve and helper calls from the inputs, evaluates them in the
/// shared session (optionally with complex results switched on just for this solve) and reads the results into
/// solution lists. Every method may run off the UI thread and honours the cancellation token.
/// </summary>
public sealed partial class SolverEngine
{
    /// <summary>Creates the engine link for a calculator session (its variables and settings are shared with other modes).</summary>
    public SolverEngine(CalcSession session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <summary>The shared calculator session.</summary>
    public CalcSession Session { get; }

    /// <summary>The calculator settings (angle unit, digits, complex results).</summary>
    public CalcSettings Settings => Session.Settings;

    // ------------------------------------------------------------------ evaluation

    /// <summary>An evaluation context with the session's settings; complex results can be forced on for one solve.</summary>
    internal EvalContext Context(CancellationToken ct, bool complex = false, bool approximate = false)
    {
        var settings = Settings.Clone();
        if (complex) settings.Complex = ComplexMode.Complex;
        return new EvalContext(settings, Session.Env, Session.Functions, approximate, ct);
    }

    /// <summary>Parses one input. Errors name the field and point at the position.</summary>
    public Expr Parse(string? text, string field)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new SolverException($"The {field} is empty", field);
        try
        {
            return Session.ParseExpression(MathText.Ascii(text));
        }
        catch (CalcException ex)
        {
            throw new SolverException($"In the {field}: {ex.Message}", field, ex.Position, ex.Hint);
        }
    }

    /// <summary>Evaluates an expression exactly. Engine errors become <see cref="SolverException"/>; cancellation is passed on.</summary>
    public Expr Evaluate(Expr e, CancellationToken ct, bool complex = false, IEnumerable<string>? free = null)
    {
        var ctx = Context(ct, complex);
        if (free is not null) ctx = ctx.WithFree(free);
        return Guard(() => Evaluator.Eval(e, ctx));
    }

    /// <summary>Runs engine work and turns its errors into <see cref="SolverException"/>.</summary>
    internal static T Guard<T>(Func<T> work)
    {
        try
        {
            return work();
        }
        catch (CalcException ex)
        {
            throw new SolverException(ex.Message, hint: ex.Hint);
        }
        catch (DivideByZeroException)
        {
            throw new SolverException("Division by zero");
        }
        catch (OverflowException)
        {
            throw new SolverException("A number became too large");
        }
        catch (InsufficientExecutionStackException)
        {
            throw new SolverException("The expression is nested too deeply");
        }
        catch (ArithmeticException ex)
        {
            throw new SolverException(ex.Message);
        }
    }

    /// <summary>The decimal value of a constant expression (complex allowed), or null when it has none.</summary>
    public Number? Numeric(Expr e, CancellationToken ct)
    {
        if (e.FreeSymbols().Count > 0) return null;
        try
        {
            var n = Evaluator.Numeric(e, Context(ct, complex: true, approximate: true));
            return n is { IsFinite: true, IsNaN: false } ? n : null;
        }
        catch (Exception ex) when (ex is CalcException or ArithmeticException)
        {
            return null;
        }
    }

    /// <summary>The real decimal value of a constant expression, or NaN.</summary>
    public double Real(Expr e, CancellationToken ct)
    {
        var n = Numeric(e, ct);
        if (n is not { } v) return double.NaN;
        return Math.Abs(v.Im) <= 1e-12 * Math.Max(1, Math.Abs(v.Re)) ? v.Re : double.NaN;
    }

    /// <summary>The decimal form of an exact value, or null when it would look the same.</summary>
    public Expr? Approximate(Expr exact, CancellationToken ct)
    {
        if (exact is NumberExpr { Value.IsExact: false } or NumberExpr { Value.IsExactInteger: true }) return null;
        if (exact is StringExpr or BoolExpr) return null;
        try
        {
            var approx = Evaluator.Eval(exact, Context(ct, complex: true, approximate: true).WithFree(exact.FreeSymbols()));
            return approx.Equals(exact) ? null : approx;
        }
        catch (Exception ex) when (ex is CalcException or ArithmeticException)
        {
            return null;
        }
    }

    /// <summary>True when the name holds a stored value in the session (a known number, not an unknown).</summary>
    public bool IsStored(string name) => Session.Env.Variables.ContainsKey(name);

    /// <summary>
    /// A note naming the stored values an input uses (a = 2 from Advanced mode), so it is clear why they are not
    /// unknowns; null when it uses none. The unknowns themselves are never replaced by stored values.
    /// </summary>
    internal string? StoredNote(IEnumerable<string> names, IEnumerable<string> unknowns)
    {
        var skip = unknowns.ToHashSet();
        var used = names.Distinct().Where(n => !skip.Contains(n) && Session.Env.TryGetVariable(n, out _)).ToList();
        if (used.Count == 0) return null;
        var parts = used.Select(n => Session.Env.TryGetVariable(n, out var v) ? $"{n} = {Show(v)}" : n);
        return $"Uses the stored value{(used.Count == 1 ? "" : "s")} {string.Join(", ", parts)}.";
    }

    /// <summary>On-screen text with the session's number format.</summary>
    public string Show(Expr e) => MathText.Show(e, Settings);

    /// <summary>The value of f with the variable set to a number (NaN when undefined or not real).</summary>
    public double ValueAt(Expr f, string x, double at, CancellationToken ct)
    {
        try
        {
            var ctx = Context(ct, approximate: true).WithLocal(x, new NumberExpr(Number.Approx(at)));
            var r = Evaluator.Eval(f, ctx);
            if (r is NumberExpr n && n.Value.IsFinite && (n.Value.IsReal || Math.Abs(n.Value.Im) < 1e-12 * Math.Max(1, Math.Abs(n.Value.Re))))
                return n.Value.Re;
            return double.NaN;
        }
        catch (Exception ex) when (ex is CalcException or ArithmeticException)
        {
            return double.NaN;
        }
    }

    /// <summary>The value of f with several variables set to numbers (NaN when undefined or not real).</summary>
    public double ValueAt(Expr f, IReadOnlyDictionary<string, double> at, CancellationToken ct)
    {
        try
        {
            var ctx = Context(ct, approximate: true).WithLocals(at.Select(kv => new KeyValuePair<string, Expr>(kv.Key, new NumberExpr(Number.Approx(kv.Value)))));
            var r = Evaluator.Eval(f, ctx);
            if (r is NumberExpr n && n.Value.IsFinite && (n.Value.IsReal || Math.Abs(n.Value.Im) < 1e-12 * Math.Max(1, Math.Abs(n.Value.Re))))
                return n.Value.Re;
            return double.NaN;
        }
        catch (Exception ex) when (ex is CalcException or ArithmeticException)
        {
            return double.NaN;
        }
    }

    // ------------------------------------------------------------------ values

    /// <summary>
    /// Describes one solution value for display: exact and decimal text, a + bi for complex values, and its parameters
    /// (names not in <paramref name="known"/>, such as n in 2πn + π/6).
    /// </summary>
    public SolutionValue Describe(Expr value, IReadOnlyCollection<string> known, CancellationToken ct)
    {
        var parameters = value.FreeSymbols().Where(s => !known.Contains(s)).ToList();
        var n = Numeric(value, ct);
        bool numeric = value is NumberExpr { Value.IsExact: false };
        bool real = n is { } nv ? Math.Abs(nv.Im) <= 1e-12 * Math.Max(1, Math.Abs(nv.Re)) : !ContainsImaginary(value);
        var approx = numeric ? null : Approximate(value, ct);
        string? approxText = approx is null ? null : Show(approx);
        if (parameters.Count > 0)
        {
            // 2πn + π/6 has no single decimal; show the member for n = 0 instead.
            approx = null;
            approxText = null;
            try
            {
                var atZero = Evaluate(value.Map(e => e is SymbolExpr s && parameters.Contains(s.Name) ? Expr.Zero : e), ct);
                if (atZero.FreeSymbols().Count == 0 && Approximate(atZero, ct) is { } a0)
                    approxText = $"{Show(a0)} when {string.Join(", ", parameters.Select(p => p + " = 0"))}";
            }
            catch (SolverException)
            {
                // No decimal hint then.
            }
        }
        string text = Show(value);
        string input = MathText.Input(value);
        string latex = MathText.Latex(value);
        if (!real && value is not NumberExpr && SplitComplex(value, ct) is var (re, im))
        {
            var form = ComplexForm(re, im, ct);
            text = form.Text;
            latex = form.Latex;
            input = form.Input;
        }
        return new SolutionValue
        {
            Exact = value,
            Approx = approx,
            IsReal = real,
            IsNumeric = numeric,
            Re = n?.Re ?? double.NaN,
            Im = n?.Im ?? double.NaN,
            Text = text,
            ApproxText = approxText,
            Input = input,
            Latex = latex,
            Parameters = parameters,
        };
    }

    private static bool ContainsImaginary(Expr e) => e switch
    {
        NumberExpr n => !n.Value.IsReal,
        _ => e.Children.Any(ContainsImaginary),
    };

    /// <summary>
    /// Splits an exact complex value into its real and imaginary parts: (−i√3 − 1)/2 gives −1/2 and −√3/2.
    /// Null when it is not of the form a + bi with real a and b.
    /// </summary>
    public (Expr Re, Expr Im)? SplitComplex(Expr value, CancellationToken ct)
    {
        try
        {
            var expanded = Evaluate(CallExpr.Of("expand", value), ct, complex: true);
            var terms = expanded is AddExpr a ? (IReadOnlyList<Expr>)a.Terms : [expanded];
            var re = new List<Expr>();
            var im = new List<Expr>();
            foreach (var t in terms)
            {
                var (coef, rest) = Algebra.SplitCoefficient(t);
                if (ContainsImaginary(rest)) return null;
                if (coef.IsReal)
                {
                    re.Add(t);
                    continue;
                }
                Number reCoef = coef.IsExact ? Number.Exact(coef.ReQ) : Number.Approx(coef.Re);
                Number imCoef = coef.IsExact ? Number.Exact(coef.ImQ) : Number.Approx(coef.Im);
                if (!reCoef.IsZero) re.Add(Algebra.Mul(new NumberExpr(reCoef), rest));
                im.Add(Algebra.Mul(new NumberExpr(imCoef), rest));
            }
            if (im.Count == 0) return null;
            var reExpr = re.Count == 0 ? Expr.Zero : Evaluate(Algebra.Add(re), ct);
            var imExpr = Evaluate(Algebra.Add(im), ct);
            return (reExpr, imExpr);
        }
        catch (SolverException)
        {
            return null;
        }
    }

    /// <summary>The a + bi texts: on screen, as linear input, and as LaTeX.</summary>
    private (string Text, string Input, string Latex) ComplexForm(Expr re, Expr im, CancellationToken ct)
    {
        bool negative = Real(im, ct) < 0;
        var size = negative ? Evaluate(Algebra.Neg(im), ct) : im;
        bool plain = size is NumberExpr { Value.IsReal: true } sn && (sn.Value.IsExactInteger || !sn.Value.IsExact);
        bool one = size.IsOne;
        string sizeText = one ? "" : plain ? Show(size) : "(" + Show(size) + ")";
        string sizeInput = one ? "" : plain ? MathText.Input(size) : "(" + MathText.Input(size) + ")";
        string sizeLatex = one ? "" : plain ? MathText.Latex(size) : @"\left(" + MathText.Latex(size) + @"\right)";
        bool hasRe = !re.IsZero;
        string sign = negative ? MathText.Minus.ToString() : "+";
        string text = hasRe ? $"{Show(re)} {sign} {sizeText}i" : (negative ? MathText.Minus.ToString() : "") + sizeText + "i";
        string input = hasRe ? $"{MathText.Input(re)} {(negative ? "-" : "+")} {sizeInput}i" : (negative ? "-" : "") + sizeInput + "i";
        string latex = hasRe ? $"{MathText.Latex(re)} {(negative ? "-" : "+")} {sizeLatex}i" : (negative ? "-" : "") + sizeLatex + "i";
        return (text, input, latex);
    }

    // ------------------------------------------------------------------ shared helpers

    /// <summary>left − right of an equation (the expression itself when it is not an equation).</summary>
    public static Expr Residual(Expr e) => e is RelationExpr { Op: RelOp.Equal } r ? Algebra.Sub(r.Left, r.Right) : e;

    /// <summary>The two sides of an equation (the expression and 0 when it is not an equation).</summary>
    public static (Expr Left, Expr Right) Sides(Expr e) => e is RelationExpr r ? (r.Left, r.Right) : (e, Expr.Zero);

    /// <summary>The step-by-step explanation of solve(equation, x), or an empty list when there is none.</summary>
    public IReadOnlyList<string> Steps(Expr equation, string x, CancellationToken ct, bool complex = false)
    {
        try
        {
            var r = Evaluate(CallExpr.Of("steps", CallExpr.Of("solve", equation, new SymbolExpr(x))), ct, complex);
            if (r is not ListExpr list) return [];
            return [.. list.Items.OfType<StringExpr>().Select(s => s.Value)];
        }
        catch (SolverException)
        {
            return [];
        }
    }

    /// <summary>A check value |left − right| as short text: 0, 2.2e−16.</summary>
    public static string CheckText(double residual)
    {
        if (double.IsNaN(residual)) return "?";
        double a = Math.Abs(residual);
        if (a == 0) return "0";
        if (a < 1e-3) return MathText.Typographic(a.ToString("0.0e0", System.Globalization.CultureInfo.InvariantCulture));
        return MathText.Short(a, 3);
    }
}

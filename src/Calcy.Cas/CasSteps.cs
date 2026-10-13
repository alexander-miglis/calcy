using Calcy.Cas.Calculus;
using Calcy.Cas.Polynomials;
using Calcy.Cas.Solving;
using Calcy.Cas.Symbolic;
using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;
using Calcy.Core.Printing;

namespace Calcy.Cas;

/// <summary>
/// Human-readable step-by-step explanations for solving linear and quadratic equations, derivatives (which rule is
/// applied), basic integrals and limits. Each method returns the steps in order, ending with the result.
/// </summary>
public static class CasSteps
{
    /// <summary>
    /// Explains an unevaluated call such as solve(2x + 3 = 7, x), diff(x^2 sin(x), x), integrate(x^2, x) or
    /// limit(sin(x)/x, x, 0). Returns a single explanatory step for anything else.
    /// </summary>
    public static IReadOnlyList<string> Explain(Expr call, EvalContext ctx)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(ctx);
        if (call is not CallExpr c) return ["Steps are available for solve, diff, integrate and limit, for example steps(solve(2x + 3 = 7, x))"];
        string name = c.Name;
        if (ctx.Functions.TryGet(name, out var def)) name = def.Name;
        string Var(int i) => c.Args.Length > i && c.Args[i] is SymbolExpr s ? s.Name : throw new CalcException($"steps: argument {i + 1} of {c.Name} must be a variable");
        return name switch
        {
            "solve" when c.Args.Length >= 1 => SolveSteps(c.Args[0], c.Args.Length > 1 ? Var(1) : DefaultVariable(c.Args[0], ctx), ctx),
            "diff" when c.Args.Length >= 1 => DiffSteps(c.Args[0], c.Args.Length > 1 ? Var(1) : DefaultVariable(c.Args[0], ctx), ctx),
            "integrate" when c.Args.Length is 2 or 4 => IntegrateSteps(c.Args[0], Var(1), ctx, c.Args.Length == 4 ? c.Args[2] : null, c.Args.Length == 4 ? c.Args[3] : null),
            "limit" when c.Args.Length >= 3 => LimitSteps(c.Args[0], Var(1), c.Args[2], ctx),
            _ => [$"Steps are not available for {c.Name}; try solve, diff, integrate or limit"],
        };
    }

    /// <summary>Parses and explains a line of input such as "solve(x^2 - 5x + 6 = 0, x)".</summary>
    public static IReadOnlyList<string> Explain(string input, CalcSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Explain(session.ParseExpression(input), session.Context());
    }

    private static string DefaultVariable(Expr raw, EvalContext ctx)
    {
        var free = Evaluator.Eval(raw, ctx.WithApproximate(false)).FreeSymbols();
        if (free.Count == 1) return free[0];
        return free.Contains("x") || free.Count == 0 ? "x" : free[0];
    }

    private static string T(Expr e) => ExprPrinter.ToText(e);

    // ------------------------------------------------------------------ equations

    /// <summary>Steps for solving an equation in one variable (detailed for linear and quadratic equations).</summary>
    public static IReadOnlyList<string> SolveSteps(Expr equation, string x, EvalContext ctx)
    {
        using var scope = CasEnv.Enter(ctx);
        var env = CasEnv.Require;
        var steps = new List<string>();
        var eq = env.EvalArg(equation, x);
        if (eq is BoolExpr truth)
        {
            steps.Add(truth.Value ? "Both sides are always equal: every value is a solution." : "The two sides are never equal: there is no solution.");
            steps.Add($"Solution: {T(truth)}");
            return steps;
        }
        var rel = eq as RelationExpr ?? new RelationExpr(RelOp.Equal, eq, Expr.Zero);
        steps.Add($"Solve {T(env.Externalize(rel))} for {x}.");
        if (rel.Op != RelOp.Equal)
        {
            steps.Add("Find where both sides are equal, then test the sign of left - right between those points.");
            var ineq = Inequalities.Solve(rel, x);
            steps.Add(ineq is null ? "No exact solution was found." : $"Solution: {T(env.Finish(ineq))}");
            return steps;
        }
        var f = Algebra.Sub(rel.Left, rel.Right);
        var poly = ExprPoly.From(f, x);
        var xs = new SymbolExpr(x);
        if (poly is not null && poly.Degree == 1)
        {
            var a = poly[1];
            var b = poly[0];
            var leftX = ExprPoly.From(rel.Left, x);
            bool simple = leftX is not null && leftX.Degree == 1 && !rel.Right.ContainsSymbol(x);
            if (simple && !Simplifier.IsZero(leftX![0]))
            {
                var c0 = leftX[0];
                bool add = CasEnv.SignOf(c0) is >= 0 || c0.FreeSymbols().Count > 0;
                var rhs = Simplifier.Simplify(Algebra.Sub(rel.Right, c0));
                steps.Add(add
                    ? $"Subtract {T(c0)} from both sides: {T(Algebra.Mul(leftX[1], xs))} = {T(rhs)}"
                    : $"Add {T(Algebra.Neg(c0))} to both sides: {T(Algebra.Mul(leftX[1], xs))} = {T(rhs)}");
            }
            else if (!simple)
            {
                steps.Add($"Move the terms with {x} to the left side and the other terms to the right side: {T(Algebra.Mul(a, xs))} = {T(Simplifier.Simplify(Algebra.Neg(b)))}");
            }
            var sol = Simplifier.Simplify(Algebra.Neg(Algebra.Div(b, a)));
            if (!a.IsOne) steps.Add($"Divide both sides by {T(a)}: {x} = {T(env.Finish(sol))}");
            steps.Add($"Solution: {x} = {T(env.Finish(sol))}");
            return steps;
        }
        if (poly is not null && poly.Degree == 2 && poly.Coefficients.All(c => c is NumberExpr { Value.IsExactRational: true }))
        {
            var a = ((NumberExpr)poly[2]).Value.ReQ;
            var b = ((NumberExpr)poly[1]).Value.ReQ;
            var c = ((NumberExpr)poly[0]).Value.ReQ;
            var standard = new RelationExpr(RelOp.Equal, poly.ToExpr(), Expr.Zero);
            if (!rel.Right.IsZero) steps.Add($"Move everything to one side: {T(standard)}");
            var disc = b * b - 4 * a * c;
            var factored = Factoring.Factor(poly.ToExpr());
            bool factors = factored is MulExpr fm && fm.Factors.Count(t => t.ContainsSymbol(x)) >= 2 || factored is PowExpr;
            if (factors && disc >= 0)
            {
                steps.Add($"Factor the left side: {T(factored)} = 0");
                var parts = factored is PowExpr pw ? [pw.Base] : ((MulExpr)factored).Factors.Where(t => t.ContainsSymbol(x)).Select(t => t is PowExpr p ? p.Base : t).ToList();
                steps.Add("A product is zero when one of its factors is zero: " + string.Join(" or ", parts.Select(p => $"{T(p)} = 0")));
            }
            else
            {
                steps.Add($"Use the quadratic formula with a = {T(Algebra.Q(a))}, b = {T(Algebra.Q(b))}, c = {T(Algebra.Q(c))}.");
                steps.Add($"Discriminant: Δ = b² - 4ac = {T(Algebra.Q(disc))}");
                steps.Add(disc.IsPositive ? "Δ > 0, so there are two real solutions." : disc.IsZero ? "Δ = 0, so there is one (double) solution." : "Δ < 0, so there are no real solutions; the solutions are complex.");
                steps.Add($"{x} = (-b ± √Δ)/(2a) = ({T(Algebra.Q(-b))} ± √{T(Algebra.Q(disc))})/{T(Algebra.Q(2 * a))}");
            }
        }
        var solver = new Solver(x, CasEnv.FreshName("n", f));
        var methods = new List<(string, Expr)>();
        Solver.Trace = (m, e) => methods.Add((m, e));
        RootSet? roots;
        try { roots = solver.Solve(f); }
        finally { Solver.Trace = null; }
        if (poly is null || poly.Degree > 2)
        {
            foreach (var (m, e) in methods.Take(4))
            {
                var text = m switch
                {
                    "factor" => $"Factor: {T(env.Externalize(e))} = 0, and solve each factor.",
                    "substitute" => $"Substitute u = {T(env.Externalize(e))} and solve for u first.",
                    "log" => "Take logarithms of both sides.",
                    "root" => "Take roots of both sides.",
                    "sin" or "cos" or "tan" => $"Use the inverse function and add the period (n is any integer).",
                    "ln" or "log10" => "Exponentiate both sides.",
                    "abs-cases" => "Split into the cases where the inside of |...| is positive or negative.",
                    "numeric" => "No exact method applies; the solutions are found numerically.",
                    "trig-identity" => "Rewrite with sin² + cos² = 1 and multiple-angle formulas.",
                    _ => null,
                };
                if (text is not null && !steps.Contains(text)) steps.Add(text);
            }
        }
        if (roots is null) steps.Add("No solution was found.");
        else steps.Add("Solution: " + T(env.Finish(SolveFormat.Single(roots, x))));
        return steps;
    }

    // ------------------------------------------------------------------ derivatives

    /// <summary>Steps for a derivative: the rules applied (sum, product, quotient, chain, power...), then the result.</summary>
    public static IReadOnlyList<string> DiffSteps(Expr expr, string x, EvalContext ctx)
    {
        using var scope = CasEnv.Enter(ctx);
        var env = CasEnv.Require;
        var f = env.EvalArg(expr, x);
        var steps = new List<string> { $"Find d/d{x} of {T(env.Externalize(f))}." };
        if (env.AngleFactor is not null) steps.Add("The angle unit is degrees: d/dx sin(x) = π/180·cos(x) because sin(x°) = sin(πx/180).");
        var events = new List<(string Rule, Expr E)>();
        Differentiator.Trace = (r, e) => events.Add((r, e));
        Expr result;
        try { result = Differentiator.Diff(f, x, 1); }
        finally { Differentiator.Trace = null; }
        var seen = new HashSet<string>();
        foreach (var (rule, e) in events)
        {
            var shown = T(env.Externalize(e));
            string? text = rule switch
            {
                "sum" => $"Sum rule: differentiate {shown} term by term.",
                "constant" => $"Constant multiple rule: keep the constant factor of {shown}.",
                "product" => $"Product rule for {shown}: (u·v)' = u'·v + u·v'.",
                "quotient" => $"Quotient rule for {shown}: (u/v)' = (u'·v - u·v')/v².",
                "power" => $"Power rule: d/d{x} {shown} = {T(env.Externalize(Simplifier.Tidy(Differentiator.D(e, x))))}.",
                "chain-power" => $"Chain rule with the power rule for {shown}: n·uⁿ⁻¹·u'.",
                "exp" => $"Exponential rule for {shown}: d/d{x} e^u = e^u·u'.",
                "exponential" => $"Exponential rule for {shown}: d/d{x} a^u = a^u·ln(a)·u'.",
                "general-power" => $"Write {shown} as e^(v·ln(u)) and differentiate.",
                "function" => $"Derivative of {shown}: {T(env.Externalize(Simplifier.Tidy(Differentiator.D(e, x))))}.",
                "chain" => $"Chain rule for {shown}: f'(u)·u' with u = {T(env.Externalize(((CallExpr)e).Args[^1]))}.",
                "log" => $"Logarithm rule for {shown}: d/d{x} log_b(u) = u'/(u·ln(b)).",
                "unknown" => $"{shown} is an unknown function, so its derivative is written with a prime.",
                _ => null,
            };
            if (text is not null && seen.Add(text)) steps.Add(text);
            if (steps.Count > 14) break;
        }
        steps.Add($"Result: {T(env.Finish(result))}");
        return steps;
    }

    // ------------------------------------------------------------------ integrals

    /// <summary>Steps for an indefinite (or definite, when bounds are given) integral.</summary>
    public static IReadOnlyList<string> IntegrateSteps(Expr expr, string x, EvalContext ctx, Expr? lower = null, Expr? upper = null)
    {
        using var scope = CasEnv.Enter(ctx);
        var env = CasEnv.Require;
        var f = env.EvalArg(expr, x);
        var steps = new List<string> { $"Integrate {T(env.Externalize(f))} with respect to {x}." };
        var log = new List<(string Rule, Expr Expr)>();
        Integrator.Log = log;
        Expr? F;
        try { F = Integrator.Antiderivative(f, x); }
        finally { Integrator.Log = null; }
        if (F is null)
        {
            steps.Add("No antiderivative in terms of elementary functions was found.");
            if (lower is not null && upper is not null)
            {
                var v = DefiniteIntegrals.Integrate(f, x, env.EvalArg(lower), env.EvalArg(upper));
                if (v is not null) steps.Add($"Numeric value: {T(env.Finish(v))}");
            }
            return steps;
        }
        var seen = new HashSet<string>();
        foreach (var (rule, e) in log)
        {
            var shown = T(env.Externalize(e));
            string? text = rule switch
            {
                "sum" => $"Split the integral of {shown} into a sum of integrals.",
                "constant" => $"Take the constant factor out of ∫ {shown} d{x}.",
                "power" or "power-linear" => $"Power rule: ∫ uⁿ du = uⁿ⁺¹/(n + 1) for {shown}.",
                "reciprocal" or "reciprocal-linear" => $"∫ 1/u du = ln|u| for {shown}.",
                "exp" => $"∫ e^u du = e^u for {shown}.",
                "exponential" => $"∫ a^u du = a^u/ln(a) for {shown}.",
                "table" or "table-linear" => $"Standard integral of {shown}.",
                "polynomial" => $"Integrate the polynomial {shown} term by term with the power rule.",
                "partial-fractions" => $"Split {shown} into partial fractions: {T(env.Externalize(PolyTools.Apart(e, x) ?? e))}.",
                "parts-tabular" => $"Integrate {shown} by parts repeatedly (tabular method), differentiating the polynomial.",
                "parts-log" => $"Integrate {shown} by parts with u = the logarithm or inverse function and dv = the rest.",
                "parts-cyclic" => $"Integrate {shown} by parts twice and solve for the integral.",
                "substitution" => $"Substitute u = {shown}.",
                "trig-odd" => $"Odd power of sin or cos in {shown}: substitute u = cos or u = sin and use sin² + cos² = 1.",
                "trig-even" => $"Even powers in {shown}: reduce them with double-angle formulas.",
                "trig-reduction" => $"Use a reduction formula for {shown}.",
                "radical" => $"Complete the square under the root in {shown} and use a standard form.",
                "expand" => $"Expand {shown} first.",
                _ => null,
            };
            if (text is not null && seen.Add(text)) steps.Add(text);
            if (steps.Count > 14) break;
        }
        if (lower is null || upper is null)
        {
            steps.Add($"Result: {T(env.Finish(F))} + C");
            return steps;
        }
        var a = env.EvalArg(lower);
        var b = env.EvalArg(upper);
        steps.Add($"Antiderivative: F({x}) = {T(env.Finish(F))}");
        var value = DefiniteIntegrals.Integrate(f, x, a, b);
        steps.Add($"Evaluate F({T(b)}) - F({T(a)}) (checking that the integrand has no singularity in between).");
        steps.Add(value is null ? "The integral could not be evaluated." : $"Result: {T(env.Finish(value))}");
        return steps;
    }

    // ------------------------------------------------------------------ limits

    /// <summary>Steps for a limit: substitution, indeterminate forms, factoring and cancelling, L'Hôpital's rule.</summary>
    public static IReadOnlyList<string> LimitSteps(Expr expr, string x, Expr point, EvalContext ctx)
    {
        using var scope = CasEnv.Enter(ctx);
        var env = CasEnv.Require;
        var f = env.EvalArg(expr, x);
        var a = env.EvalArg(point);
        var steps = new List<string> { $"Find the limit of {T(env.Externalize(f))} as {x} → {T(a)}." };
        var result = Limits.Limit(f, x, a, 0);
        bool infinite = a is ConstantExpr { IsInfinite: true };
        var direct = infinite ? null : CasEnv.TrySubst(f, x, a);
        if (direct is not null && direct.IsConstantValue() && CasEnv.Numeric(direct) is { IsFinite: true })
        {
            steps.Add($"The expression is continuous there: substitute {x} = {T(a)} to get {T(env.Finish(direct))}.");
            steps.Add($"Result: {T(env.Finish(result ?? direct))}");
            return steps;
        }
        var (num, den) = Limits.SplitFraction(f);
        if (!den.IsOne)
        {
            var ln = Limits.Limit(num, x, a, 0);
            var ld = Limits.Limit(den, x, a, 0);
            bool zeroZero = ln is not null && ld is not null && ln.IsZero && ld.IsZero;
            bool infInf = ln is ConstantExpr { IsInfinite: true } && ld is ConstantExpr { IsInfinite: true };
            if (zeroZero) steps.Add("Substituting gives 0/0, an indeterminate form.");
            if (infInf) steps.Add("Both numerator and denominator grow without bound: the form ∞/∞ is indeterminate.");
            var rat = Simplifier.Rational(f, x);
            bool rational = rat is not null && rat.Value.Km.Count == 1;
            if (rational && !infinite && zeroZero)
            {
                var cancelled = Simplifier.Cancel(f);
                var shownFactored = new MulExpr([Factoring.Factor(num), new PowExpr(Factoring.Factor(den), Expr.MinusOne)]);
                steps.Add($"Factor and cancel the common factor: {T(env.Externalize(shownFactored))} = {T(env.Externalize(cancelled))}");
                var sub = CasEnv.TrySubst(cancelled, x, a);
                if (sub is not null) steps.Add($"Now substitute {x} = {T(a)}: {T(env.Finish(sub))}");
            }
            else if (rational && infinite)
            {
                steps.Add("Divide the numerator and the denominator by the highest power of " + x + " in the denominator and compare the degrees.");
            }
            else if (zeroZero || infInf)
            {
                var dn = Simplifier.Tidy(Differentiator.D(num, x));
                var dd = Simplifier.Tidy(Differentiator.D(den, x));
                steps.Add($"Apply L'Hôpital's rule: differentiate the numerator and the denominator: {T(env.Externalize(Algebra.Div(dn, dd)))}");
                var after = Limits.Limit(Simplifier.Simplify(Algebra.Div(dn, dd), trig: false), x, a, 0);
                if (after is not null) steps.Add($"The new limit is {T(env.Finish(after))}.");
            }
        }
        else if (infinite) steps.Add("Compare the growth of the terms: the dominant term decides the limit.");
        if (result is ConstantExpr { Constant: ConstantKind.Undefined }) steps.Add("The one-sided limits differ (or the expression oscillates), so the limit does not exist.");
        steps.Add(result is null ? "The limit could not be determined." : $"Result: {T(env.Finish(result))}");
        return steps;
    }
}

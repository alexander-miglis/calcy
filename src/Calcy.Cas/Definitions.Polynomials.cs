using Calcy.Cas.Symbolic;
using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;

namespace Calcy.Cas;

internal static partial class Definitions
{
    private static IEnumerable<FunctionDefinition> PolynomialFunctions(FunctionRegistry r)
    {
        yield return Def("collect", c => Run(c, env =>
            {
                var e = EvalWithVar(c, env, 1, out var x);
                return MapList(e, v => PolyTools.Collect(v, x));
            }),
            AlgebraCat, "collect(expr, x)", "Groups the terms by powers of x: collect(ax + bx + c, x) = (a + b)x + c.", "collect(a x + b x + c, x)", min: 1, max: 2);
        yield return Def("coeffs", c => Run(c, env =>
            {
                var e = EvalWithVar(c, env, 1, out var x);
                return PolyTools.Coeffs(e, x);
            }),
            AlgebraCat, "coeffs(p, x)", "The coefficients of a polynomial, highest power first.", "coeffs(3x^2 + 2x + 1, x)", min: 1, max: 2,
            aliases: ["coefficients", "polycoeffs"]);
        yield return Def("degree", c => Run(c, env =>
            {
                if (c.Count == 1) return PolyTools.Degree(env.EvalArg(c.Args[0]), null);
                var x = VarArg(c, 1);
                return PolyTools.Degree(env.EvalArg(c.Args[0], x), x);
            }),
            AlgebraCat, "degree(p, x)", "The highest power of x in a polynomial (total degree without x).", "degree(x^3 + x, x)", min: 1, max: 2,
            aliases: ["polydegree"]);
        yield return Def("lcoeff", c => Run(c, env =>
            {
                var e = EvalWithVar(c, env, 1, out var x);
                return PolyTools.LeadingCoefficient(e, x);
            }),
            AlgebraCat, "lcoeff(p, x)", "The coefficient of the highest power of x.", "lcoeff(3x^2 + 1, x)", min: 1, max: 2,
            aliases: ["leadcoeff"]);
        yield return Def("polyquo", c => Run(c, env =>
            {
                var (a, b, x) = TwoPolys(c, env);
                return PolyTools.Divide(a, b, x).Quotient;
            }),
            AlgebraCat, "polyquo(a, b, x)", "Quotient of polynomial division a ÷ b.", "polyquo(x^3 - 1, x - 1, x)", min: 2, max: 3,
            aliases: ["quo", "quotient"]);
        yield return Def("polyrem", c => Run(c, env =>
            {
                var (a, b, x) = TwoPolys(c, env);
                return PolyTools.Divide(a, b, x).Remainder;
            }),
            AlgebraCat, "polyrem(a, b, x)", "Remainder of polynomial division a ÷ b.", "polyrem(x^3 + 2, x - 1, x)", min: 2, max: 3,
            aliases: ["remainder"]);
        yield return Def("polygcd", c => Run(c, env => PolyTools.Gcd(env.EvalArg(c.Args[0]), env.EvalArg(c.Args[1]))),
            AlgebraCat, "polygcd(a, b)", "Greatest common divisor of two polynomials.", "polygcd(x^2 - 1, x^2 + 2x + 1)", min: 2, max: 2,
            aliases: ["gcdpoly"]);
        yield return Def("polylcm", c => Run(c, env => PolyTools.Lcm(env.EvalArg(c.Args[0]), env.EvalArg(c.Args[1]))),
            AlgebraCat, "polylcm(a, b)", "Least common multiple of two polynomials.", "polylcm(x^2 - 1, x + 1)", min: 2, max: 2,
            aliases: ["lcmpoly"]);
        yield return Def("discriminant", c => Run(c, env =>
            {
                var e = EvalWithVar(c, env, 1, out var x);
                return PolyTools.Discriminant(e, x);
            }),
            AlgebraCat, "discriminant(p, x)", "Discriminant of a polynomial: b² - 4ac for a quadratic.", "discriminant(a x^2 + b x + c, x)", min: 1, max: 2,
            aliases: ["discrim"]);
        yield return Def("resultant", c => Run(c, env =>
            {
                var (a, b, x) = TwoPolys(c, env);
                return PolyTools.Resultant(a, b, x);
            }),
            AlgebraCat, "resultant(p, q, x)", "Resultant of two polynomials (zero when they share a root).", "resultant(x^2 - 1, x - 1, x)", min: 2, max: 3);
        yield return Def("compsquare", c => Run(c, env =>
            {
                var e = EvalWithVar(c, env, 1, out var x);
                return PolyTools.CompleteSquare(e, x);
            }),
            AlgebraCat, "compsquare(expr, x)", "Completes the square: x² + 6x + 5 = (x + 3)² - 4.", "compsquare(x^2 + 6x + 5, x)", min: 1, max: 2,
            aliases: ["completesquare", "completeSquare"]);
        yield return Def("apart", c => Run(c, env =>
            {
                var e = EvalWithVar(c, env, 1, out var x);
                return MapList(e, v => PolyTools.Apart(v, x) ?? v);
            }),
            AlgebraCat, "apart(expr, x)", "Partial fractions: 1/(x² - 1) = 1/(2(x - 1)) - 1/(2(x + 1)).", "apart(1/(x^2 - 1), x)", min: 1, max: 2,
            aliases: ["partfrac"]);
    }

    /// <summary>Evaluates argument 0 with the variable in argument <paramref name="varIndex"/> free (or the default variable).</summary>
    internal static Expr EvalWithVar(CallInfo c, CasEnv env, int varIndex, out string x)
    {
        if (c.Count > varIndex)
        {
            x = VarArg(c, varIndex);
            return env.EvalArg(c.Args[0], x);
        }
        var e = env.EvalArg(c.Args[0]);
        x = DefaultVar(c, e);
        return env.EvalArg(c.Args[0], x);
    }

    private static (Expr A, Expr B, string X) TwoPolys(CallInfo c, CasEnv env)
    {
        if (c.Count > 2)
        {
            var x = VarArg(c, 2);
            return (env.EvalArg(c.Args[0], x), env.EvalArg(c.Args[1], x), x);
        }
        var a = env.EvalArg(c.Args[0]);
        var b = env.EvalArg(c.Args[1]);
        var v = DefaultVar(c, Algebra.Add(a, b));
        return (env.EvalArg(c.Args[0], v), env.EvalArg(c.Args[1], v), v);
    }
}

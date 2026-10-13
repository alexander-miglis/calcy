using System.Text;
using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;
using Calcy.Core.Printing;

namespace Calcy.Cas.Tests;

/// <summary>Sessions with the CAS registered, plus helpers to evaluate and compare results.</summary>
internal static class Cas
{
    public static CalcSession Session(CalcSettings? settings = null)
    {
        var registry = FunctionRegistry.CreateDefault();
        CasModule.Register(registry);
        return new CalcSession(settings, registry);
    }

    public static CalcSession Degrees() => Session(new CalcSettings { Angle = AngleMode.Degree });

    /// <summary>The exact result of an input line (fails the test on errors).</summary>
    public static Expr Exact(string input, CalcSession? s = null)
    {
        s ??= Session();
        var r = s.Evaluate(input);
        Assert.False(r.IsError, $"'{input}' failed: {r.ErrorMessage}");
        return r.Exact!;
    }

    /// <summary>The parsed (unsimplified) input.</summary>
    public static Expr Parse(string input, CalcSession s) => s.ParseExpression(input);

    /// <summary>The printed exact result, with redundant double parentheses removed.</summary>
    public static string Text(string input, CalcSession? s = null) => Normalize(ExprPrinter.ToText(Exact(input, s)));

    public static string Error(string input, CalcSession? s = null)
    {
        s ??= Session();
        var r = s.Evaluate(input);
        Assert.True(r.IsError, $"'{input}' should fail but gave {(r.Exact is null ? "" : ExprPrinter.ToText(r.Exact))}");
        return r.ErrorMessage!;
    }

    /// <summary>Collapses ((...)) to (...) where both pairs enclose the same text.</summary>
    public static string Normalize(string text)
    {
        while (true)
        {
            int start = -1;
            for (int i = 0; i + 1 < text.Length; i++)
            {
                if (text[i] != '(' || text[i + 1] != '(') continue;
                int closeInner = Match(text, i + 1);
                int closeOuter = Match(text, i);
                if (closeInner >= 0 && closeOuter == closeInner + 1)
                {
                    start = i;
                    text = text.Remove(closeOuter, 1).Remove(i, 1);
                    break;
                }
            }
            if (start < 0) return text;
        }
    }

    private static int Match(string s, int open)
    {
        int depth = 0;
        for (int i = open; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')' && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary>The real value of an expression with the variable set to v (NaN when undefined or not real).</summary>
    public static double Value(Expr e, string variable, double v, CalcSession s)
    {
        try
        {
            var ctx = s.Context(approximate: true).WithLocal(variable, new NumberExpr(Number.Approx(v)));
            var r = Evaluator.Eval(e, ctx);
            if (r is NumberExpr n && (n.Value.IsReal || Math.Abs(n.Value.Im) < 1e-12)) return n.Value.Re;
            return double.NaN;
        }
        catch (CalcException) { return double.NaN; }
        catch (ArithmeticException) { return double.NaN; }
    }

    public static double Value(Expr e, CalcSession s)
    {
        var r = Evaluator.Eval(e, s.Context(approximate: true));
        Assert.True(r is NumberExpr, $"{ExprPrinter.ToText(e)} is not a number");
        return ((NumberExpr)r).Value.Re;
    }

    /// <summary>Central difference derivative.</summary>
    public static double NumericDerivative(Expr f, string x, double at, CalcSession s)
    {
        double h = 1e-5 * Math.Max(1, Math.Abs(at));
        return (Value(f, x, at + h, s) - Value(f, x, at - h, s)) / (2 * h);
    }

    /// <summary>The numeric values on the right of x = v in a solve result (sorted).</summary>
    public static List<double> SolutionValues(Expr result, CalcSession s)
    {
        var values = new List<double>();
        void Visit(Expr e)
        {
            switch (e)
            {
                case RelationExpr { Op: RelOp.Equal } r:
                    values.Add(Value(r.Right, s));
                    break;
                case LogicExpr l:
                    foreach (var o in l.Operands) Visit(o);
                    break;
            }
        }
        Visit(result);
        values.Sort();
        return values;
    }

    public static void AssertClose(double expected, double actual, double tol = 1e-8)
    {
        Assert.True(Math.Abs(expected - actual) <= tol * Math.Max(1, Math.Abs(expected)), $"expected {expected}, got {actual}");
    }

    public static void AssertSameValues(IReadOnlyList<double> expected, IReadOnlyList<double> actual, double tol = 1e-8)
    {
        Assert.Equal(expected.Count, actual.Count);
        var e = expected.OrderBy(v => v).ToList();
        var a = actual.OrderBy(v => v).ToList();
        for (int i = 0; i < e.Count; i++) AssertClose(e[i], a[i], tol);
    }

    /// <summary>The factors of a factored expression as canonical strings (powers repeated), sorted, plus the constant.</summary>
    public static (string Constant, List<string> Factors) FactorMultiset(Expr e, CalcSession s)
    {
        var factors = new List<string>();
        Expr constant = Expr.One;
        void Add(Expr f, int times)
        {
            var canonical = Normalize(ExprPrinter.ToText(s.Evaluate(f)));
            for (int i = 0; i < times; i++) factors.Add(canonical);
        }
        var items = e is MulExpr m ? (IReadOnlyList<Expr>)m.Factors : [e];
        foreach (var f in items)
        {
            if (f is NumberExpr) { constant = Algebra.Mul(constant, f); continue; }
            if (f is PowExpr { Exponent: NumberExpr { Value.IsExactInteger: true } k } p && k.Value.IsPositiveReal)
                Add(p.Base, (int)k.Value.Re);
            else Add(f, 1);
        }
        factors.Sort(StringComparer.Ordinal);
        return (ExprPrinter.ToText(constant), factors);
    }

    public static string Join(IEnumerable<string> items)
    {
        var sb = new StringBuilder();
        foreach (var i in items)
        {
            if (sb.Length > 0) sb.Append(" | ");
            sb.Append(i);
        }
        return sb.ToString();
    }
}

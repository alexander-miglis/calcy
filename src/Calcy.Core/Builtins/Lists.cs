using System.Collections.Immutable;
using System.Numerics;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Core.Builtins;

public static partial class CoreBuiltins
{
    private const int MaxListLength = 1_000_000;

    private static void RegisterLists(FunctionRegistry r)
    {
        r.Add(Def("range", Range, "Lists", "range(a, b) or range(a, b, step)", "The list a, a + step, ..., up to b. [1...5] is range(1, 5).", "range(1, 10, 2)", min: 2, max: 3, listable: false));
        r.Add(Def("seq", Seq, "Lists", "seq(expr, k, a, b) or seq(expr, k, a, b, step)", "Builds a list by evaluating expr for k = a, ..., b.", "seq(k^2, k, 1, 5)", min: 4, max: 5, hold: true, listable: false));
        r.Add(Def("sum", c => SumProduct(c, product: false), "Lists", "sum(list) or sum(expr, k, a, b)", "Adds the items of a list, or the terms expr for k from a to b.", "sum(k^2, k, 1, 10)", min: 1, max: 4, hold: true, listable: false));
        r.Add(Def("product", c => SumProduct(c, product: true), "Lists", "product(list) or product(expr, k, a, b)", "Multiplies the items of a list, or the factors expr for k from a to b.", "product(k, k, 1, 5)", min: 1, max: 4, hold: true, listable: false, aliases: ["prod"]));
        r.Add(Def("len", c => c[0] switch
        {
            ListExpr l => Expr.Int(l.Count),
            StringExpr s => Expr.Int(s.Value.Length),
            TupleExpr t => Expr.Int(t.Items.Length),
            MatrixExpr m => Expr.Int(m.Rows),
            _ => null,
        }, "Lists", "len(list)", "Number of items in a list (or characters in text).", "len({4, 5, 6})", listable: false, aliases: ["length", "count"]));
        r.Add(Def("dim", c => c[0] switch
        {
            ListExpr l => Expr.Int(l.Count),
            MatrixExpr m => new ListExpr([Expr.Int(m.Rows), Expr.Int(m.Cols)]),
            _ => null,
        }, "Lists", "dim(list or matrix)", "Length of a list, or {rows, columns} of a matrix.", "dim([[1, 2, 3], [4, 5, 6]])", listable: false));
        r.Add(Def("sort", c => SortList(c, descending: false), "Lists", "sort(list)", "Sorts from smallest to largest.", "sort({3, 1, 2})", listable: false, aliases: ["sortA"]));
        r.Add(Def("sortd", c => SortList(c, descending: true), "Lists", "sortd(list)", "Sorts from largest to smallest.", "sortd({3, 1, 2})", listable: false, aliases: ["sortD"]));
        r.Add(Def("reverse", c => new ListExpr([.. ListArg(c, 0).Items.Reverse()]), "Lists", "reverse(list)", "Reverses the order.", "reverse({1, 2, 3})", listable: false));
        r.Add(Def("cumsum", c =>
        {
            var l = ListArg(c, 0);
            var acc = new List<Expr>();
            Expr total = Expr.Zero;
            foreach (var it in l.Items) acc.Add(total = Algebra.Add(total, it));
            return new ListExpr([.. acc]);
        }, "Lists", "cumsum(list)", "Running totals: cumsum({1, 2, 3}) = {1, 3, 6}.", "cumsum({1, 2, 3})", listable: false, aliases: ["cumSum"]));
        r.Add(Def("deltalist", c =>
        {
            var l = ListArg(c, 0);
            return new ListExpr([.. l.Items.Skip(1).Select((x, i) => Algebra.Sub(x, l.Items[i]))]);
        }, "Lists", "deltalist(list)", "Differences between neighbours: {1, 4, 9} gives {3, 5}.", "deltalist({1, 4, 9})", listable: false, aliases: ["deltaList", "ΔList"]));
        r.Add(Def("join", c =>
        {
            var items = new List<Expr>();
            foreach (var a in c.Args)
            {
                if (a is ListExpr l) items.AddRange(l.Items);
                else items.Add(a);
            }
            return new ListExpr([.. items]);
        }, "Lists", "join(list1, list2, ...)", "Joins lists end to end.", "join({1, 2}, {3})", min: 1, max: -1, listable: false));
        r.Add(Def("map", c =>
        {
            var l = ListArg(c, 1);
            return new ListExpr([.. l.Items.Select(x => Evaluator.Invoke(c[0], [x], c.Context))]);
        }, "Lists", "map(f, list)", "Applies a function to every item: map(x => x^2, {1, 2, 3}).", "map(x => x^2, {1, 2, 3})", min: 2, max: 2, listable: false));
        r.Add(Def("filter", c =>
        {
            var l = ListArg(c, 1);
            var kept = l.Items.Where(x => Evaluator.Invoke(c[0], [x], c.Context) is BoolExpr { Value: true });
            return new ListExpr([.. kept]);
        }, "Lists", "filter(test, list)", "Keeps items where the test is true: filter(x => x > 2, {1, 2, 3, 4}).", "filter(x => x > 2, {1, 2, 3, 4})", min: 2, max: 2, listable: false));
        r.Add(Def("min", c => MinMax(c, max: false), "Lists", "min(a, b, ...) or min(list)", "The smallest value.", "min({4, 2, 8})", min: 1, max: -1, listable: false));
        r.Add(Def("max", c => MinMax(c, max: true), "Lists", "max(a, b, ...) or max(list)", "The largest value.", "max(4, 2, 8)", min: 1, max: -1, listable: false));
        r.Add(Def("mean", c =>
        {
            var l = ListArg(c, 0);
            if (l.Count == 0) throw c.Error("the list is empty");
            return Algebra.Div(Algebra.Add(l.Items), Expr.Int(l.Count));
        }, "Statistics", "mean(list)", "Arithmetic mean (average). Exact for fractions.", "mean({1, 2, 4})", listable: false, aliases: ["avg", "average"]));
        r.Add(Def("median", Median, "Statistics", "median(list)", "The middle value of the sorted list.", "median({5, 1, 3, 2})", listable: false));
        r.Add(Def("variance", c => Variance(c, sample: true), "Statistics", "variance(list)", "Sample variance s² (divides by n - 1).", "variance({2, 4, 4, 4, 5, 5, 7, 9})", listable: false, aliases: ["var"]));
        r.Add(Def("stdev", c => { var v = Variance(c, sample: true); return v is null ? null : Algebra.Sqrt(v); }, "Statistics", "stdev(list)", "Sample standard deviation s (Sx).", "stdev({2, 4, 4, 4, 5, 5, 7, 9})", listable: false, aliases: ["stdDev", "sd", "Sx"]));
        r.Add(Def("pvariance", c => Variance(c, sample: false), "Statistics", "pvariance(list)", "Population variance σ² (divides by n).", "pvariance({2, 4, 4, 4, 5, 5, 7, 9})", listable: false, aliases: ["pvar"]));
        r.Add(Def("pstdev", c => { var v = Variance(c, sample: false); return v is null ? null : Algebra.Sqrt(v); }, "Statistics", "pstdev(list)", "Population standard deviation σ.", "pstdev({2, 4, 4, 4, 5, 5, 7, 9})", listable: false, aliases: ["σx"]));
    }

    private static Expr? Range(CallInfo c)
    {
        var a = NumberArg(c, 0);
        var b = NumberArg(c, 1);
        var step = c.Count > 2 ? NumberArg(c, 2) : (Number.CompareReal(a, b) <= 0 ? Number.One : Number.MinusOne);
        if (step.IsZero) throw c.Error("the step cannot be 0");
        if (!a.IsReal || !b.IsReal || !step.IsReal) throw c.Error("needs real numbers");
        var items = new List<Expr>();
        var x = a;
        bool up = step.IsPositiveReal;
        while (up ? Number.CompareReal(x, b) <= 0 : Number.CompareReal(x, b) >= 0)
        {
            items.Add(N(x));
            if (items.Count > MaxListLength) throw c.Error($"the list would have more than {MaxListLength:N0} items");
            x += step;
            if (!x.IsExact && Math.Abs(x.Re - b.Re) < 1e-12 * Math.Max(1, Math.Abs(b.Re))) { items.Add(N(b)); break; }
        }
        return new ListExpr([.. items]);
    }

    private static Expr? Seq(CallInfo c)
    {
        var var = c.Args[1] is SymbolExpr s ? s.Name : throw c.Error("the second argument must be the variable, for example seq(k^2, k, 1, 5)");
        var ctx = c.Context;
        var a = Evaluator.Eval(c.Args[2], ctx);
        var b = Evaluator.Eval(c.Args[3], ctx);
        var step = c.Count > 4 ? Evaluator.Eval(c.Args[4], ctx) : Expr.One;
        if (a is not NumberExpr an || b is not NumberExpr bn || step is not NumberExpr sn)
            throw c.Error("the start, end and step must be numbers");
        if (sn.Value.IsZero) throw c.Error("the step cannot be 0");
        var items = new List<Expr>();
        var x = an.Value;
        bool up = sn.Value.IsPositiveReal;
        while (up ? Number.CompareReal(x, bn.Value) <= 0 : Number.CompareReal(x, bn.Value) >= 0)
        {
            ctx.Cancellation.ThrowIfCancellationRequested();
            items.Add(Evaluator.Eval(c.Args[0], ctx.WithLocal(var, N(x))));
            if (items.Count > MaxListLength) throw c.Error($"the list would have more than {MaxListLength:N0} items");
            x += sn.Value;
        }
        return new ListExpr([.. items]);
    }

    private static Expr? SumProduct(CallInfo c, bool product)
    {
        var ctx = c.Context;
        if (c.Count == 1)
        {
            var v = Evaluator.Eval(c.Args[0], ctx);
            if (v is ListExpr l) return product ? Algebra.Mul(l.Items) : Algebra.Add(l.Items);
            if (v is MatrixExpr m) return product ? Algebra.Mul(m.Items) : Algebra.Add(m.Items);
            return new CallExpr(c.Function.Name, [v]);
        }
        if (c.Count != 4) throw c.Error($"use {c.Function.Name}(list) or {c.Function.Name}(expr, k, a, b)");
        var name = c.Args[1] is SymbolExpr s ? s.Name : throw c.Error("the second argument must be the index variable");
        var a = Evaluator.Eval(c.Args[2], ctx);
        var b = Evaluator.Eval(c.Args[3], ctx);
        var free = ctx.WithFree(name);
        if (a is NumberExpr { Value.IsInteger: true } an && b is NumberExpr { Value.IsInteger: true } bn)
        {
            var lo = an.Value.ToBigInteger();
            var hi = bn.Value.ToBigInteger();
            if (hi < lo) return product ? Expr.One : Expr.Zero;
            if (hi - lo <= 200_000)
            {
                var terms = new List<Expr>();
                for (var k = lo; k <= hi; k++)
                {
                    ctx.Cancellation.ThrowIfCancellationRequested();
                    terms.Add(Evaluator.Eval(c.Args[0], ctx.WithLocal(name, Z(k))));
                }
                return product ? Algebra.Mul(terms) : Algebra.Add(terms);
            }
        }
        // Infinite or symbolic bounds: hand over to the algebra engine or numeric summation when available.
        var symbolicName = product ? "symproduct" : "symsum";
        if (ctx.Functions.TryGet(symbolicName, out var sym))
        {
            var body = Evaluator.Eval(c.Args[0], free);
            var result = Evaluator.Apply(sym, [body, new SymbolExpr(name), a, b], ctx);
            if (result is not CallExpr { Name: var n } || n != symbolicName) return result;
        }
        return new CallExpr(c.Function.Name, [Evaluator.Eval(c.Args[0], free), new SymbolExpr(name), a, b]);
    }

    private static Expr? SortList(CallInfo c, bool descending)
    {
        var l = ListArg(c, 0);
        var keyed = l.Items.Select(x => (Item: x, Key: Evaluator.Numeric(x, c.Context))).ToList();
        if (keyed.Any(k => k.Key is null || !k.Key.Value.IsReal))
        {
            if (l.Items.All(x => x is StringExpr))
            {
                var strs = l.Items.Cast<StringExpr>().OrderBy(s => s.Value, StringComparer.Ordinal).ToList();
                if (descending) strs.Reverse();
                return new ListExpr([.. strs]);
            }
            throw c.Error("can only sort real numbers or text");
        }
        var sorted = keyed.OrderBy(k => k.Key!.Value.Re).Select(k => k.Item).ToList();
        if (descending) sorted.Reverse();
        return new ListExpr([.. sorted]);
    }

    private static Expr? MinMax(CallInfo c, bool max)
    {
        var items = c.Args.Count == 1 && c[0] is ListExpr l ? l.Items.ToList() : c.Args.ToList();
        if (items.Count == 0) throw c.Error("needs at least one value");
        if (c.Args.Count == 2 && c[0] is ListExpr la && c[1] is ListExpr lb && la.Count == lb.Count)
        {
            // Elementwise min/max of two lists (TI behaviour).
            return new ListExpr([.. la.Items.Zip(lb.Items, (x, y) => MinMax(new CallInfo(c.Function, [x, y], c.Context), max) ?? CallExpr.Of(c.Function.Name, x, y))]);
        }
        Expr? best = null;
        double bestV = 0;
        foreach (var it in items)
        {
            var v = Evaluator.Numeric(it, c.Context);
            if (v is null || !v.Value.IsReal) return null;
            double d = v.Value.Re;
            if (best is null || (max ? d > bestV : d < bestV))
            {
                best = it;
                bestV = d;
            }
        }
        return best;
    }

    private static Expr? Median(CallInfo c)
    {
        var l = ListArg(c, 0);
        if (l.Count == 0) throw c.Error("the list is empty");
        var sorted = (ListExpr)SortList(c, false)!;
        int n = sorted.Count;
        if (n % 2 == 1) return sorted.Items[n / 2];
        return Algebra.Div(Algebra.Add(sorted.Items[n / 2 - 1], sorted.Items[n / 2]), Expr.Two);
    }

    private static Expr? Variance(CallInfo c, bool sample)
    {
        var l = ListArg(c, 0);
        int n = l.Count;
        if (n < (sample ? 2 : 1)) throw c.Error(sample ? "needs at least 2 values" : "the list is empty");
        var mean = Algebra.Div(Algebra.Add(l.Items), Expr.Int(n));
        var squares = l.Items.Select(x => { var d = Algebra.Sub(x, mean); return Algebra.Mul(d, d); });
        return Algebra.Div(Algebra.Add(squares), Expr.Int(sample ? n - 1 : n));
    }
}

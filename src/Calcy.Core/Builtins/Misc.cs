using System.Globalization;
using System.Numerics;
using System.Text;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Core.Builtins;

public static partial class CoreBuiltins
{
    private static readonly object RandomGate = new();
    private static ulong _s0 = 0x9E3779B97F4A7C15UL, _s1 = 0xBF58476D1CE4E5B9UL, _s2 = 0x94D049BB133111EBUL, _s3 = 0x2545F4914F6CDD1DUL;

    private static void RegisterMisc(FunctionRegistry r)
    {
        r.Add(Def("if", If, "Logic", "if(condition, then, else)", "Chooses a value: if(x > 0, x, -x).", "if(3 > 2, 1, 0)", min: 2, max: 3, hold: true, listable: false));
        r.Add(Def("piecewise", Piecewise, "Logic", "piecewise(c1, v1, c2, v2, ..., otherwise)", "The first value whose condition holds.", "piecewise(x < 0, -x, x)", min: 2, max: -1, hold: true, listable: false));
        r.Add(Def("approx", c => Evaluator.Eval(c[0], c.Context.WithApproximate()), "Numbers", "approx(x)", "Decimal value of an exact result.", "approx(sqrt(2))", listable: false, aliases: ["N", "dec", "decimal"]));
        r.Add(Def("exact", c => Evaluator.Eval(ToExact(c[0]), c.Context.WithApproximate(false)), "Numbers", "exact(x)", "Converts decimals to fractions: exact(0.75) = 3/4.", "exact(0.125)", listable: false, aliases: ["rational", "tofrac"]));
        r.Add(Def("simplify", c => c[0], "Algebra", "simplify(expr)", "Simplifies an expression.", "simplify((x^2 - 1)/(x - 1))", listable: false));
        r.Add(Def("subst", Subst, "Algebra", "subst(expr, x, value) or subst(expr, x = value)", "Replaces a variable by a value.", "subst(x^2 + 1, x, 3)", min: 2, max: 3, hold: true, listable: false, aliases: ["subs", "with"]));
        r.Add(Def("rand", Rand, "Random", "rand() or rand(n)", "Random decimal between 0 and 1 (or a list of n of them).", "rand()", min: 0, max: 1, listable: false));
        r.Add(Def("randint", RandInt, "Random", "randint(a, b) or randint(a, b, n)", "Random whole number from a to b inclusive.", "randint(1, 6)", min: 2, max: 3, listable: false, aliases: ["randInt"]));
        r.Add(Def("seed", c =>
        {
            Seed((ulong)(long)IntegerArg(c, 0));
            return new StringExpr("seeded");
        }, "Random", "seed(n)", "Makes random numbers repeatable.", "seed(42)", listable: false));
        r.Add(Def("tobase", ToBase, "Programmer", "tobase(n, b)", "Writes a whole number in base b (2 to 36) as text.", "tobase(255, 16)", min: 2, max: 2, aliases: ["base"]));
        r.Add(Def("hex", c => ToBase(new CallInfo(c.Function, [c[0], Expr.Int(16)], c.Context)), "Programmer", "hex(n)", "Hexadecimal text.", "hex(255)"));
        r.Add(Def("bin", c => ToBase(new CallInfo(c.Function, [c[0], Expr.Int(2)], c.Context)), "Programmer", "bin(n)", "Binary text.", "bin(10)"));
        r.Add(Def("oct", c => ToBase(new CallInfo(c.Function, [c[0], Expr.Int(8)], c.Context)), "Programmer", "oct(n)", "Octal text.", "oct(64)"));
        r.Add(Def("frombase", FromBase, "Programmer", "frombase(text, b)", "Reads a number written in base b.", "frombase(\"ff\", 16)", min: 2, max: 2));
        r.Add(Def("string", c => new StringExpr(Printing.ExprPrinter.ToText(c[0])), "Text", "string(x)", "Turns a value into text.", "string(1/3)", listable: false, aliases: ["str"]));
        r.Add(Def("fderiv", FDeriv, "Calculus", "f'(x)", "Derivative of a user function: f'(2).", "f'(x)", min: 2, max: -1, listable: false, hiddenDef: true));
    }

    private static FunctionDefinition Def(string name, Func<CallInfo, Expr?> eval, string category, string signature, string description, string example,
        int min, int max, bool listable, bool hiddenDef)
    {
        var d = Def(name, eval, category, signature, description, example, min: min, max: max, listable: listable);
        return new FunctionDefinition
        {
            Name = d.Name, Evaluate = d.Evaluate, Category = d.Category, Signature = d.Signature, Description = d.Description,
            Example = d.Example, MinArgs = d.MinArgs, MaxArgs = d.MaxArgs, Listable = d.Listable, Hidden = hiddenDef,
        };
    }

    private static Expr? If(CallInfo c)
    {
        var cond = c.Eval(0);
        if (cond is BoolExpr b)
        {
            if (b.Value) return c.Eval(1);
            return c.Count > 2 ? c.Eval(2) : ConstantExpr.Undefined;
        }
        var then = c.Eval(1);
        return new PiecewiseExpr([(cond, then)], c.Count > 2 ? c.Eval(2) : null);
    }

    private static Expr? Piecewise(CallInfo c)
    {
        var cases = new List<(Expr, Expr)>();
        int i = 0;
        for (; i + 1 < c.Count; i += 2) cases.Add((c.Args[i], c.Args[i + 1]));
        Expr? otherwise = i < c.Count ? c.Args[i] : null;
        return Evaluator.Eval(new PiecewiseExpr([.. cases], otherwise), c.Context);
    }

    private static Expr ToExact(Expr e) => e.Map(x => x is NumberExpr { Value.IsExact: false } n && n.Value.IsFinite
        ? new NumberExpr(Number.Exact(BigRational.Approximate(n.Value.Re), BigRational.Approximate(n.Value.Im)))
        : x);

    private static Expr? Subst(CallInfo c)
    {
        string name;
        Expr value;
        if (c.Count == 2 && c.Args[1] is RelationExpr { Op: RelOp.Equal, Left: SymbolExpr s } rel)
        {
            name = s.Name;
            value = Evaluator.Eval(rel.Right, c.Context);
        }
        else if (c.Count == 3 && c.Args[1] is SymbolExpr s2)
        {
            name = s2.Name;
            value = Evaluator.Eval(c.Args[2], c.Context);
        }
        else throw c.Error("use subst(expr, x, value) or subst(expr, x = value)");
        return Evaluator.Eval(c.Args[0], c.Context.WithLocal(name, value));
    }

    private static ulong NextRandom()
    {
        lock (RandomGate)
        {
            // xoshiro256**
            ulong result = BitOperations.RotateLeft(_s1 * 5, 7) * 9;
            ulong t = _s1 << 17;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = BitOperations.RotateLeft(_s3, 45);
            return result;
        }
    }

    private static void Seed(ulong seed)
    {
        lock (RandomGate)
        {
            ulong z = seed;
            ulong SplitMix()
            {
                z += 0x9E3779B97F4A7C15UL;
                ulong r = z;
                r = (r ^ (r >> 30)) * 0xBF58476D1CE4E5B9UL;
                r = (r ^ (r >> 27)) * 0x94D049BB133111EBUL;
                return r ^ (r >> 31);
            }
            _s0 = SplitMix();
            _s1 = SplitMix();
            _s2 = SplitMix();
            _s3 = SplitMix();
        }
    }

    private static double NextDouble() => (NextRandom() >> 11) * (1.0 / (1UL << 53));

    private static Expr? Rand(CallInfo c)
    {
        if (c.Count == 0) return N(NextDouble());
        int n = SmallIntArg(c, 0, 1, MaxListLength);
        return new ListExpr([.. Enumerable.Range(0, n).Select(_ => (Expr)N(NextDouble()))]);
    }

    private static Expr? RandInt(CallInfo c)
    {
        var lo = IntegerArg(c, 0);
        var hi = IntegerArg(c, 1);
        if (hi < lo) (lo, hi) = (hi, lo);
        var span = hi - lo + 1;
        Expr One()
        {
            if (span <= long.MaxValue)
            {
                ulong s = (ulong)(long)span;
                ulong limit = ulong.MaxValue - ulong.MaxValue % s;
                ulong v;
                do v = NextRandom(); while (v >= limit);
                return Z(lo + (v % s));
            }
            return Z(lo + new BigInteger(NextDouble() * (double)span));
        }
        if (c.Count == 2) return One();
        int n = SmallIntArg(c, 2, 1, MaxListLength);
        return new ListExpr([.. Enumerable.Range(0, n).Select(_ => One())]);
    }

    private static Expr? ToBase(CallInfo c)
    {
        var n = IntegerArg(c, 0);
        int b = SmallIntArg(c, 1, 2, 36);
        if (n.IsZero) return new StringExpr("0");
        const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var sb = new StringBuilder();
        var v = BigInteger.Abs(n);
        while (!v.IsZero)
        {
            sb.Insert(0, digits[(int)(v % b)]);
            v /= b;
        }
        if (n.Sign < 0) sb.Insert(0, '-');
        return new StringExpr(sb.ToString());
    }

    private static Expr? FromBase(CallInfo c)
    {
        if (c[0] is not StringExpr s) throw c.Error("the first argument must be text, for example \"ff\"");
        int b = SmallIntArg(c, 1, 2, 36);
        var text = s.Value.Trim().Replace("_", "").Replace(" ", "");
        bool neg = text.StartsWith('-');
        if (neg) text = text[1..];
        BigInteger v = 0;
        foreach (var ch in text.ToUpperInvariant())
        {
            int d = char.IsDigit(ch) ? ch - '0' : ch >= 'A' && ch <= 'Z' ? ch - 'A' + 10 : 99;
            if (d >= b) throw c.Error($"'{ch}' is not a digit in base {b}");
            v = v * b + d;
        }
        return Z(neg ? -v : v);
    }

    private static Expr? FDeriv(CallInfo c)
    {
        if (c[0] is not SymbolExpr f) throw c.Error("expected a function name");
        int order = SmallIntArg(c, 1, 1, 20);
        if (!c.Context.Functions.TryGet("diff", out var diff))
            throw new CalcException("Derivatives need the algebra engine, which is not loaded");
        var args = c.Args.Skip(2).ToList();
        string var;
        Expr body;
        if (c.Context.Env.TryGetFunction(f.Name, out var uf))
        {
            if (uf.Parameters.Length != 1) throw c.Error($"{f.Name}' needs a function of one variable");
            var = uf.Parameters[0];
            body = Evaluator.Eval(uf.Body, c.Context.WithOnlyLocals([new(var, new SymbolExpr(var))]));
        }
        else if (c.Context.Functions.TryGet(f.Name, out var builtin))
        {
            var = "x";
            body = Evaluator.Apply(builtin, [new SymbolExpr(var)], c.Context.WithFree(var));
        }
        else throw c.Error($"'{f.Name}' is not a function");
        var derivative = Evaluator.Apply(diff, [body, new SymbolExpr(var), Expr.Int(order)], c.Context.WithFree(var));
        if (args.Count == 0) return derivative;
        if (args.Count != 1) throw c.Error($"{f.Name}' takes one argument");
        return Evaluator.Eval(derivative, c.Context.WithLocal(var, args[0]));
    }
}

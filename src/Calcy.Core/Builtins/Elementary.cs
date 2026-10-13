using System.Numerics;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Core.Builtins;

public static partial class CoreBuiltins
{
    private static void RegisterElementary(FunctionRegistry r)
    {
        r.Add(Def("sqrt", c => Algebra.Sqrt(c[0]), "Powers and roots", "sqrt(x)", "Square root. √8 = 2√2, √(-4) = 2i.", "sqrt(8)", real1: Math.Sqrt));
        r.Add(Def("cbrt", c => Algebra.Pow(c[0], Expr.Rational(1, 3)), "Powers and roots", "cbrt(x)", "Cube root (real for negative numbers: ∛(-8) = -2).", "cbrt(-8)", real1: Math.Cbrt));
        r.Add(Def("root", c => Algebra.Pow(c[0], Algebra.Pow(c[1], Expr.MinusOne)), "Powers and roots", "root(x, n)", "The n-th root of x. Odd roots of negative numbers are real.", "root(32, 5)", min: 2, max: 2,
            real2: (x, n) => n % 2 == 1 && x < 0 ? -Math.Pow(-x, 1 / n) : Math.Pow(x, 1 / n)));
        r.Add(Def("exp", c => Algebra.Pow(ConstantExpr.E, c[0]), "Exponentials and logs", "exp(x)", "e to the power x.", "exp(1)", real1: Math.Exp));
        r.Add(Def("ln", Ln, "Exponentials and logs", "ln(x)", "Natural logarithm (base e).", "ln(e^3)", real1: Math.Log));
        r.Add(Def("log", Log, "Exponentials and logs", "log(x) or log(x, b)", "Logarithm base 10, or base b when given. log(1000) = 3, log(8, 2) = 3.", "log(8, 2)", min: 1, max: 2,
            aliases: ["log10"], real1: Math.Log10, real2: (x, b) => Math.Log(x) / Math.Log(b)));
        r.Add(Def("log2", c => Log(new CallInfo(c.Function, [c[0], Expr.Two], c.Context)), "Exponentials and logs", "log2(x)", "Logarithm base 2.", "log2(1024)", real1: Math.Log2));
        r.Add(Def("abs", Abs, "Numbers", "abs(x) or |x|", "Absolute value (distance from 0). For complex numbers, the modulus.", "abs(3 + 4i)", real1: Math.Abs));
        r.Add(Def("sign", c => Sign(c), "Numbers", "sign(x)", "-1, 0 or 1 depending on the sign of x.", "sign(-3)", aliases: ["sgn"], real1: x => Math.Sign(x)));
        r.Add(Def("floor", c => Rounding(c, q => q.Floor(), Math.Floor), "Numbers", "floor(x)", "Largest whole number ≤ x.", "floor(-2.5)", aliases: ["int"], real1: Math.Floor));
        r.Add(Def("ceil", c => Rounding(c, q => q.Ceiling(), Math.Ceiling), "Numbers", "ceil(x)", "Smallest whole number ≥ x.", "ceil(2.1)", aliases: ["ceiling"], real1: Math.Ceiling));
        r.Add(Def("trunc", c => Rounding(c, q => q.Truncate(), Math.Truncate), "Numbers", "trunc(x)", "Removes the fractional part (rounds toward 0).", "trunc(-2.7)", aliases: ["ipart", "iPart"], real1: Math.Truncate));
        r.Add(Def("fpart", FPart, "Numbers", "fpart(x)", "Fractional part with the sign of x: fpart(-2.75) = -0.75.", "fpart(7/4)", aliases: ["fPart"], real1: x => x - Math.Truncate(x)));
        r.Add(Def("round", Round, "Numbers", "round(x) or round(x, n)", "Rounds to n decimals (half away from zero).", "round(2.345, 2)", min: 1, max: 2,
            real1: x => Math.Round(x, MidpointRounding.AwayFromZero)));
        r.Add(Def("percent", c => Algebra.Div(c[0], Expr.Int(100)), "Numbers", "x%", "Percent: 15% = 15/100.", "15%", real1: x => x / 100, hiddenName: true));
        r.Add(Def("deg", Deg, "Angles", "x°", "Marks an angle in degrees: 30° is π/6 in radian mode.", "sin(30°)", real1: x => x * Math.PI / 180, hiddenName: true));
        r.Add(Def("rad", c => c.Context.Settings.Angle switch
        {
            AngleMode.Degree => Algebra.Div(Algebra.Mul(c[0], Expr.Int(180)), ConstantExpr.Pi),
            AngleMode.Gradian => Algebra.Div(Algebra.Mul(c[0], Expr.Int(200)), ConstantExpr.Pi),
            _ => c[0],
        }, "Angles", "rad(x)", "Marks an angle in radians, converting it to the current angle unit.", "rad(pi/2)"));
        r.Add(Def("re", c => Part(c, true), "Complex numbers", "re(z)", "Real part.", "re(3 + 4i)", aliases: ["real"], real1: x => x));
        r.Add(Def("im", c => Part(c, false), "Complex numbers", "im(z)", "Imaginary part.", "im(3 + 4i)", aliases: ["imag"], real1: _ => 0));
        r.Add(Def("conj", c => c[0] is NumberExpr n ? N(n.Value.Conjugate()) : c[0].IsConstantValue() && !c[0].ToString().Contains('i') ? c[0] : null,
            "Complex numbers", "conj(z)", "Complex conjugate: conj(3 + 4i) = 3 - 4i.", "conj(3 + 4i)", aliases: ["conjugate"], real1: x => x));
        r.Add(Def("arg", Arg, "Complex numbers", "arg(z)", "Argument (angle) of a complex number, in the current angle unit.", "arg(1 + i)", aliases: ["angle"], angleResult: true,
            real1: x => x >= 0 ? 0 : Math.PI));
    }

    private static FunctionDefinition Def(string name, Func<CallInfo, Expr?> eval, string category, string signature, string description, string example,
        Func<double, double> real1, bool hiddenName)
    {
        var d = Def(name, eval, category, signature, description, example, real1: real1);
        return new FunctionDefinition
        {
            Name = d.Name, Evaluate = d.Evaluate, Category = d.Category, Signature = d.Signature, Description = d.Description,
            Example = d.Example, Real1 = d.Real1, Hidden = hiddenName,
        };
    }

    private static Expr? Ln(CallInfo c)
    {
        var x = c[0];
        if (WantsNumeric(c, x)) return NumericUnary(((NumberExpr)x).Value, Math.Log, ComplexMath.Log, v => v >= 0);
        if (x is NumberExpr n)
        {
            if (n.Value.IsOne) return Expr.Zero;
            if (n.Value.IsZero) throw new CalcException("ln(0) is undefined (ln x goes to -∞ as x goes to 0)");
            if (n.Value.IsNegativeReal)
                return Algebra.Add(CallExpr.Of("ln", N(-n.Value)), Algebra.Mul(Expr.ImaginaryUnit, ConstantExpr.Pi));
            return null;
        }
        if (x is ConstantExpr { Constant: ConstantKind.E }) return Expr.One;
        if (x is PowExpr { Base: ConstantExpr { Constant: ConstantKind.E } } p && IsRealValued(p.Exponent)) return p.Exponent;
        if (x is ConstantExpr { Constant: ConstantKind.Infinity }) return ConstantExpr.Infinity;
        return null;
    }

    private static bool IsRealValued(Expr e) => e switch
    {
        NumberExpr n => n.Value.IsReal,
        SymbolExpr => true,
        ConstantExpr => true,
        AddExpr or MulExpr => e.Children.All(IsRealValued),
        _ => false,
    };

    private static Expr? Log(CallInfo c)
    {
        var x = c[0];
        Expr b = c.Count > 1 ? c[1] : Expr.Int(10);
        if (b is NumberExpr bn && (bn.Value.IsOne || bn.Value.IsZero || bn.Value.IsNegativeReal))
            throw c.Error("the base must be positive and not 1");
        if (x is NumberExpr xn && b is NumberExpr bv && (!xn.Value.IsExact || !bv.Value.IsExact || c.Approximate))
        {
            var lx = NumericUnary(xn.Value, Math.Log, ComplexMath.Log, v => v >= 0);
            var lb = NumericUnary(bv.Value, Math.Log, ComplexMath.Log, v => v >= 0);
            return N(((NumberExpr)lx).Value / ((NumberExpr)lb).Value);
        }
        if (x is NumberExpr xe && xe.Value.IsOne) return Expr.Zero;
        if (x is NumberExpr { Value.IsZero: true }) throw new CalcException("log(0) is undefined (log x goes to -∞ as x goes to 0)");
        if (x.Equals(b)) return Expr.One;
        // Exact powers: log(1000) = 3, log(8, 4) = 3/2, log(1/9, 3) = -2.
        if (x is NumberExpr { Value.IsExactRational: true } xr && b is NumberExpr { Value.IsExactRational: true } br && xr.Value.IsPositiveReal)
        {
            var k = ExactLog(xr.Value.ReQ, br.Value.ReQ);
            if (k is not null) return Q(k.Value);
        }
        if (x is PowExpr px && px.Base.Equals(b)) return px.Exponent;
        if (b is ConstantExpr { Constant: ConstantKind.E }) return CallExpr.Of("ln", x);
        return null;
    }

    /// <summary>Finds a rational k with small denominator such that b^k = x exactly.</summary>
    private static BigRational? ExactLog(BigRational x, BigRational b)
    {
        if (x.IsOne) return BigRational.Zero;
        double guess = Math.Log(x.ToDouble()) / Math.Log(b.ToDouble());
        if (double.IsNaN(guess) || double.IsInfinity(guess) || Math.Abs(guess) > 10_000) return null;
        for (int den = 1; den <= 12; den++)
        {
            var num = Math.Round(guess * den);
            if (Math.Abs(num / den - guess) > 1e-9 * Math.Max(1, Math.Abs(guess))) continue;
            var k = new BigRational(new BigInteger(num), den);
            // Verify: b^num == x^den exactly.
            var lhs = b.Pow((int)num);
            var rhs = x.Pow(den);
            if (lhs == rhs) return k;
        }
        return null;
    }

    private static Expr? Abs(CallInfo c)
    {
        var x = c[0];
        if (x is NumberExpr n)
        {
            var v = n.Value;
            if (v.IsReal) return N(v.IsNegativeReal ? -v : v);
            if (!v.IsExact || c.Approximate) return N(Number.Approx(v.AbsDouble()));
            return Algebra.Sqrt(N(v.AbsSquared()));
        }
        if (x is ConstantExpr { Constant: ConstantKind.Pi or ConstantKind.E }) return x;
        if (x is ConstantExpr { IsInfinite: true }) return ConstantExpr.Infinity;
        if (x is MulExpr m && m.Factors[0] is NumberExpr coef)
        {
            var rest = m.Factors.Length == 2 ? m.Factors[1] : new MulExpr(m.Factors.RemoveAt(0));
            var absCoef = coef.Value.IsReal ? (coef.Value.IsNegativeReal ? -coef.Value : coef.Value) : Number.Approx(coef.Value.AbsDouble());
            return Algebra.Mul(N(absCoef), Abs(new CallInfo(c.Function, [rest], c.Context)) ?? CallExpr.Of("abs", rest));
        }
        if (x is PowExpr { Exponent: NumberExpr { Value.IsExactInteger: true } e } p && e.Value.ToBigInteger().IsEven && IsRealValued(p.Base)) return x;
        if (x is CallExpr { Name: "abs" }) return x;
        if (x.IsConstantValue())
        {
            var v = Evaluator.Numeric(x, c.Context);
            if (v is { IsReal: true }) return v.Value.Re >= 0 ? x : Algebra.Neg(x);
        }
        return null;
    }

    private static Expr? Sign(CallInfo c)
    {
        var x = c[0];
        if (x is NumberExpr n)
        {
            if (!n.Value.IsReal)
            {
                double m = n.Value.AbsDouble();
                return N(Number.Approx(n.Value.Re / m, n.Value.Im / m));
            }
            return Expr.Int(n.Value.RealSign);
        }
        if (x.IsConstantValue())
        {
            var v = Evaluator.Numeric(x, c.Context);
            if (v is { IsReal: true }) return Expr.Int(Math.Sign(v.Value.Re));
        }
        return null;
    }

    private static Expr? Rounding(CallInfo c, Func<BigRational, BigInteger> exact, Func<double, double> approx)
    {
        var x = c[0];
        if (x is NumberExpr n)
        {
            if (!n.Value.IsReal) throw c.Error("needs a real number");
            if (n.Value.IsExact) return Z(exact(n.Value.ReQ));
            return N(approx(n.Value.Re));
        }
        if (x.IsConstantValue())
        {
            var v = Evaluator.Numeric(x, c.Context);
            if (v is { IsReal: true })
            {
                // Exact constant like π: decide from a high-confidence numeric value.
                double d = approx(v.Value.Re);
                return Expr.Int((long)d);
            }
        }
        return null;
    }

    private static Expr? FPart(CallInfo c)
    {
        if (c[0] is NumberExpr n && n.Value.IsReal)
            return n.Value.IsExact ? Q(n.Value.ReQ.FractionalPart()) : N(n.Value.Re - Math.Truncate(n.Value.Re));
        return null;
    }

    private static Expr? Round(CallInfo c)
    {
        int digits = c.Count > 1 ? SmallIntArg(c, 1, -100, 100) : 0;
        var x = c[0];
        if (x is not NumberExpr && x.IsConstantValue())
        {
            var v = Evaluator.Numeric(x, c.Context);
            if (v is null) return null;
            x = N(v.Value);
        }
        if (x is not NumberExpr n) return null;
        if (!n.Value.IsReal) throw c.Error("needs a real number");
        var q = n.Value.IsExact ? n.Value.ReQ : BigRational.FromDouble(n.Value.Re);
        var scale = digits >= 0 ? new BigRational(BigInteger.Pow(10, digits)) : new BigRational(BigInteger.One, BigInteger.Pow(10, -digits));
        var rounded = new BigRational((q * scale).RoundHalfAwayFromZero()) / scale;
        return n.Value.IsExact ? Q(rounded) : N(rounded.ToDouble());
    }

    private static Expr? Deg(CallInfo c)
    {
        return c.Context.Settings.Angle switch
        {
            AngleMode.Degree => c[0],
            AngleMode.Gradian => Algebra.Mul(c[0], Expr.Rational(10, 9)),
            _ => Algebra.Div(Algebra.Mul(c[0], ConstantExpr.Pi), Expr.Int(180)),
        };
    }

    private static Expr? Part(CallInfo c, bool real)
    {
        var x = c[0];
        if (x is NumberExpr n)
        {
            if (n.Value.IsExact) return Q(real ? n.Value.ReQ : n.Value.ImQ);
            return N(real ? n.Value.Re : n.Value.Im);
        }
        if (x is ConstantExpr or SymbolExpr && IsRealValued(x)) return real ? x : Expr.Zero;
        return null;
    }

    private static Expr? Arg(CallInfo c)
    {
        if (c[0] is not NumberExpr n) return null;
        var v = n.Value;
        if (v.IsZero) throw c.Error("arg(0) is undefined");
        Expr radians;
        if (v.IsExact)
        {
            // Exact angles for numbers on the axes and diagonals.
            var re = v.ReQ;
            var im = v.ImQ;
            if (im.IsZero) radians = re.IsPositive ? Expr.Zero : ConstantExpr.Pi;
            else if (re.IsZero) radians = Algebra.Mul(Expr.Rational(im.IsPositive ? 1 : -1, 2), ConstantExpr.Pi);
            else if (re.Abs() == im.Abs())
            {
                int quarter = (re.IsPositive, im.IsPositive) switch { (true, true) => 1, (false, true) => 3, (false, false) => -3, _ => -1 };
                radians = Algebra.Mul(Expr.Rational(quarter, 4), ConstantExpr.Pi);
            }
            else if (!c.Approximate) return null;
            else radians = N(v.ArgDouble());
        }
        else radians = N(v.ArgDouble());
        return FromRadians(radians, c.Context);
    }

    /// <summary>Converts a radian value to the current angle unit.</summary>
    internal static Expr FromRadians(Expr radians, EvalContext ctx) => ctx.Settings.Angle switch
    {
        AngleMode.Degree => Algebra.Div(Algebra.Mul(radians, Expr.Int(180)), ctx.Approximate ? N(Math.PI) : ConstantExpr.Pi),
        AngleMode.Gradian => Algebra.Div(Algebra.Mul(radians, Expr.Int(200)), ctx.Approximate ? N(Math.PI) : ConstantExpr.Pi),
        _ => radians,
    };
}

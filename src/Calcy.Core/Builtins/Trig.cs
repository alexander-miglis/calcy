using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Core.Builtins;

public static partial class CoreBuiltins
{
    // sin(nπ/12) for n = 0..6, and tan(nπ/12) for n = 0..5 (exact forms).
    private static readonly Lazy<Expr[]> SinTable = new(() =>
    [
        Expr.Zero,
        Algebra.Div(Algebra.Sub(Algebra.Sqrt(Expr.Int(6)), Algebra.Sqrt(Expr.Int(2))), Expr.Int(4)),
        Expr.HalfExpr,
        Algebra.Div(Algebra.Sqrt(Expr.Int(2)), Expr.Int(2)),
        Algebra.Div(Algebra.Sqrt(Expr.Int(3)), Expr.Int(2)),
        Algebra.Div(Algebra.Add(Algebra.Sqrt(Expr.Int(6)), Algebra.Sqrt(Expr.Int(2))), Expr.Int(4)),
        Expr.One,
    ]);

    private static readonly Lazy<Expr[]> TanTable = new(() =>
    [
        Expr.Zero,
        Algebra.Sub(Expr.Two, Algebra.Sqrt(Expr.Int(3))),
        Algebra.Div(Algebra.Sqrt(Expr.Int(3)), Expr.Int(3)),
        Expr.One,
        Algebra.Sqrt(Expr.Int(3)),
        Algebra.Add(Expr.Two, Algebra.Sqrt(Expr.Int(3))),
    ]);

    private static void RegisterTrig(FunctionRegistry r)
    {
        r.Add(Def("sin", c => TrigCall(c, TrigKind.Sin), "Trigonometry", "sin(x)", "Sine. Uses the current angle unit (RAD or DEG).", "sin(pi/6)", real1: Math.Sin, angleArg: true));
        r.Add(Def("cos", c => TrigCall(c, TrigKind.Cos), "Trigonometry", "cos(x)", "Cosine.", "cos(pi/3)", real1: Math.Cos, angleArg: true));
        r.Add(Def("tan", c => TrigCall(c, TrigKind.Tan), "Trigonometry", "tan(x)", "Tangent.", "tan(pi/4)", real1: Math.Tan, angleArg: true));
        r.Add(Def("sec", c => TrigCall(c, TrigKind.Sec), "Trigonometry", "sec(x)", "Secant, 1/cos(x).", "sec(pi/3)", real1: x => 1 / Math.Cos(x), angleArg: true));
        r.Add(Def("csc", c => TrigCall(c, TrigKind.Csc), "Trigonometry", "csc(x)", "Cosecant, 1/sin(x).", "csc(pi/6)", real1: x => 1 / Math.Sin(x), angleArg: true, aliases: ["cosec"]));
        r.Add(Def("cot", c => TrigCall(c, TrigKind.Cot), "Trigonometry", "cot(x)", "Cotangent, 1/tan(x).", "cot(pi/4)", real1: x => 1 / Math.Tan(x), angleArg: true));
        r.Add(Def("asin", c => InverseTrig(c, TrigKind.Sin), "Trigonometry", "asin(x)", "Inverse sine (sin⁻¹), result in the current angle unit.", "asin(1/2)", aliases: ["arcsin"], real1: Math.Asin, angleResult: true));
        r.Add(Def("acos", c => InverseTrig(c, TrigKind.Cos), "Trigonometry", "acos(x)", "Inverse cosine (cos⁻¹).", "acos(0)", aliases: ["arccos"], real1: Math.Acos, angleResult: true));
        r.Add(Def("atan", c => InverseTrig(c, TrigKind.Tan), "Trigonometry", "atan(x)", "Inverse tangent (tan⁻¹).", "atan(1)", aliases: ["arctan"], real1: Math.Atan, angleResult: true));
        r.Add(Def("acot", c => c[0] is NumberExpr { Value.IsZero: true } ? FromRadians(Algebra.Mul(Expr.HalfExpr, ConstantExpr.Pi), c.Context) : InverseTrig(new CallInfo(c.Function, [Algebra.Pow(c[0], Expr.MinusOne)], c.Context), TrigKind.Tan),
            "Trigonometry", "acot(x)", "Inverse cotangent.", "acot(1)", aliases: ["arccot"], real1: x => Math.Atan(1 / x), angleResult: true));
        r.Add(Def("asec", c => InverseTrig(new CallInfo(c.Function, [Algebra.Pow(c[0], Expr.MinusOne)], c.Context), TrigKind.Cos),
            "Trigonometry", "asec(x)", "Inverse secant.", "asec(2)", aliases: ["arcsec"], real1: x => Math.Acos(1 / x), angleResult: true));
        r.Add(Def("acsc", c => InverseTrig(new CallInfo(c.Function, [Algebra.Pow(c[0], Expr.MinusOne)], c.Context), TrigKind.Sin),
            "Trigonometry", "acsc(x)", "Inverse cosecant.", "acsc(2)", aliases: ["arccsc"], real1: x => Math.Asin(1 / x), angleResult: true));
        r.Add(Def("atan2", Atan2, "Trigonometry", "atan2(y, x)", "Angle of the point (x, y) from the positive x-axis, in (-π, π].", "atan2(1, -1)", min: 2, max: 2,
            real2: Math.Atan2, angleResult: true));

        r.Add(Def("sinh", c => Hyperbolic(c, Math.Sinh, ComplexMath.Sinh, odd: true), "Hyperbolic", "sinh(x)", "Hyperbolic sine.", "sinh(0)", real1: Math.Sinh));
        r.Add(Def("cosh", c => Hyperbolic(c, Math.Cosh, ComplexMath.Cosh, odd: false, atZero: Expr.One), "Hyperbolic", "cosh(x)", "Hyperbolic cosine.", "cosh(0)", real1: Math.Cosh));
        r.Add(Def("tanh", c => Hyperbolic(c, Math.Tanh, ComplexMath.Tanh, odd: true), "Hyperbolic", "tanh(x)", "Hyperbolic tangent.", "tanh(1)", real1: Math.Tanh));
        r.Add(Def("sech", c => Recip(Hyperbolic(c, Math.Cosh, ComplexMath.Cosh, odd: false, atZero: Expr.One), "cosh", c), "Hyperbolic", "sech(x)", "1/cosh(x).", "sech(0)", real1: x => 1 / Math.Cosh(x)));
        r.Add(Def("csch", c => Recip(Hyperbolic(c, Math.Sinh, ComplexMath.Sinh, odd: true), "sinh", c), "Hyperbolic", "csch(x)", "1/sinh(x).", "csch(1)", real1: x => 1 / Math.Sinh(x)));
        r.Add(Def("coth", c => Recip(Hyperbolic(c, Math.Tanh, ComplexMath.Tanh, odd: true), "tanh", c), "Hyperbolic", "coth(x)", "1/tanh(x).", "coth(1)", real1: x => 1 / Math.Tanh(x)));
        r.Add(Def("asinh", c => Hyperbolic(c, Math.Asinh, ComplexMath.Asinh, odd: true), "Hyperbolic", "asinh(x)", "Inverse hyperbolic sine.", "asinh(1)", aliases: ["arsinh", "arcsinh"], real1: Math.Asinh));
        r.Add(Def("acosh", c => Hyperbolic(c, Math.Acosh, ComplexMath.Acosh, odd: false, atZero: null, domain: x => x >= 1, atOne: Expr.Zero), "Hyperbolic", "acosh(x)", "Inverse hyperbolic cosine.", "acosh(2)", aliases: ["arcosh", "arccosh"], real1: Math.Acosh));
        r.Add(Def("atanh", c => Hyperbolic(c, Math.Atanh, ComplexMath.Atanh, odd: true, domain: x => Math.Abs(x) < 1), "Hyperbolic", "atanh(x)", "Inverse hyperbolic tangent.", "atanh(0.5)", aliases: ["artanh", "arctanh"], real1: Math.Atanh));
    }

    private enum TrigKind { Sin, Cos, Tan, Sec, Csc, Cot }

    private static Expr? Recip(Expr? value, string name, CallInfo c) =>
        value is null ? null : value is CallExpr { Name: var n } && n == name ? null : Algebra.Pow(value, Expr.MinusOne);

    private static Expr? TrigCall(CallInfo c, TrigKind kind)
    {
        var x = c[0];
        var settings = c.Context.Settings;
        if (WantsNumeric(c, x))
        {
            var v = ((NumberExpr)x).Value;
            var rad = v.IsReal ? Number.Approx(v.Re * settings.ToRadians) : v.ToApprox() * Number.Approx(settings.ToRadians);
            if (v.IsReal)
            {
                double t = rad.Re;
                // Snap results that are zero up to rounding (sin(π) should print 0, not 1.2e-16).
                double result = kind switch
                {
                    TrigKind.Sin => Math.Sin(t),
                    TrigKind.Cos => Math.Cos(t),
                    TrigKind.Tan => Math.Tan(t),
                    TrigKind.Sec => 1 / Math.Cos(t),
                    TrigKind.Csc => 1 / Math.Sin(t),
                    _ => 1 / Math.Tan(t),
                };
                if (Math.Abs(result) < 1e-15 && Math.Abs(t) > 1e-8) result = 0;
                if (Math.Abs(result) > 1e15) throw new CalcException($"{kind.ToString().ToLowerInvariant()} is undefined at this angle (vertical asymptote)");
                return N(result);
            }
            Number z = kind switch
            {
                TrigKind.Sin => ComplexMath.Sin(rad),
                TrigKind.Cos => ComplexMath.Cos(rad),
                TrigKind.Tan => ComplexMath.Tan(rad),
                TrigKind.Sec => Number.Approx(1) / ComplexMath.Cos(rad),
                TrigKind.Csc => Number.Approx(1) / ComplexMath.Sin(rad),
                _ => Number.Approx(1) / ComplexMath.Tan(rad),
            };
            return N(z);
        }
        // Exact special angles: rational multiples of π (or degrees / gradians).
        var k = PiMultiple(x, settings.Angle);
        if (k is not null)
        {
            var value = ExactTrig(kind, k.Value);
            if (value is not null) return value;
        }
        // Odd and even symmetry: sin(-x) = -sin(x), cos(-x) = cos(x).
        if (x is MulExpr m && m.Factors[0] is NumberExpr { Value.IsNegativeReal: true })
        {
            var pos = Algebra.Neg(x);
            var name = c.Function.Name;
            var call = CallExpr.Of(name, pos);
            return kind is TrigKind.Cos or TrigKind.Sec ? call : Algebra.Neg(call);
        }
        return null;
    }

    /// <summary>The angle as a multiple of π (k in kπ), when the input is a rational multiple of π in the current unit.</summary>
    private static BigRational? PiMultiple(Expr x, AngleMode mode)
    {
        if (mode == AngleMode.Degree || mode == AngleMode.Gradian)
        {
            if (x is NumberExpr { Value.IsExactRational: true } n)
                return n.Value.ReQ / (mode == AngleMode.Degree ? 180 : 200);
            return null;
        }
        if (x is NumberExpr { Value.IsExactRational: true, Value.IsZero: true }) return BigRational.Zero;
        if (x is ConstantExpr { Constant: ConstantKind.Pi }) return BigRational.One;
        if (x is MulExpr { Factors.Length: 2 } m && m.Factors[0] is NumberExpr { Value.IsExactRational: true } c && m.Factors[1] is ConstantExpr { Constant: ConstantKind.Pi })
            return c.Value.ReQ;
        return null;
    }

    private static Expr? ExactTrig(TrigKind kind, BigRational k)
    {
        // Reduce to [0, 2) and express in twelfths of π.
        var two = new BigRational(2);
        var reduced = k - two * new BigRational((k / two).Floor());
        var twelfths = reduced * 12;
        if (!twelfths.IsInteger) return null;
        int n = (int)twelfths.Num; // 0..23
        Expr Sin(int m)
        {
            m = ((m % 24) + 24) % 24;
            if (m <= 6) return SinTable.Value[m];
            if (m <= 12) return SinTable.Value[12 - m];
            return Algebra.Neg(Sin(m - 12));
        }
        Expr Cos(int m) => Sin(m + 6);
        Expr? Tan(int m)
        {
            m = ((m % 12) + 12) % 12;
            if (m == 6) return null;
            if (m < 6) return TanTable.Value[m];
            return Algebra.Neg(TanTable.Value[12 - m]);
        }
        switch (kind)
        {
            case TrigKind.Sin: return Sin(n);
            case TrigKind.Cos: return Cos(n);
            case TrigKind.Tan:
                return Tan(n) ?? throw new CalcException("tan is undefined here (it has a vertical asymptote at 90°)");
            case TrigKind.Sec:
            {
                var cv = Cos(n);
                if (cv.IsZero) throw new CalcException("sec is undefined here (cos is 0)");
                return Algebra.Pow(cv, Expr.MinusOne);
            }
            case TrigKind.Csc:
            {
                var sv = Sin(n);
                if (sv.IsZero) throw new CalcException("csc is undefined here (sin is 0)");
                return Algebra.Pow(sv, Expr.MinusOne);
            }
            default:
            {
                var tv = Tan(n);
                if (tv is null) return Expr.Zero;
                if (tv.IsZero) throw new CalcException("cot is undefined here (tan is 0)");
                return Algebra.Pow(tv, Expr.MinusOne);
            }
        }
    }

    private static Expr? InverseTrig(CallInfo c, TrigKind kind)
    {
        var x = c[0];
        if (WantsNumeric(c, x))
        {
            var v = ((NumberExpr)x).Value;
            Expr rad = kind switch
            {
                TrigKind.Sin => NumericUnary(v, Math.Asin, ComplexMath.Asin, t => Math.Abs(t) <= 1),
                TrigKind.Cos => NumericUnary(v, Math.Acos, ComplexMath.Acos, t => Math.Abs(t) <= 1),
                _ => NumericUnary(v, Math.Atan, ComplexMath.Atan),
            };
            var rv = ((NumberExpr)rad).Value;
            if (!rv.IsReal) return rad;
            double factor = c.Context.Settings.ToRadians;
            return N(rv.Re / factor);
        }
        // Exact: find n with sin(nπ/12) = x for n in -6..6 (or cos for 0..12, tan for -5..5).
        int? found = null;
        if (kind == TrigKind.Sin || kind == TrigKind.Cos)
        {
            for (int n = -6; n <= 6; n++)
            {
                var sv = n >= 0 ? SinTable.Value[n] : Algebra.Neg(SinTable.Value[-n]);
                if (sv.Equals(x)) { found = n; break; }
            }
            if (found is not null && kind == TrigKind.Cos) found = 6 - found.Value;
        }
        else
        {
            for (int n = -5; n <= 5; n++)
            {
                var tv = n >= 0 ? TanTable.Value[n] : Algebra.Neg(TanTable.Value[-n]);
                if (tv.Equals(x)) { found = n; break; }
            }
            if (x is ConstantExpr { Constant: ConstantKind.Infinity }) found = 6;
            if (x is ConstantExpr { Constant: ConstantKind.NegativeInfinity }) found = -6;
        }
        if (found is null)
        {
            if (x is NumberExpr { Value.IsExactRational: true } nr && Math.Abs(nr.Value.Re) > 1 && kind != TrigKind.Tan)
                return null; // complex result, shown approximately
            return null;
        }
        var radians = Algebra.Mul(Expr.Rational(found.Value, 12), ConstantExpr.Pi);
        return c.Context.Settings.Angle switch
        {
            AngleMode.Degree => Expr.Int(found.Value * 15),
            AngleMode.Gradian => Algebra.Mul(Expr.Rational(found.Value * 50, 3), Expr.One),
            _ => radians,
        };
    }

    private static Expr? Atan2(CallInfo c)
    {
        if (c[0] is NumberExpr y && c[1] is NumberExpr x)
        {
            if (!y.Value.IsReal || !x.Value.IsReal) throw c.Error("needs real numbers");
            if (y.Value.IsZero && x.Value.IsZero) throw c.Error("atan2(0, 0) is undefined");
            if (y.Value.IsExact && x.Value.IsExact && !c.Approximate)
            {
                // atan2 via atan with quadrant correction keeps exact special angles.
                if (x.Value.IsZero) return FromRadians(Algebra.Mul(Expr.Rational(y.Value.RealSign, 2), ConstantExpr.Pi), c.Context);
                var ratio = Algebra.Div(y, x);
                var a = InverseTrig(new CallInfo(c.Function, [ratio], c.Context.WithApproximate(false)), TrigKind.Tan);
                if (a is not null)
                {
                    if (x.Value.IsPositiveReal) return a;
                    var half = c.Context.Settings.Angle switch { AngleMode.Degree => Expr.Int(180), AngleMode.Gradian => Expr.Int(200), _ => (Expr)ConstantExpr.Pi };
                    return y.Value.IsNegativeReal ? Algebra.Sub(a, half) : Algebra.Add(a, half);
                }
                return null;
            }
            return N(Math.Atan2(y.Value.Re, x.Value.Re) / c.Context.Settings.ToRadians);
        }
        return null;
    }

    private static Expr? Hyperbolic(CallInfo c, Func<double, double> real, Func<Number, Number> complex, bool odd, Expr? atZero = null,
        Func<double, bool>? domain = null, Expr? atOne = null)
    {
        var x = c[0];
        if (WantsNumeric(c, x)) return NumericUnary(((NumberExpr)x).Value, real, complex, domain);
        if (x is NumberExpr { Value.IsZero: true }) return atZero ?? (odd ? Expr.Zero : null);
        if (atOne is not null && x is NumberExpr { Value.IsOne: true }) return atOne;
        if (x is MulExpr m && m.Factors[0] is NumberExpr { Value.IsNegativeReal: true } && c.Function.Name is "sinh" or "cosh" or "tanh" or "asinh" or "atanh")
        {
            var call = CallExpr.Of(c.Function.Name, Algebra.Neg(x));
            return odd ? Algebra.Neg(call) : call;
        }
        return null;
    }
}

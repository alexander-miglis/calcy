using System.Globalization;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;
using Calcy.Core.Parsing;
using Calcy.Core.Printing;

namespace Calcy.UI.Math.Model;

/// <summary>
/// Builds a display model from an expression with the same conventions as <see cref="ExprPrinter"/>:
/// terms of a sum ordered by degree, numerator and denominator split from negative powers, √ for x^(1/2),
/// ∛ for x^(1/3), |x| for abs, x! for factorial, implicit multiplication "2x" and "x·y" for longer names.
/// </summary>
public static class MathDisplayBuilder
{
    private const int PLambda = 0, POr = 1, PAnd = 2, PNot = 3, PRel = 4, PAdd = 5, PMul = 6, PNeg = 7, PPow = 8, PPost = 9, PAtom = 10;

    /// <summary>Builds a display row for an expression.</summary>
    /// <param name="e">The expression.</param>
    /// <param name="options">Number formatting; defaults to <see cref="PrintOptions.Display"/>.</param>
    /// <param name="keepTermOrder">Keep the order of terms in sums instead of sorting them by degree.</param>
    public static MathRow FromExpr(Expr e, PrintOptions? options = null, bool keepTermOrder = false)
    {
        var b = new Builder(options ?? PrintOptions.Display, keepTermOrder);
        var row = new MathRow();
        b.Write(row, e, 0);
        return row;
    }

    /// <summary>Builds a display row for a statement: an expression, "name = value" or "f(x) = body".</summary>
    public static MathRow FromStatement(Statement statement, PrintOptions? options = null, bool keepTermOrder = false)
    {
        var b = new Builder(options ?? PrintOptions.Display, keepTermOrder);
        var row = new MathRow();
        switch (statement)
        {
            case ExprStatement s:
                b.Write(row, s.Expression, 0);
                break;
            case AssignStatement a:
                MathInputBuilder.EmitName(row, a.Name);
                row.Add(new MathChar("="));
                b.Write(row, a.Value, 0);
                break;
            case FunctionDefStatement f:
            {
                MathInputBuilder.EmitName(row, f.Name);
                var p = new MathTemplate(MathTemplateKind.Parens);
                for (int i = 0; i < f.Parameters.Length; i++)
                {
                    if (i > 0) p[0].Add(new MathChar(","));
                    MathInputBuilder.EmitName(p[0], f.Parameters[i]);
                }
                row.Add(p);
                row.Add(new MathChar("="));
                b.Write(row, f.Body, 0);
                break;
            }
        }
        return row;
    }

    private sealed class Builder(PrintOptions o, bool keepOrder)
    {
        public void Write(MathRow row, Expr e, int minPrec)
        {
            if (Prec(e) < minPrec)
            {
                var p = new MathTemplate(MathTemplateKind.Parens);
                WriteCore(p[0], e);
                row.Add(p);
            }
            else WriteCore(row, e);
        }

        private int Prec(Expr e) => e switch
        {
            NumberExpr n => NumberPrec(n.Value),
            ConstantExpr { Constant: ConstantKind.NegativeInfinity } => PNeg,
            AddExpr => PAdd,
            MulExpr m => IsNegative(m) ? PNeg : PMul,
            PowExpr pw => PowPrec(pw),
            RelationExpr => PRel,
            LogicExpr { Op: LogicOp.Not } => PNot,
            LogicExpr { Op: LogicOp.And } => PAnd,
            LogicExpr => POr,
            LambdaExpr => PLambda,
            PiecewiseExpr pw when pw.Otherwise is null && pw.Cases.Length == 1 => PAdd,
            CallExpr { Name: "factorial" or "doublefactorial" or "percent" or "deg" } => PPost,
            _ => PAtom,
        };

        private static int PowPrec(PowExpr p)
        {
            if (p.Exponent is NumberExpr { Value.IsExactRational: true } ne)
            {
                var q = ne.Value.ReQ;
                if (q.IsNegative) return PMul;
                if (q == BigRational.Half || q == new BigRational(1, 3)) return PAtom;
            }
            return PPow;
        }

        private int NumberPrec(Number n)
        {
            if (!n.IsReal)
            {
                bool reZero = n.IsExact ? n.ReQ.IsZero : n.Re == 0;
                if (!reZero) return PAdd;
                return n.Im < 0 ? PNeg : PMul;
            }
            if (n.IsNegativeReal) return PNeg;
            if (n.IsExact && !n.ReQ.IsInteger && !o.RationalsAsDecimals) return PMul;
            if (!n.IsExact && FormatDouble(n.Re).Contains('e')) return PMul;
            return PAtom;
        }

        private static bool IsNegative(Expr e) => e switch
        {
            NumberExpr n => n.Value.IsNegativeReal,
            MulExpr m => m.Factors[0] is NumberExpr { Value.IsNegativeReal: true },
            ConstantExpr { Constant: ConstantKind.NegativeInfinity } => true,
            _ => false,
        };

        private static Expr Negate(Expr e) => e switch
        {
            NumberExpr n => new NumberExpr(-n.Value),
            MulExpr m when m.Factors[0] is NumberExpr { Value.IsMinusOne: true } && m.Factors.Length == 2 => m.Factors[1],
            MulExpr m when m.Factors[0] is NumberExpr { Value.IsMinusOne: true } => new MulExpr(m.Factors.RemoveAt(0)),
            MulExpr m when m.Factors[0] is NumberExpr c => new MulExpr(m.Factors.SetItem(0, new NumberExpr(-c.Value))),
            ConstantExpr { Constant: ConstantKind.NegativeInfinity } => ConstantExpr.Infinity,
            _ => e,
        };

        private string FormatDouble(double x) => NumberFormatter.Format(x, o.Numbers with { Unicode = false });

        private static void Text(MathRow row, string s) => MathInputBuilder.Chars(row, s);

        private static void Word(MathRow row, string s, MathColorRole color = MathColorRole.Ink) => row.Add(new MathChar(s, color));

        private void WriteCore(MathRow row, Expr e)
        {
            switch (e)
            {
                case NumberExpr n:
                    WriteNumber(row, n.Value);
                    break;
                case ConstantExpr c:
                    switch (c.Constant)
                    {
                        case ConstantKind.Pi: Word(row, "π"); break;
                        case ConstantKind.E: Word(row, "e"); break;
                        case ConstantKind.Infinity: Word(row, "∞"); break;
                        case ConstantKind.NegativeInfinity: Word(row, "-"); Word(row, "∞"); break;
                        default: Word(row, "undefined", MathColorRole.Muted); break;
                    }
                    break;
                case SymbolExpr { Name: "h_bar" or "hbar" }:
                    // The reduced Planck constant is typed h_bar but printed as ħ.
                    MathInputBuilder.Chars(row, "ħ");
                    break;
                case SymbolExpr s:
                    MathInputBuilder.EmitName(row, s.Name);
                    break;
                case AddExpr a:
                    WriteAdd(row, a);
                    break;
                case MulExpr m:
                    WriteMul(row, m);
                    break;
                case PowExpr p:
                    WritePow(row, p);
                    break;
                case CallExpr c:
                    WriteCall(row, c);
                    break;
                case RelationExpr r:
                    Write(row, r.Left, PRel + 1);
                    Word(row, RelationExpr.Symbol(r.Op));
                    Write(row, r.Right, PRel + 1);
                    break;
                case LogicExpr l:
                    if (l.Op == LogicOp.Not)
                    {
                        Word(row, "not");
                        Write(row, l.Operands[0], PNot);
                    }
                    else
                    {
                        string word = l.Op switch { LogicOp.And => "and", LogicOp.Or => "or", _ => "xor" };
                        int prec = l.Op == LogicOp.And ? PAnd : POr;
                        for (int i = 0; i < l.Operands.Length; i++)
                        {
                            if (i > 0) Word(row, word);
                            Write(row, l.Operands[i], prec + 1);
                        }
                    }
                    break;
                case ListExpr list:
                {
                    var t = new MathTemplate(MathTemplateKind.Braces);
                    WriteJoined(t[0], list.Items);
                    row.Add(t);
                    break;
                }
                case TupleExpr tuple:
                {
                    var t = new MathTemplate(MathTemplateKind.Parens);
                    WriteJoined(t[0], tuple.Items);
                    row.Add(t);
                    break;
                }
                case MatrixExpr mx:
                {
                    var t = new MathTemplate(MathTemplateKind.Matrix, mx.Rows, mx.Cols);
                    for (int r = 0; r < mx.Rows; r++)
                        for (int c = 0; c < mx.Cols; c++)
                            Write(t.Cell(r, c), mx[r, c], 0);
                    row.Add(t);
                    break;
                }
                case StringExpr str:
                    row.Add(new MathChar(MathInputBuilder.QuoteString(str.Value)));
                    break;
                case BoolExpr b:
                    Word(row, b.Value ? "true" : "false");
                    break;
                case LambdaExpr lam:
                {
                    if (lam.Parameters.Length == 1) MathInputBuilder.EmitName(row, lam.Parameters[0]);
                    else
                    {
                        var p = new MathTemplate(MathTemplateKind.Parens);
                        for (int i = 0; i < lam.Parameters.Length; i++)
                        {
                            if (i > 0) p[0].Add(new MathChar(","));
                            MathInputBuilder.EmitName(p[0], lam.Parameters[i]);
                        }
                        row.Add(p);
                    }
                    Word(row, "↦");
                    Write(row, lam.Body, 0);
                    break;
                }
                case PiecewiseExpr pw:
                    WritePiecewise(row, pw);
                    break;
                case IndexExpr ix:
                {
                    Write(row, ix.Target, PAtom);
                    var t = new MathTemplate(MathTemplateKind.Brackets);
                    WriteJoined(t[0], ix.Indices);
                    row.Add(t);
                    break;
                }
                default:
                    Text(row, ExprPrinter.Print(e, o));
                    break;
            }
        }

        private void WriteJoined(MathRow row, IEnumerable<Expr> items)
        {
            bool first = true;
            foreach (var it in items)
            {
                if (!first) row.Add(new MathChar(","));
                Write(row, it, 0);
                first = false;
            }
        }

        private void WritePiecewise(MathRow row, PiecewiseExpr pw)
        {
            if (pw.Otherwise is null && pw.Cases.Length == 1)
            {
                Write(row, pw.Cases[0].Value, PAdd);
                row.Add(new MathChar(" "));
                var braces = new MathTemplate(MathTemplateKind.Braces);
                Write(braces[0], pw.Cases[0].Condition, 0);
                row.Add(braces);
                return;
            }
            int rows = pw.Cases.Length + (pw.Otherwise is null ? 0 : 1);
            var t = new MathTemplate(MathTemplateKind.Piecewise, rows);
            for (int i = 0; i < pw.Cases.Length; i++)
            {
                Write(t.Cell(i, 0), pw.Cases[i].Value, 0);
                Write(t.Cell(i, 1), pw.Cases[i].Condition, 0);
            }
            if (pw.Otherwise is not null)
            {
                Write(t.Cell(pw.Cases.Length, 0), pw.Otherwise, 0);
                t.Cell(pw.Cases.Length, 1).Add(new MathChar("otherwise", MathColorRole.Muted));
            }
            row.Add(t);
        }

        // ------------------------------------------------------------ numbers

        private void WriteNumber(MathRow row, Number n)
        {
            if (n.IsReal)
            {
                if (n.IsExact)
                {
                    var q = n.ReQ;
                    if (q.IsInteger)
                    {
                        Text(row, q.Num.ToString(CultureInfo.InvariantCulture));
                    }
                    else if (o.RationalsAsDecimals)
                    {
                        WriteDecimalText(row, NumberFormatter.FormatRational(q, o.Numbers with { Unicode = false }));
                    }
                    else
                    {
                        if (q.IsNegative) Word(row, "-");
                        var f = new MathTemplate(MathTemplateKind.Fraction);
                        Text(f[0], System.Numerics.BigInteger.Abs(q.Num).ToString(CultureInfo.InvariantCulture));
                        Text(f[1], q.Den.ToString(CultureInfo.InvariantCulture));
                        row.Add(f);
                    }
                }
                else WriteDecimalText(row, FormatDouble(n.Re));
                return;
            }
            bool reZero = n.IsExact ? n.ReQ.IsZero : n.Re == 0;
            if (!reZero)
            {
                WriteNumber(row, n.IsExact ? Number.Exact(n.ReQ) : Number.Approx(n.Re));
                bool imNeg = n.Im < 0;
                Word(row, imNeg ? "-" : "+");
                WriteImaginary(row, n.IsExact ? Number.Exact(n.ImQ.Abs()) : Number.Approx(System.Math.Abs(n.Im)));
            }
            else
            {
                var im = n.IsExact ? Number.Exact(n.ImQ) : Number.Approx(n.Im);
                if (im.IsNegativeReal)
                {
                    Word(row, "-");
                    im = -im;
                }
                WriteImaginary(row, im);
            }
        }

        private void WriteImaginary(MathRow row, Number im)
        {
            if (im.IsOne)
            {
                Word(row, "i");
                return;
            }
            if (im.IsExact && !im.ReQ.IsInteger && !o.RationalsAsDecimals)
            {
                var f = new MathTemplate(MathTemplateKind.Fraction);
                if (!System.Numerics.BigInteger.Abs(im.ReQ.Num).IsOne) Text(f[0], System.Numerics.BigInteger.Abs(im.ReQ.Num).ToString(CultureInfo.InvariantCulture));
                f[0].Add(new MathChar("i"));
                Text(f[1], im.ReQ.Den.ToString(CultureInfo.InvariantCulture));
                row.Add(f);
                return;
            }
            WriteNumber(row, im);
            Word(row, "i");
        }

        /// <summary>Writes "1.5e12" as 1.5×10 with a superscript 12.</summary>
        private static void WriteDecimalText(MathRow row, string s)
        {
            if (s == "∞" || s == "undefined")
            {
                Word(row, s, s == "undefined" ? MathColorRole.Muted : MathColorRole.Ink);
                return;
            }
            if (s.StartsWith("-∞", StringComparison.Ordinal) || s.StartsWith("-inf", StringComparison.Ordinal))
            {
                Word(row, "-");
                Word(row, "∞");
                return;
            }
            int e = s.IndexOfAny(['e', 'E']);
            if (e < 0)
            {
                Text(row, s);
                return;
            }
            Text(row, s[..e]);
            Word(row, "×");
            Text(row, "10");
            var p = new MathTemplate(MathTemplateKind.Power);
            var exp = s[(e + 1)..];
            if (exp.StartsWith('+')) exp = exp[1..];
            Text(p[0], exp);
            row.Add(p);
        }

        // ------------------------------------------------------------ sums and products

        private static double Degree(Expr e) => e switch
        {
            NumberExpr or ConstantExpr => 0,
            SymbolExpr => 1,
            PowExpr { Exponent: NumberExpr n } p when n.Value.IsReal => Degree(p.Base) * n.Value.Re,
            PowExpr p => Degree(p.Base) > 0 ? 1 : 0,
            MulExpr m => m.Factors.Sum(Degree),
            AddExpr a => a.Terms.Max(Degree),
            CallExpr c => c.Args.Any(x => Degree(x) > 0) ? 1 : 0,
            _ => 0,
        };

        private static string TermKey(Expr t)
        {
            var (_, rest) = Algebra.SplitCoefficient(t);
            return ExprPrinter.Print(rest, PrintOptions.Input);
        }

        private void WriteAdd(MathRow row, AddExpr a)
        {
            // The same reading order as the text and LaTeX printers (5 - √13, x·e^x), unless the input order is kept.
            var terms = keepOrder ? a.Terms.ToList() : ExprPrinter.DisplayTerms(a);
            for (int i = 0; i < terms.Count; i++)
            {
                var t = terms[i];
                if (i == 0)
                {
                    Write(row, t, PAdd);
                    continue;
                }
                if (IsNegative(t))
                {
                    Word(row, "-");
                    Write(row, Negate(t), PAdd + 1);
                }
                else if (t is NumberExpr { Value.IsReal: false } cn && (cn.Value.IsExact ? cn.Value.ReQ.IsZero : cn.Value.Re == 0) && cn.Value.Im < 0)
                {
                    Word(row, "-");
                    Write(row, new NumberExpr(-cn.Value), PAdd + 1);
                }
                else
                {
                    Word(row, "+");
                    Write(row, t, PAdd + 1);
                }
            }
        }

        private void WriteMul(MathRow row, MulExpr m)
        {
            var num = new List<Expr>();
            var den = new List<Expr>();
            bool negative = false;
            Number? coefficient = null;
            foreach (var f in m.Factors)
            {
                if (f is NumberExpr n && n.Value.IsReal && coefficient is null && ReferenceEquals(f, m.Factors[0]))
                {
                    var v = n.Value;
                    if (v.IsNegativeReal) { negative = true; v = -v; }
                    if (v.IsExact && !v.ReQ.IsInteger && !o.RationalsAsDecimals)
                    {
                        var q = v.ReQ;
                        if (!q.Num.IsOne) num.Add(new NumberExpr(Number.Exact(q.Num)));
                        den.Add(new NumberExpr(Number.Exact(q.Den)));
                    }
                    else if (!v.IsOne) num.Add(new NumberExpr(v));
                    coefficient = v;
                    continue;
                }
                if (f is PowExpr { Exponent: NumberExpr ne } pw && ne.Value.IsNegativeReal)
                {
                    var pos = -ne.Value;
                    den.Add(pos.IsOne ? pw.Base : new PowExpr(pw.Base, new NumberExpr(pos)));
                    continue;
                }
                num.Add(f);
            }
            if (!keepOrder) num = ExprPrinter.DisplayFactors(num);
            if (negative) Word(row, "-");
            if (den.Count == 0)
            {
                if (num.Count == 0) Text(row, "1");
                else WriteFactors(row, num);
                return;
            }
            var frac = new MathTemplate(MathTemplateKind.Fraction);
            if (num.Count == 0) Text(frac[0], "1");
            else WriteFactors(frac[0], num);
            WriteFactors(frac[1], den);
            row.Add(frac);
        }

        /// <summary>Factors joined like ExprPrinter (2x, x·y); a single factor alone in a fraction slot needs no brackets.</summary>
        private void WriteFactors(MathRow row, List<Expr> factors)
        {
            if (factors.Count == 1 && row.Owner?.Kind == MathTemplateKind.Fraction)
            {
                Write(row, factors[0], PAdd);
                return;
            }
            for (int i = 0; i < factors.Count; i++)
            {
                var f = factors[i];
                if (i > 0)
                {
                    var sep = Separator(factors[i - 1], f);
                    if (sep.Length > 0) Word(row, sep);
                }
                Write(row, f, PMul + 1);
            }
        }

        private static string Separator(Expr left, Expr right)
        {
            if (right is NumberExpr) return "·";
            if (left is NumberExpr && right is SymbolExpr or ConstantExpr or CallExpr or PowExpr or AddExpr) return "";
            if (left is NumberExpr && right is NumberExpr) return "·";
            if (IsSingleLetter(left) && IsSingleLetter(right)) return "";
            return "·";
        }

        private static bool IsSingleLetter(Expr e) => e switch
        {
            SymbolExpr s => s.Name.Length == 1,
            ConstantExpr => true,
            PowExpr { Base: SymbolExpr s2, Exponent: NumberExpr { Value.IsExactInteger: true } } => s2.Name.Length == 1,
            _ => false,
        };

        private void WritePow(MathRow row, PowExpr p)
        {
            if (p.Exponent is NumberExpr { Value.IsExactRational: true } ne)
            {
                var q = ne.Value.ReQ;
                if (q.IsNegative)
                {
                    var f = new MathTemplate(MathTemplateKind.Fraction);
                    Text(f[0], "1");
                    if ((-q).IsOne) Write(f[1], p.Base, PAdd);
                    else Write(f[1], new PowExpr(p.Base, new NumberExpr(Number.Exact(-q))), PAdd);
                    row.Add(f);
                    return;
                }
                if (q == BigRational.Half)
                {
                    var t = new MathTemplate(MathTemplateKind.Sqrt);
                    Write(t[0], p.Base, 0);
                    row.Add(t);
                    return;
                }
                if (q == new BigRational(1, 3))
                {
                    var t = new MathTemplate(MathTemplateKind.NthRoot) { FunctionName = "cbrt" };
                    Text(t[0], "3");
                    Write(t[1], p.Base, 0);
                    row.Add(t);
                    return;
                }
            }
            // Textbook form for powers of trig and log functions: sin²(θ), not sin(θ)².
            if (p.Base is CallExpr { Args.Length: 1 } fn && PowerBeforeArgument.Contains(fn.Name)
                && p.Exponent is NumberExpr { Value.IsExactInteger: true, Value.IsPositiveReal: true })
            {
                Word(row, fn.Name);
                var power = new MathTemplate(MathTemplateKind.Power);
                Write(power[0], p.Exponent, 0);
                row.Add(power);
                var arg = new MathTemplate(MathTemplateKind.Parens);
                Write(arg[0], fn.Args[0], 0);
                row.Add(arg);
                return;
            }
            Write(row, p.Base, PPost);
            var sup = new MathTemplate(MathTemplateKind.Power);
            Write(sup[0], p.Exponent, 0);
            row.Add(sup);
        }

        private static readonly HashSet<string> PowerBeforeArgument =
            ["sin", "cos", "tan", "cot", "sec", "csc", "sinh", "cosh", "tanh", "coth", "ln", "log"];

        private void WriteCall(MathRow row, CallExpr c)
        {
            var a = c.Args;
            switch (c.Name)
            {
                case "abs" when a.Length == 1:
                    row.Add(Slots(MathTemplateKind.Abs, a[0]));
                    return;
                case "sqrt" when a.Length == 1:
                    row.Add(Slots(MathTemplateKind.Sqrt, a[0]));
                    return;
                case "cbrt" when a.Length == 1:
                {
                    var t = new MathTemplate(MathTemplateKind.NthRoot) { FunctionName = "cbrt" };
                    Text(t[0], "3");
                    Write(t[1], a[0], 0);
                    row.Add(t);
                    return;
                }
                case "root" when a.Length == 2:
                    row.Add(Slots(MathTemplateKind.NthRoot, a[1], a[0]));
                    return;
                case "log" when a.Length == 2:
                    row.Add(Slots(MathTemplateKind.LogBase, a[1], a[0]));
                    return;
                case "factorial" when a.Length == 1:
                    Write(row, a[0], PPost + 1);
                    Word(row, "!");
                    return;
                case "doublefactorial" when a.Length == 1:
                    Write(row, a[0], PPost + 1);
                    Word(row, "!!");
                    return;
                case "percent" when a.Length == 1:
                    Write(row, a[0], PPost + 1);
                    Word(row, "%");
                    return;
                case "deg" when a.Length == 1:
                    Write(row, a[0], PPost + 1);
                    Word(row, "°");
                    return;
                case "fderiv" when a.Length >= 2 && a[0] is SymbolExpr f && a[1] is NumberExpr order && order.Value.IsExactInteger:
                {
                    MathInputBuilder.EmitName(row, f.Name);
                    int n = (int)System.Math.Clamp(order.Value.Re, 0, 20);
                    if (n <= 3) Word(row, new string('′', n));
                    else
                    {
                        var sup = new MathTemplate(MathTemplateKind.Power);
                        sup[0].Add(new MathChar("("));
                        Text(sup[0], n.ToString(CultureInfo.InvariantCulture));
                        sup[0].Add(new MathChar(")"));
                        row.Add(sup);
                    }
                    var args = new MathTemplate(MathTemplateKind.Parens);
                    WriteJoined(args[0], a.Skip(2));
                    row.Add(args);
                    return;
                }
                case "integrate" when a.Length == 2:
                    row.Add(Slots(MathTemplateKind.Integral, a[0], a[1]));
                    return;
                case "integrate" when a.Length == 4:
                    row.Add(Slots(MathTemplateKind.DefiniteIntegral, a[2], a[3], a[0], a[1]));
                    return;
                case "diff" when a.Length == 2:
                {
                    var t = new MathTemplate(MathTemplateKind.Derivative);
                    Write(t[0], a[1], 0);
                    Write(t[1], a[0], PMul);
                    row.Add(t);
                    return;
                }
                case "diff" when a.Length == 3 && a[2] is NumberExpr:
                {
                    // d^n/dx^n f
                    var frac = new MathTemplate(MathTemplateKind.Fraction);
                    Word(frac[0], "d");
                    frac[0].Add(Slots(MathTemplateKind.Power, a[2]));
                    Word(frac[1], "d");
                    Write(frac[1], a[1], PAtom);
                    frac[1].Add(Slots(MathTemplateKind.Power, a[2]));
                    row.Add(frac);
                    Write(row, a[0], PMul);
                    return;
                }
                case "sum" or "product" or "prod" when a.Length == 4:
                {
                    var t = new MathTemplate(c.Name == "sum" ? MathTemplateKind.Sum : MathTemplateKind.Product);
                    Write(t[0], a[1], 0);
                    Write(t[1], a[2], 0);
                    Write(t[2], a[3], 0);
                    Write(t[3], a[0], PMul);
                    row.Add(t);
                    return;
                }
                case "limit" when a.Length is 3 or 4:
                {
                    var t = new MathTemplate(MathTemplateKind.Limit);
                    Write(t[0], a[1], 0);
                    Write(t[1], a[2], 0);
                    if (a.Length == 4 && a[3] is StringExpr { Value: "+" or "-" } dir)
                    {
                        var sup = new MathTemplate(MathTemplateKind.Power);
                        Word(sup[0], dir.Value);
                        t[1].Add(sup);
                    }
                    else if (a.Length == 4 && a[3] is NumberExpr dn && dn.Value.IsReal)
                    {
                        var sup = new MathTemplate(MathTemplateKind.Power);
                        Word(sup[0], dn.Value.Re < 0 ? "-" : "+");
                        t[1].Add(sup);
                    }
                    Write(t[2], a[0], PMul);
                    row.Add(t);
                    return;
                }
            }
            if (c.Name.Contains('_')) MathInputBuilder.EmitName(row, c.Name);
            else Word(row, c.Name);
            var call = new MathTemplate(MathTemplateKind.Parens);
            WriteJoined(call[0], a);
            row.Add(call);
        }

        private MathTemplate Slots(MathTemplateKind kind, params Expr[] slots)
        {
            var t = new MathTemplate(kind);
            for (int i = 0; i < slots.Length; i++) Write(t[i], slots[i], 0);
            return t;
        }
    }
}

using System.Text;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Core.Printing;

public sealed record PrintOptions
{
    /// <summary>Unicode output (√, π, ², ·) for display. When false the output can be typed back into Calcy.</summary>
    public bool Unicode { get; init; } = true;

    public NumberFormatOptions Numbers { get; init; } = NumberFormatOptions.Default;

    /// <summary>Show exact rationals as decimals instead of fractions.</summary>
    public bool RationalsAsDecimals { get; init; }

    public static readonly PrintOptions Display = new();
    public static readonly PrintOptions Input = new() { Unicode = false, Numbers = NumberFormatOptions.Ascii };
}

/// <summary>Turns expressions into readable linear text.</summary>
public static class ExprPrinter
{
    private const int PLambda = 0, POr = 1, PAnd = 2, PNot = 3, PRel = 4, PAdd = 5, PMul = 6, PNeg = 7, PPow = 8, PPost = 9, PAtom = 10;
    private const string Sup = "⁰¹²³⁴⁵⁶⁷⁸⁹";

    public static string ToText(Expr e) => Print(e, PrintOptions.Display);

    /// <summary>ASCII text that the Calcy parser reads back as the same expression.</summary>
    public static string ToInput(Expr e) => Print(e, PrintOptions.Input);

    public static string Print(Expr e, PrintOptions o)
    {
        var sb = new StringBuilder();
        Write(sb, e, 0, o);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, Expr e, int minPrec, PrintOptions o)
    {
        int p = Prec(e, o);
        bool paren = p < minPrec;
        if (paren) sb.Append('(');
        WriteCore(sb, e, o);
        if (paren) sb.Append(')');
    }

    private static int Prec(Expr e, PrintOptions o) => e switch
    {
        NumberExpr n => NumberPrec(n.Value, o),
        ConstantExpr { Constant: ConstantKind.NegativeInfinity } => PNeg,
        AddExpr => PAdd,
        MulExpr m => IsNegative(m) ? PNeg : PMul,
        PowExpr pw => PowPrec(pw, o),
        RelationExpr => PRel,
        LogicExpr { Op: LogicOp.Not } => PNot,
        LogicExpr { Op: LogicOp.And } => PAnd,
        LogicExpr => POr,
        LambdaExpr => PLambda,
        PiecewiseExpr pw when pw.Otherwise is null && pw.Cases.Length == 1 => PAdd,
        CallExpr { Name: "factorial" or "doublefactorial" or "percent" or "deg" } => PPost,
        _ => PAtom,
    };

    private static int PowPrec(PowExpr p, PrintOptions o)
    {
        if (p.Exponent is NumberExpr { Value.IsExactRational: true } ne)
        {
            var q = ne.Value.ReQ;
            if (q.IsNegative) return PMul;            // printed as a fraction 1/x
            if (q == BigRational.Half) return PAtom;  // √x or sqrt(x)
            if (o.Unicode && q == new BigRational(1, 3)) return PAtom;
        }
        return PPow;
    }

    private static int NumberPrec(Number n, PrintOptions o)
    {
        if (!n.IsReal)
        {
            bool reZero = n.IsExact ? n.ReQ.IsZero : n.Re == 0;
            if (!reZero) return PAdd;
            // i and 3i read as one unit next to other factors: 3ix, not (3i)x.
            return n.Im < 0 ? PNeg : PAtom;
        }
        if (n.IsNegativeReal) return PNeg;
        if (n.IsExact && !n.ReQ.IsInteger && !o.RationalsAsDecimals) return PMul;
        if (!n.IsExact)
        {
            var s = NumberFormatter.Format(n.Re, o.Numbers);
            if (s.Contains('×') || s.Contains('e')) return PMul;
        }
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

    private static void WriteCore(StringBuilder sb, Expr e, PrintOptions o)
    {
        switch (e)
        {
            case NumberExpr n:
                WriteNumber(sb, n.Value, o);
                break;
            case ConstantExpr c:
                sb.Append(c.Constant switch
                {
                    ConstantKind.Pi => o.Unicode ? "π" : "pi",
                    ConstantKind.E => "e",
                    ConstantKind.Infinity => o.Unicode ? "∞" : "inf",
                    ConstantKind.NegativeInfinity => o.Unicode ? "-∞" : "-inf",
                    _ => "undefined",
                });
                break;
            case SymbolExpr s:
                sb.Append(s.Name);
                break;
            case AddExpr a:
                WriteAdd(sb, a, o);
                break;
            case MulExpr m:
                WriteMul(sb, m, o);
                break;
            case PowExpr p:
                WritePow(sb, p, o);
                break;
            case CallExpr c:
                WriteCall(sb, c, o);
                break;
            case RelationExpr r:
                Write(sb, r.Left, PRel + 1, o);
                sb.Append(' ').Append(o.Unicode ? RelationExpr.Symbol(r.Op) : AsciiRel(r.Op)).Append(' ');
                Write(sb, r.Right, PRel + 1, o);
                break;
            case LogicExpr l:
                if (l.Op == LogicOp.Not)
                {
                    sb.Append("not ");
                    Write(sb, l.Operands[0], PNot, o);
                }
                else
                {
                    string word = l.Op switch { LogicOp.And => " and ", LogicOp.Or => " or ", _ => " xor " };
                    int prec = l.Op == LogicOp.And ? PAnd : POr;
                    for (int i = 0; i < l.Operands.Length; i++)
                    {
                        if (i > 0) sb.Append(word);
                        Write(sb, l.Operands[i], prec + 1, o);
                    }
                }
                break;
            case ListExpr list:
                sb.Append('{');
                WriteJoined(sb, list.Items, o);
                sb.Append('}');
                break;
            case TupleExpr t:
                sb.Append('(');
                WriteJoined(sb, t.Items, o);
                sb.Append(')');
                break;
            case MatrixExpr mx:
                sb.Append('[');
                for (int r = 0; r < mx.Rows; r++)
                {
                    if (r > 0) sb.Append(", ");
                    sb.Append('[');
                    for (int c = 0; c < mx.Cols; c++)
                    {
                        if (c > 0) sb.Append(", ");
                        Write(sb, mx[r, c], 0, o);
                    }
                    sb.Append(']');
                }
                sb.Append(']');
                break;
            case StringExpr str:
                sb.Append('"').Append(str.Value.Replace("\"", "\\\"")).Append('"');
                break;
            case BoolExpr b:
                sb.Append(b.Value ? "true" : "false");
                break;
            case LambdaExpr lam:
                sb.Append('(').Append(string.Join(", ", lam.Parameters)).Append(") => ");
                Write(sb, lam.Body, 0, o);
                break;
            case PiecewiseExpr pw:
                if (pw.Otherwise is null && pw.Cases.Length == 1)
                {
                    Write(sb, pw.Cases[0].Value, PAdd, o);
                    sb.Append(" {");
                    Write(sb, pw.Cases[0].Condition, 0, o);
                    sb.Append('}');
                    break;
                }
                sb.Append('{');
                for (int i = 0; i < pw.Cases.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    Write(sb, pw.Cases[i].Condition, 0, o);
                    sb.Append(": ");
                    Write(sb, pw.Cases[i].Value, 0, o);
                }
                if (pw.Otherwise is not null)
                {
                    sb.Append(", ");
                    Write(sb, pw.Otherwise, 0, o);
                }
                sb.Append('}');
                break;
            case IndexExpr ix:
                Write(sb, ix.Target, PAtom, o);
                sb.Append('[');
                WriteJoined(sb, ix.Indices, o);
                sb.Append(']');
                break;
            default:
                sb.Append(e.Kind.ToString());
                break;
        }
    }

    private static string AsciiRel(RelOp op) => op switch
    {
        RelOp.Equal => "=",
        RelOp.NotEqual => "!=",
        RelOp.Less => "<",
        RelOp.LessEqual => "<=",
        RelOp.Greater => ">",
        RelOp.GreaterEqual => ">=",
        _ => "?",
    };

    private static void WriteJoined(StringBuilder sb, IEnumerable<Expr> items, PrintOptions o)
    {
        bool first = true;
        foreach (var it in items)
        {
            if (!first) sb.Append(", ");
            Write(sb, it, 0, o);
            first = false;
        }
    }

    private static void WriteNumber(StringBuilder sb, Number n, PrintOptions o)
    {
        if (n.IsReal)
        {
            if (n.IsExact)
            {
                var q = n.ReQ;
                if (q.IsInteger || !o.RationalsAsDecimals)
                {
                    if (q.IsInteger) sb.Append(q.Num.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    else sb.Append(q.Num.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('/').Append(q.Den.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                else sb.Append(NumberFormatter.FormatRational(q, o.Numbers));
            }
            else sb.Append(NumberFormatter.Format(n.Re, o.Numbers));
            return;
        }
        // Complex: a + bi
        bool reZero = n.IsExact ? n.ReQ.IsZero : n.Re == 0;
        if (!reZero)
        {
            WriteNumber(sb, n.IsExact ? Number.Exact(n.ReQ) : Number.Approx(n.Re), o);
            bool imNeg = n.Im < 0;
            sb.Append(imNeg ? " - " : " + ");
            var imAbs = n.IsExact ? Number.Exact(n.ImQ.Abs()) : Number.Approx(Math.Abs(n.Im));
            WriteImaginary(sb, imAbs, o);
        }
        else
        {
            var im = n.IsExact ? Number.Exact(n.ImQ) : Number.Approx(n.Im);
            if (im.IsNegativeReal)
            {
                sb.Append('-');
                im = -im;
            }
            WriteImaginary(sb, im, o);
        }
    }

    private static void WriteImaginary(StringBuilder sb, Number im, PrintOptions o)
    {
        if (im.IsOne) { sb.Append('i'); return; }
        if (im.IsExact && !im.ReQ.IsInteger && !o.RationalsAsDecimals)
        {
            if (!im.ReQ.Num.IsOne) sb.Append(im.ReQ.Num);
            sb.Append('i').Append('/').Append(im.ReQ.Den);
            return;
        }
        WriteNumber(sb, im, o);
        sb.Append(o.Unicode ? "i" : "*i");
    }

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

    /// <summary>Sort key of a term without its numeric coefficient, so ad - bc prints in alphabetical order.</summary>
    private static string TermKey(Expr t)
    {
        var (_, rest) = Algebra.SplitCoefficient(t);
        return Print(rest, PrintOptions.Input);
    }

    /// <summary>
    /// The reading order of a sum, shared by every printer (text, LaTeX, typeset): highest degree first, then
    /// alphabetical; sums of constants start with the plain number (5 + √13) and avoid a leading minus (5 - √13, √3 - √2).
    /// </summary>
    public static List<Expr> DisplayTerms(AddExpr a)
    {
        var terms = a.Terms
            .Select((t, i) => (Term: t, Index: i, Degree: t is NumberExpr ? -1.0 : Degree(t), Key: TermKey(t)))
            .OrderByDescending(x => x.Degree)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .ThenBy(x => x.Index)
            .Select(x => x.Term)
            .ToList();
        if (terms.Count > 1 && terms.All(t => t.IsConstantValue()))
        {
            int number = terms.FindIndex(t => t is NumberExpr);
            if (number > 0)
            {
                var n = terms[number];
                terms.RemoveAt(number);
                terms.Insert(0, n);
            }
            if (IsNegative(terms[0]))
            {
                int firstPositive = terms.FindIndex(t => !IsNegative(t));
                if (firstPositive > 0)
                {
                    var p = terms[firstPositive];
                    terms.RemoveAt(firstPositive);
                    terms.Insert(0, p);
                }
            }
        }
        return terms;
    }

    /// <summary>The reading order of the factors of a product (x·e^x, 2x·sin(x)), shared by every printer.</summary>
    public static List<Expr> DisplayFactors(IEnumerable<Expr> factors) =>
        [.. factors.Select((f, i) => (f, i)).OrderBy(x => FactorRank(x.f)).ThenBy(x => x.i).Select(x => x.f)];

    private static void WriteAdd(StringBuilder sb, AddExpr a, PrintOptions o)
    {
        var terms = DisplayTerms(a);
        for (int i = 0; i < terms.Count; i++)
        {
            var t = terms[i];
            if (i == 0)
            {
                Write(sb, t, PAdd, o);
                continue;
            }
            if (IsNegative(t))
            {
                sb.Append(" - ");
                Write(sb, Negate(t), PAdd + 1, o);
            }
            else if (t is NumberExpr { Value.IsReal: false } cn && (cn.Value.IsExact ? cn.Value.ReQ.IsZero : cn.Value.Re == 0) && cn.Value.Im < 0)
            {
                sb.Append(" - ");
                Write(sb, new NumberExpr(-cn.Value), PAdd + 1, o);
            }
            else
            {
                sb.Append(" + ");
                Write(sb, t, PAdd + 1, o);
            }
        }
    }

    private static void WriteMul(StringBuilder sb, MulExpr m, PrintOptions o)
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
        // Reading order: number, letters and their powers, then e^x-style powers, then functions (x·e^x, 2x·sin(x)).
        num = DisplayFactors(num);
        if (negative) sb.Append('-');
        if (num.Count == 0) sb.Append('1');
        else WriteFactors(sb, num, o, den.Count > 0);
        if (den.Count > 0)
        {
            sb.Append('/');
            if (den.Count == 1 && Prec(den[0], o) >= PPow && den[0] is not MulExpr) Write(sb, den[0], PPow, o);
            else if (den.Count == 1) Write(sb, den[0], PPow, o);
            else
            {
                sb.Append('(');
                WriteFactors(sb, den, o, false);
                sb.Append(')');
            }
        }
    }

    /// <summary>
    /// Only two moves are made: e to a variable power goes after the letters (x·e^x), and functions go last (2x·sin(x)).
    /// Everything else keeps the canonical order, so prime factorisations and (a + b)·x stay as they are.
    /// </summary>
    private static int FactorRank(Expr f) => f switch
    {
        PowExpr { Base: ConstantExpr { Constant: ConstantKind.E } } p when !p.Exponent.IsConstantValue() => 1,
        CallExpr => 2,
        _ => 0,
    };

    private static void WriteFactors(StringBuilder sb, List<Expr> factors, PrintOptions o, bool asNumerator)
    {
        bool wrapAll = asNumerator && factors.Count == 1 && factors[0] is AddExpr;
        if (wrapAll)
        {
            sb.Append('(');
            Write(sb, factors[0], 0, o);
            sb.Append(')');
            return;
        }
        for (int i = 0; i < factors.Count; i++)
        {
            var f = factors[i];
            if (i > 0) sb.Append(Separator(factors[i - 1], f, o));
            Write(sb, f, PMul + 1, o);
        }
    }

    private static string Separator(Expr left, Expr right, PrintOptions o)
    {
        if (!o.Unicode) return "*";
        if (right is NumberExpr) return "·";
        // 2·3^x and 2·3!, not 23^x and 23!: a factor that starts with a digit always needs the dot.
        if (right is PowExpr or CallExpr && Print(right, o) is { Length: > 0 } text && char.IsDigit(text[0])) return "·";
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

    private static void WritePow(StringBuilder sb, PowExpr p, PrintOptions o)
    {
        if (p.Exponent is NumberExpr { Value.IsExactRational: true } ne)
        {
            var q = ne.Value.ReQ;
            if (q.IsNegative)
            {
                sb.Append("1/");
                var pos = new PowExpr(p.Base, new NumberExpr(Number.Exact(-q)));
                if ((-q).IsOne) Write(sb, p.Base, PPow, o);
                else Write(sb, pos, PPow, o);
                return;
            }
            if (q == BigRational.Half)
            {
                if (o.Unicode)
                {
                    sb.Append('√');
                    Write(sb, p.Base, PAtom, o);
                }
                else
                {
                    sb.Append("sqrt(");
                    Write(sb, p.Base, 0, o);
                    sb.Append(')');
                }
                return;
            }
            if (o.Unicode && q == new BigRational(1, 3))
            {
                sb.Append('∛');
                Write(sb, p.Base, PAtom, o);
                return;
            }
            if (o.Unicode && q.IsInteger && q.Num >= 0 && q.Num <= 99)
            {
                Write(sb, p.Base, PPost, o);
                foreach (var ch in q.Num.ToString()) sb.Append(Sup[ch - '0']);
                return;
            }
        }
        Write(sb, p.Base, PPost, o);
        sb.Append('^');
        Write(sb, p.Exponent, PAtom, o);
    }

    private static void WriteCall(StringBuilder sb, CallExpr c, PrintOptions o)
    {
        switch (c.Name)
        {
            case "abs" when c.Args.Length == 1:
                sb.Append('|');
                Write(sb, c.Args[0], 0, o);
                sb.Append('|');
                return;
            case "factorial" when c.Args.Length == 1:
                Write(sb, c.Args[0], PPost + 1, o);
                sb.Append('!');
                return;
            case "doublefactorial" when c.Args.Length == 1:
                Write(sb, c.Args[0], PPost + 1, o);
                sb.Append("!!");
                return;
            case "percent" when c.Args.Length == 1:
                Write(sb, c.Args[0], PPost + 1, o);
                sb.Append('%');
                return;
            case "deg" when c.Args.Length == 1:
                Write(sb, c.Args[0], PPost + 1, o);
                sb.Append('°');
                return;
            case "fderiv" when c.Args.Length >= 2 && c.Args[0] is SymbolExpr f && c.Args[1] is NumberExpr order:
                sb.Append(f.Name).Append('\'', (int)order.Value.Re).Append('(');
                WriteJoined(sb, c.Args.Skip(2), o);
                sb.Append(')');
                return;
        }
        sb.Append(c.Name).Append('(');
        WriteJoined(sb, c.Args, o);
        sb.Append(')');
    }
}

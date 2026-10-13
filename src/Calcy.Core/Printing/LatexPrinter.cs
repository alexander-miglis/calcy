using System.Globalization;
using System.Text;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Core.Printing;

/// <summary>Turns expressions into LaTeX (for MCP results, notebook export and copying into documents).</summary>
public static class LatexPrinter
{
    private const int PLambda = 0, POr = 1, PAnd = 2, PNot = 3, PRel = 4, PAdd = 5, PMul = 6, PNeg = 7, PPow = 8, PPost = 9, PAtom = 10;

    private static readonly Dictionary<string, string> Greek = new(StringComparer.Ordinal)
    {
        ["alpha"] = @"\alpha", ["beta"] = @"\beta", ["gamma"] = @"\gamma", ["delta"] = @"\delta", ["epsilon"] = @"\varepsilon",
        ["zeta"] = @"\zeta", ["eta"] = @"\eta", ["theta"] = @"\theta", ["iota"] = @"\iota", ["kappa"] = @"\kappa",
        ["lambda"] = @"\lambda", ["mu"] = @"\mu", ["nu"] = @"\nu", ["xi"] = @"\xi", ["rho"] = @"\rho", ["sigma"] = @"\sigma",
        ["tau"] = @"\tau", ["upsilon"] = @"\upsilon", ["phi"] = @"\varphi", ["chi"] = @"\chi", ["psi"] = @"\psi", ["omega"] = @"\omega",
        ["Gamma"] = @"\Gamma", ["Delta"] = @"\Delta", ["Theta"] = @"\Theta", ["Lambda"] = @"\Lambda", ["Xi"] = @"\Xi", ["Pi"] = @"\Pi",
        ["Sigma"] = @"\Sigma", ["Phi"] = @"\Phi", ["Psi"] = @"\Psi", ["Omega"] = @"\Omega",
        ["α"] = @"\alpha", ["β"] = @"\beta", ["γ"] = @"\gamma", ["δ"] = @"\delta", ["ε"] = @"\varepsilon", ["ζ"] = @"\zeta",
        ["η"] = @"\eta", ["θ"] = @"\theta", ["ι"] = @"\iota", ["κ"] = @"\kappa", ["λ"] = @"\lambda", ["μ"] = @"\mu", ["ν"] = @"\nu",
        ["ξ"] = @"\xi", ["ρ"] = @"\rho", ["σ"] = @"\sigma", ["τ"] = @"\tau", ["υ"] = @"\upsilon", ["φ"] = @"\varphi", ["χ"] = @"\chi",
        ["ψ"] = @"\psi", ["ω"] = @"\omega", ["Γ"] = @"\Gamma", ["Δ"] = @"\Delta", ["Θ"] = @"\Theta", ["Λ"] = @"\Lambda", ["Ξ"] = @"\Xi",
        ["Π"] = @"\Pi", ["Σ"] = @"\Sigma", ["Φ"] = @"\Phi", ["Ψ"] = @"\Psi", ["Ω"] = @"\Omega",
    };

    private static readonly Dictionary<string, string> Operators = new(StringComparer.Ordinal)
    {
        ["sin"] = @"\sin", ["cos"] = @"\cos", ["tan"] = @"\tan", ["cot"] = @"\cot", ["sec"] = @"\sec", ["csc"] = @"\csc",
        ["asin"] = @"\arcsin", ["acos"] = @"\arccos", ["atan"] = @"\arctan", ["sinh"] = @"\sinh", ["cosh"] = @"\cosh",
        ["tanh"] = @"\tanh", ["coth"] = @"\coth", ["ln"] = @"\ln", ["log"] = @"\log", ["exp"] = @"\exp", ["det"] = @"\det",
        ["gcd"] = @"\gcd", ["min"] = @"\min", ["max"] = @"\max", ["arg"] = @"\arg",
    };

    /// <summary>The LaTeX for an expression, with numbers formatted using <paramref name="numbers"/> (15 digits by default).</summary>
    public static string ToLatex(Expr e, NumberFormatOptions? numbers = null)
    {
        var sb = new StringBuilder();
        Write(sb, e, 0, numbers ?? NumberFormatOptions.Ascii);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, Expr e, int minPrec, NumberFormatOptions o)
    {
        bool paren = Prec(e, o) < minPrec;
        if (paren) sb.Append(@"\left(");
        WriteCore(sb, e, o);
        if (paren) sb.Append(@"\right)");
    }

    private static int Prec(Expr e, NumberFormatOptions o) => e switch
    {
        NumberExpr n => NumberPrec(n.Value, o),
        ConstantExpr { Constant: ConstantKind.NegativeInfinity } => PNeg,
        AddExpr => PAdd,
        MulExpr m => IsNegative(m) ? PNeg : HasDenominator(m) ? PAtom : PMul,
        PowExpr pw => PowPrec(pw),
        RelationExpr => PRel,
        LogicExpr { Op: LogicOp.Not } => PNot,
        LogicExpr { Op: LogicOp.And } => PAnd,
        LogicExpr => POr,
        LambdaExpr => PLambda,
        PiecewiseExpr pw when pw.Otherwise is null && pw.Cases.Length == 1 => PAdd,
        CallExpr { Name: "factorial" or "doublefactorial" or "percent" or "deg" } => PPost,
        CallExpr { Name: "integrate" or "sum" or "product" or "limit" or "diff" } => PMul,
        CallExpr c when Operators.ContainsKey(c.Name) && c.Args.Length == 1 => PMul,
        _ => PAtom,
    };

    private static int PowPrec(PowExpr p)
    {
        if (p.Exponent is NumberExpr { Value.IsExactRational: true } ne)
        {
            var q = ne.Value.ReQ;
            if (q.IsNegative) return PAtom;                 // \frac{1}{x^n}
            if (q.Num.IsOne && q.Den > 1) return PAtom;     // \sqrt[n]{x}
        }
        return PPow;
    }

    private static int NumberPrec(Number n, NumberFormatOptions o)
    {
        if (!n.IsReal)
        {
            bool reZero = n.IsExact ? n.ReQ.IsZero : n.Re == 0;
            if (!reZero) return PAdd;
            return n.Im < 0 ? PNeg : PMul;
        }
        if (n.IsNegativeReal) return PNeg;
        if (!n.IsExact && NumberFormatter.Format(n.Re, o).Contains('e')) return PMul;
        return PAtom;
    }

    private static bool IsNegative(Expr e) => e switch
    {
        NumberExpr n => n.Value.IsNegativeReal,
        MulExpr m => m.Factors[0] is NumberExpr { Value.IsNegativeReal: true },
        ConstantExpr { Constant: ConstantKind.NegativeInfinity } => true,
        _ => false,
    };

    private static bool HasDenominator(MulExpr m) =>
        m.Factors.Any(f => f is PowExpr { Exponent: NumberExpr ne } && ne.Value.IsNegativeReal) ||
        m.Factors[0] is NumberExpr { Value.IsExact: true } n && n.Value.IsReal && !n.Value.ReQ.IsInteger;

    private static Expr Negate(Expr e) => e switch
    {
        NumberExpr n => new NumberExpr(-n.Value),
        MulExpr m when m.Factors[0] is NumberExpr { Value.IsMinusOne: true } && m.Factors.Length == 2 => m.Factors[1],
        MulExpr m when m.Factors[0] is NumberExpr { Value.IsMinusOne: true } => new MulExpr(m.Factors.RemoveAt(0)),
        MulExpr m when m.Factors[0] is NumberExpr c => new MulExpr(m.Factors.SetItem(0, new NumberExpr(-c.Value))),
        ConstantExpr { Constant: ConstantKind.NegativeInfinity } => ConstantExpr.Infinity,
        _ => e,
    };

    private static void WriteCore(StringBuilder sb, Expr e, NumberFormatOptions o)
    {
        switch (e)
        {
            case NumberExpr n:
                WriteNumber(sb, n.Value, o);
                break;
            case ConstantExpr c:
                sb.Append(c.Constant switch
                {
                    ConstantKind.Pi => @"\pi",
                    ConstantKind.E => "e",
                    ConstantKind.Infinity => @"\infty",
                    ConstantKind.NegativeInfinity => @"-\infty",
                    _ => @"\text{undefined}",
                });
                break;
            case SymbolExpr s:
                sb.Append(Name(s.Name));
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
                sb.Append(r.Op switch
                {
                    RelOp.Equal => " = ",
                    RelOp.NotEqual => @" \neq ",
                    RelOp.Less => " < ",
                    RelOp.LessEqual => @" \leq ",
                    RelOp.Greater => " > ",
                    RelOp.GreaterEqual => @" \geq ",
                    _ => " ? ",
                });
                Write(sb, r.Right, PRel + 1, o);
                break;
            case LogicExpr l:
                if (l.Op == LogicOp.Not)
                {
                    sb.Append(@"\lnot ");
                    Write(sb, l.Operands[0], PNot, o);
                    break;
                }
                string word = l.Op switch { LogicOp.And => @" \land ", LogicOp.Or => @" \lor ", _ => @" \oplus " };
                int prec = l.Op == LogicOp.And ? PAnd : POr;
                for (int i = 0; i < l.Operands.Length; i++)
                {
                    if (i > 0) sb.Append(word);
                    Write(sb, l.Operands[i], prec + 1, o);
                }
                break;
            case ListExpr list:
                sb.Append(@"\left\{");
                WriteJoined(sb, list.Items, o);
                sb.Append(@"\right\}");
                break;
            case TupleExpr t:
                sb.Append(@"\left(");
                WriteJoined(sb, t.Items, o);
                sb.Append(@"\right)");
                break;
            case MatrixExpr mx:
                sb.Append(@"\begin{bmatrix}");
                for (int r = 0; r < mx.Rows; r++)
                {
                    if (r > 0) sb.Append(@" \\ ");
                    for (int c = 0; c < mx.Cols; c++)
                    {
                        if (c > 0) sb.Append(" & ");
                        Write(sb, mx[r, c], 0, o);
                    }
                }
                sb.Append(@"\end{bmatrix}");
                break;
            case StringExpr str:
                sb.Append(@"\text{").Append(EscapeText(str.Value)).Append('}');
                break;
            case BoolExpr b:
                sb.Append(b.Value ? @"\text{true}" : @"\text{false}");
                break;
            case LambdaExpr lam:
                sb.Append(@"\left(").Append(string.Join(", ", lam.Parameters.Select(Name))).Append(@"\right) \mapsto ");
                Write(sb, lam.Body, 0, o);
                break;
            case PiecewiseExpr pw:
                if (pw.Otherwise is null && pw.Cases.Length == 1)
                {
                    Write(sb, pw.Cases[0].Value, PAdd, o);
                    sb.Append(@" \quad \left\{");
                    Write(sb, pw.Cases[0].Condition, 0, o);
                    sb.Append(@"\right\}");
                    break;
                }
                sb.Append(@"\begin{cases}");
                for (int i = 0; i < pw.Cases.Length; i++)
                {
                    if (i > 0) sb.Append(@" \\ ");
                    Write(sb, pw.Cases[i].Value, 0, o);
                    sb.Append(@" & \text{if } ");
                    Write(sb, pw.Cases[i].Condition, 0, o);
                }
                if (pw.Otherwise is not null)
                {
                    sb.Append(@" \\ ");
                    Write(sb, pw.Otherwise, 0, o);
                    sb.Append(@" & \text{otherwise}");
                }
                sb.Append(@"\end{cases}");
                break;
            case IndexExpr ix:
                Write(sb, ix.Target, PAtom, o);
                sb.Append("_{");
                WriteJoined(sb, ix.Indices, o);
                sb.Append('}');
                break;
            default:
                sb.Append(@"\text{").Append(e.Kind.ToString()).Append('}');
                break;
        }
    }

    /// <summary>A variable name in LaTeX: Greek letters as commands, x_1 as a subscript, long names upright.</summary>
    public static string Name(string name)
    {
        if (name is "h_bar" or "hbar" or "ħ") return @"\hbar";
        int us = name.IndexOf('_');
        if (us > 0 && us < name.Length - 1)
            return Name(name[..us]) + "_{" + Name(name[(us + 1)..]) + "}";
        if (Greek.TryGetValue(name, out var g)) return g;
        if (name.Length == 1) return name;
        if (name.All(char.IsDigit)) return name;
        return @"\mathrm{" + EscapeText(name) + "}";
    }

    private static string EscapeText(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\\': sb.Append(@"\textbackslash{}"); break;
                case '{': sb.Append(@"\{"); break;
                case '}': sb.Append(@"\}"); break;
                case '_': sb.Append(@"\_"); break;
                case '%': sb.Append(@"\%"); break;
                case '$': sb.Append(@"\$"); break;
                case '#': sb.Append(@"\#"); break;
                case '&': sb.Append(@"\&"); break;
                case '^': sb.Append(@"\^{}"); break;
                case '~': sb.Append(@"\~{}"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    private static void WriteJoined(StringBuilder sb, IEnumerable<Expr> items, NumberFormatOptions o)
    {
        bool first = true;
        foreach (var it in items)
        {
            if (!first) sb.Append(", ");
            Write(sb, it, 0, o);
            first = false;
        }
    }

    private static void WriteNumber(StringBuilder sb, Number n, NumberFormatOptions o)
    {
        if (n.IsReal)
        {
            if (n.IsExact)
            {
                var q = n.ReQ;
                if (q.IsInteger) sb.Append(q.Num.ToString(CultureInfo.InvariantCulture));
                else
                {
                    if (q.IsNegative) sb.Append('-');
                    sb.Append(@"\frac{").Append(BigIntegerAbs(q.Num)).Append("}{").Append(q.Den.ToString(CultureInfo.InvariantCulture)).Append('}');
                }
            }
            else WriteDecimal(sb, NumberFormatter.Format(n.Re, o));
            return;
        }
        bool reZero = n.IsExact ? n.ReQ.IsZero : n.Re == 0;
        var im = n.IsExact ? Number.Exact(n.ImQ) : Number.Approx(n.Im);
        if (!reZero)
        {
            WriteNumber(sb, n.IsExact ? Number.Exact(n.ReQ) : Number.Approx(n.Re), o);
            sb.Append(im.IsNegativeReal ? " - " : " + ");
            WriteImaginary(sb, im.IsNegativeReal ? -im : im, o);
        }
        else
        {
            if (im.IsNegativeReal)
            {
                sb.Append('-');
                im = -im;
            }
            WriteImaginary(sb, im, o);
        }
    }

    private static string BigIntegerAbs(System.Numerics.BigInteger v) =>
        System.Numerics.BigInteger.Abs(v).ToString(CultureInfo.InvariantCulture);

    private static void WriteImaginary(StringBuilder sb, Number im, NumberFormatOptions o)
    {
        if (im.IsOne) { sb.Append('i'); return; }
        WriteNumber(sb, im, o);
        sb.Append('i');
    }

    private static void WriteDecimal(StringBuilder sb, string text)
    {
        int e = text.IndexOf('e');
        if (e < 0)
        {
            sb.Append(text);
            return;
        }
        var mantissa = text[..e];
        var exponent = text[(e + 1)..].TrimStart('+');
        if (mantissa != "1") sb.Append(mantissa).Append(@" \times ");
        sb.Append("10^{").Append(exponent).Append('}');
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

    private static void WriteAdd(StringBuilder sb, AddExpr a, NumberFormatOptions o)
    {
        // Same reading order as the text printer, so LaTeX and the screen agree.
        var terms = ExprPrinter.DisplayTerms(a);
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

    private static void WriteMul(StringBuilder sb, MulExpr m, NumberFormatOptions o)
    {
        var num = new List<Expr>();
        var den = new List<Expr>();
        bool negative = false;
        for (int i = 0; i < m.Factors.Length; i++)
        {
            var f = m.Factors[i];
            if (i == 0 && f is NumberExpr n && n.Value.IsReal)
            {
                var v = n.Value;
                if (v.IsNegativeReal) { negative = true; v = -v; }
                if (v.IsExact && !v.ReQ.IsInteger)
                {
                    if (!v.ReQ.Num.IsOne) num.Add(new NumberExpr(Number.Exact(v.ReQ.Num)));
                    den.Add(new NumberExpr(Number.Exact(v.ReQ.Den)));
                }
                else if (!v.IsOne) num.Add(new NumberExpr(v));
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
        if (negative) sb.Append('-');
        if (den.Count == 0)
        {
            WriteFactors(sb, num, o);
            return;
        }
        sb.Append(@"\frac{");
        if (num.Count == 0) sb.Append('1');
        else WriteFactors(sb, num, o, insideFraction: true);
        sb.Append("}{");
        WriteFactors(sb, den, o, insideFraction: true);
        sb.Append('}');
    }

    private static void WriteFactors(StringBuilder sb, List<Expr> factors, NumberFormatOptions o, bool insideFraction = false)
    {
        if (insideFraction && factors.Count == 1)
        {
            Write(sb, factors[0], 0, o);
            return;
        }
        for (int i = 0; i < factors.Count; i++)
        {
            if (i > 0) sb.Append(Separator(factors[i - 1], factors[i]));
            Write(sb, factors[i], PMul + 1, o);
        }
    }

    private static string Separator(Expr left, Expr right)
    {
        if (right is NumberExpr) return @" \cdot ";
        if (right is PowExpr or CallExpr && ToLatex(right) is { Length: > 0 } text && char.IsDigit(text[0])) return @" \cdot ";
        if (left is NumberExpr) return " ";
        if (left is SymbolExpr or ConstantExpr or PowExpr && right is SymbolExpr or ConstantExpr or PowExpr) return " ";
        if (right is CallExpr or AddExpr) return " ";
        return @" \cdot ";
    }

    private static void WritePow(StringBuilder sb, PowExpr p, NumberFormatOptions o)
    {
        if (p.Exponent is NumberExpr { Value.IsExactRational: true } ne)
        {
            var q = ne.Value.ReQ;
            if (q.IsNegative)
            {
                sb.Append(@"\frac{1}{");
                if ((-q).IsOne) Write(sb, p.Base, 0, o);
                else WritePow(sb, new PowExpr(p.Base, new NumberExpr(Number.Exact(-q))), o);
                sb.Append('}');
                return;
            }
            if (q.Num.IsOne && q.Den > 1)
            {
                sb.Append(@"\sqrt");
                if (q.Den != 2) sb.Append('[').Append(q.Den.ToString(CultureInfo.InvariantCulture)).Append(']');
                sb.Append('{');
                Write(sb, p.Base, 0, o);
                sb.Append('}');
                return;
            }
        }
        // sin(x)^2 reads better as \sin^{2}(x).
        if (p.Base is CallExpr c && Operators.TryGetValue(c.Name, out var op) && c.Args.Length == 1 && p.Exponent is NumberExpr { Value.IsExactInteger: true, Value.IsNegativeReal: false })
        {
            sb.Append(op).Append("^{");
            Write(sb, p.Exponent, 0, o);
            sb.Append(@"}\left(");
            Write(sb, c.Args[0], 0, o);
            sb.Append(@"\right)");
            return;
        }
        Write(sb, p.Base, PPost, o);
        sb.Append("^{");
        Write(sb, p.Exponent, 0, o);
        sb.Append('}');
    }

    private static void WriteCall(StringBuilder sb, CallExpr c, NumberFormatOptions o)
    {
        var a = c.Args;
        switch (c.Name)
        {
            case "abs" when a.Length == 1:
                sb.Append(@"\left|");
                Write(sb, a[0], 0, o);
                sb.Append(@"\right|");
                return;
            case "floor" when a.Length == 1:
                sb.Append(@"\left\lfloor ");
                Write(sb, a[0], 0, o);
                sb.Append(@" \right\rfloor");
                return;
            case "ceil" when a.Length == 1:
                sb.Append(@"\left\lceil ");
                Write(sb, a[0], 0, o);
                sb.Append(@" \right\rceil");
                return;
            case "conj" when a.Length == 1:
                sb.Append(@"\overline{");
                Write(sb, a[0], 0, o);
                sb.Append('}');
                return;
            case "factorial" when a.Length == 1:
                Write(sb, a[0], PPost + 1, o);
                sb.Append('!');
                return;
            case "doublefactorial" when a.Length == 1:
                Write(sb, a[0], PPost + 1, o);
                sb.Append("!!");
                return;
            case "percent" when a.Length == 1:
                Write(sb, a[0], PPost + 1, o);
                sb.Append(@"\%");
                return;
            case "deg" when a.Length == 1:
                Write(sb, a[0], PPost + 1, o);
                sb.Append(@"^{\circ}");
                return;
            case "sqrt" when a.Length == 1:
                sb.Append(@"\sqrt{");
                Write(sb, a[0], 0, o);
                sb.Append('}');
                return;
            case "root" when a.Length == 2:
                sb.Append(@"\sqrt[");
                Write(sb, a[1], 0, o);
                sb.Append("]{");
                Write(sb, a[0], 0, o);
                sb.Append('}');
                return;
            case "nCr" when a.Length == 2:
                sb.Append(@"\binom{");
                Write(sb, a[0], 0, o);
                sb.Append("}{");
                Write(sb, a[1], 0, o);
                sb.Append('}');
                return;
            case "log" when a.Length == 2:
                sb.Append(@"\log_{");
                Write(sb, a[1], 0, o);
                sb.Append(@"}\left(");
                Write(sb, a[0], 0, o);
                sb.Append(@"\right)");
                return;
            case "diff" when a.Length is 2 or 3 && a[1] is SymbolExpr dv:
            {
                string order = a.Length == 3 ? "^{" + ToLatex(a[2], o) + "}" : "";
                sb.Append(@"\frac{d").Append(order).Append("}{d").Append(Name(dv.Name)).Append(order).Append(@"}\left(");
                Write(sb, a[0], 0, o);
                sb.Append(@"\right)");
                return;
            }
            case "integrate" when a.Length is 2 or 4 && a[1] is SymbolExpr iv:
                sb.Append(@"\int");
                if (a.Length == 4)
                {
                    sb.Append("_{");
                    Write(sb, a[2], 0, o);
                    sb.Append("}^{");
                    Write(sb, a[3], 0, o);
                    sb.Append('}');
                }
                sb.Append(' ');
                Write(sb, a[0], PAdd + 1, o);
                sb.Append(@" \, d").Append(Name(iv.Name));
                return;
            case "sum" or "product" when a.Length == 4 && a[1] is SymbolExpr sv:
                sb.Append(c.Name == "sum" ? @"\sum_{" : @"\prod_{").Append(Name(sv.Name)).Append('=');
                Write(sb, a[2], 0, o);
                sb.Append("}^{");
                Write(sb, a[3], 0, o);
                sb.Append("} ");
                Write(sb, a[0], PMul + 1, o);
                return;
            case "limit" when a.Length >= 3 && a[1] is SymbolExpr lv:
                sb.Append(@"\lim_{").Append(Name(lv.Name)).Append(@" \to ");
                Write(sb, a[2], 0, o);
                if (a.Length >= 4 && a[3] is StringExpr { Value: var dir }) sb.Append(dir.StartsWith('-') || dir.StartsWith('l') ? "^{-}" : "^{+}");
                else if (a.Length >= 4 && a[3] is NumberExpr dn) sb.Append(dn.Value.IsNegativeReal ? "^{-}" : "^{+}");
                sb.Append("} ");
                Write(sb, a[0], PMul + 1, o);
                return;
            case "fderiv" when a.Length >= 2 && a[0] is SymbolExpr f && a[1] is NumberExpr order:
                sb.Append(Name(f.Name)).Append(new string('\'', (int)order.Value.Re)).Append(@"\left(");
                WriteJoined(sb, a.Skip(2), o);
                sb.Append(@"\right)");
                return;
        }
        if (Operators.TryGetValue(c.Name, out var op))
        {
            sb.Append(op);
            sb.Append(@"\left(");
            WriteJoined(sb, a, o);
            sb.Append(@"\right)");
            return;
        }
        sb.Append(c.Name.Length == 1 ? c.Name : @"\operatorname{" + EscapeText(c.Name) + "}");
        sb.Append(@"\left(");
        WriteJoined(sb, a, o);
        sb.Append(@"\right)");
    }
}

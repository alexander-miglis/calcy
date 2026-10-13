using System.Numerics;
using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;
using Calcy.Core.Parsing;
using Calcy.Core.Printing;

namespace Calcy.UI.Math.Model;

/// <summary>
/// Builds an editable model from parsed input. The model keeps the structure (term order, explicit
/// multiplication, divisions) so that serializing it gives text that parses back to the same expression tree.
/// </summary>
public static class MathInputBuilder
{
    private const int LLambda = 0, LOr = 1, LAnd = 2, LNot = 3, LRel = 4, LAdd = 5, LMul = 6, LUnary = 7, LPow = 8, LPost = 9, LAtom = 10;

    /// <summary>Parses linear text and builds the model; returns false (and a plain-text row) when it does not parse.</summary>
    public static bool TryBuild(string text, IParseContext? context, out MathRow row)
    {
        var st = MathParsing.TryParseStatement(text, context, out _);
        if (st is null)
        {
            row = PlainText(text);
            return string.IsNullOrWhiteSpace(text);
        }
        try
        {
            row = FromStatement(st, context);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or InsufficientExecutionStackException)
        {
            row = PlainText(text);
            return false;
        }
    }

    /// <summary>A row holding the characters of <paramref name="text"/> as typed (no structure).</summary>
    public static MathRow PlainText(string text)
    {
        var row = new MathRow();
        foreach (var ch in text) row.Add(new MathChar(ch.ToString()));
        return row;
    }

    /// <summary>Builds a row from a parsed statement (expression, assignment or function definition).</summary>
    public static MathRow FromStatement(Statement statement, IParseContext? context = null)
    {
        var row = new MathRow();
        switch (statement)
        {
            case ExprStatement e:
                Emit(row, e.Expression, 0);
                // A comparison x == 1 must not turn into the definition x = 1.
                if (e.Expression is RelationExpr { Op: RelOp.Equal } && LooksLikeDefinition(row, context))
                {
                    int at = row.Items.ToList().FindIndex(n => n is MathChar { Text: "=" });
                    if (at >= 0)
                    {
                        row.RemoveAt(at);
                        row.Insert(at, new MathChar("=="));
                    }
                }
                break;
            case AssignStatement a:
            {
                var form = AssignmentForm(a.Source);
                if (form == "→")
                {
                    Emit(row, a.Value, 0);
                    row.Add(new MathChar("→"));
                    EmitName(row, a.Name);
                }
                else
                {
                    EmitName(row, a.Name);
                    row.Add(new MathChar(form));
                    Emit(row, a.Value, 0);
                }
                break;
            }
            case FunctionDefStatement f:
            {
                EmitName(row, f.Name);
                var p = new MathTemplate(MathTemplateKind.Parens);
                for (int i = 0; i < f.Parameters.Length; i++)
                {
                    if (i > 0) p[0].Add(new MathChar(","));
                    EmitName(p[0], f.Parameters[i]);
                }
                row.Add(p);
                row.Add(new MathChar(AssignmentForm(f.Source)));
                Emit(row, f.Body, 0);
                break;
            }
        }
        return row;
    }

    /// <summary>Builds a row from an expression.</summary>
    public static MathRow FromExpr(Expr e)
    {
        var row = new MathRow();
        Emit(row, e, 0);
        return row;
    }

    private static bool LooksLikeDefinition(MathRow row, IParseContext? context)
    {
        var st = MathParsing.TryParseStatement(MathSerializer.Serialize(row, context), context, out _);
        return st is AssignStatement or FunctionDefStatement;
    }

    private static string AssignmentForm(string source)
    {
        try
        {
            var tokens = Lexer.Tokenize(source);
            if (tokens.Count >= 3 && tokens[^2].Kind == TokenKind.Identifier && tokens[^3].Kind == TokenKind.Arrow) return "→";
            if (tokens.Any(t => t.Kind == TokenKind.Assign)) return "≔";
        }
        catch (CalcException)
        {
        }
        return "=";
    }

    private static int Level(Expr e) => e switch
    {
        LambdaExpr => LLambda,
        LogicExpr { Op: LogicOp.Not } => LNot,
        LogicExpr { Op: LogicOp.And } => LAnd,
        LogicExpr => LOr,
        RelationExpr => LRel,
        AddExpr => LAdd,
        MulExpr => LMul,
        NumberExpr n => NumberLevel(n.Value),
        ConstantExpr { Constant: ConstantKind.NegativeInfinity } => LUnary,
        PowExpr => LPow,
        CallExpr { Name: "factorial" or "doublefactorial" or "percent" or "deg", Args.Length: 1 } => LPost,
        _ => LAtom,
    };

    private static int NumberLevel(Number n)
    {
        if (!n.IsReal) return LAdd;
        if (n.IsExact)
        {
            var q = n.ReQ;
            if (!q.IsInteger && !IsTerminating(q.Den)) return LMul;
            return q.IsNegative ? LUnary : LAtom;
        }
        return n.Re < 0 ? LUnary : LAtom;
    }

    private static void Emit(MathRow row, Expr e, int minLevel)
    {
        if (Level(e) < minLevel)
        {
            var p = new MathTemplate(MathTemplateKind.Parens);
            EmitCore(p[0], e);
            row.Add(p);
        }
        else EmitCore(row, e);
    }

    private static void EmitCore(MathRow row, Expr e)
    {
        switch (e)
        {
            case NumberExpr n:
                EmitNumber(row, n.Value);
                break;
            case ConstantExpr c:
                switch (c.Constant)
                {
                    case ConstantKind.Pi: row.Add(new MathChar("π")); break;
                    case ConstantKind.E: row.Add(new MathChar("e")); break;
                    case ConstantKind.Infinity: row.Add(new MathChar("∞")); break;
                    case ConstantKind.NegativeInfinity: row.Add(new MathChar("-")); row.Add(new MathChar("∞")); break;
                    default: Chars(row, "undefined"); break;
                }
                break;
            case SymbolExpr s:
                EmitName(row, s.Name);
                break;
            case AddExpr a:
                EmitAdd(row, a);
                break;
            case MulExpr m:
                EmitMul(row, m);
                break;
            case PowExpr p:
            {
                Emit(row, p.Base, LPost);
                var t = new MathTemplate(MathTemplateKind.Power);
                EmitCore(t[0], p.Exponent);
                row.Add(t);
                break;
            }
            case CallExpr c:
                EmitCall(row, c);
                break;
            case RelationExpr r:
                Emit(row, r.Left, LAdd);
                row.Add(new MathChar(RelationExpr.Symbol(r.Op)));
                Emit(row, r.Right, LAdd);
                break;
            case LogicExpr l:
                EmitLogic(row, l);
                break;
            case ListExpr list:
            {
                var t = new MathTemplate(MathTemplateKind.Braces);
                EmitJoined(t[0], list.Items);
                row.Add(t);
                break;
            }
            case TupleExpr tuple:
            {
                var t = new MathTemplate(MathTemplateKind.Parens);
                EmitJoined(t[0], tuple.Items);
                row.Add(t);
                break;
            }
            case MatrixExpr mx:
            {
                var t = new MathTemplate(MathTemplateKind.Matrix, mx.Rows, mx.Cols);
                for (int r = 0; r < mx.Rows; r++)
                    for (int c = 0; c < mx.Cols; c++)
                        Emit(t.Cell(r, c), mx[r, c], 0);
                row.Add(t);
                break;
            }
            case StringExpr str:
                row.Add(new MathChar(QuoteString(str.Value)));
                break;
            case BoolExpr b:
                Chars(row, b.Value ? "true" : "false");
                break;
            case LambdaExpr lam:
            {
                if (lam.Parameters.Length == 1) EmitName(row, lam.Parameters[0]);
                else
                {
                    var p = new MathTemplate(MathTemplateKind.Parens);
                    for (int i = 0; i < lam.Parameters.Length; i++)
                    {
                        if (i > 0) p[0].Add(new MathChar(","));
                        EmitName(p[0], lam.Parameters[i]);
                    }
                    row.Add(p);
                }
                row.Add(new MathChar("⇒"));
                Emit(row, lam.Body, 0);
                break;
            }
            case PiecewiseExpr pw:
            {
                int rows = pw.Cases.Length + (pw.Otherwise is null ? 0 : 1);
                var t = new MathTemplate(MathTemplateKind.Piecewise, System.Math.Max(1, rows));
                for (int i = 0; i < pw.Cases.Length; i++)
                {
                    Emit(t.Cell(i, 0), pw.Cases[i].Value, 0);
                    Emit(t.Cell(i, 1), pw.Cases[i].Condition, 0);
                }
                if (pw.Otherwise is not null) Emit(t.Cell(pw.Cases.Length, 0), pw.Otherwise, 0);
                row.Add(t);
                break;
            }
            case IndexExpr ix:
            {
                Emit(row, ix.Target, LAtom);
                var t = new MathTemplate(MathTemplateKind.Brackets);
                EmitJoined(t[0], ix.Indices);
                row.Add(t);
                break;
            }
            default:
                Chars(row, ExprPrinter.ToInput(e));
                break;
        }
    }

    internal static string QuoteString(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";

    private static void EmitJoined(MathRow row, IEnumerable<Expr> items)
    {
        bool first = true;
        foreach (var it in items)
        {
            if (!first) row.Add(new MathChar(","));
            Emit(row, it, 0);
            first = false;
        }
    }

    private static void EmitAdd(MathRow row, AddExpr a)
    {
        for (int i = 0; i < a.Terms.Length; i++)
        {
            var t = a.Terms[i];
            if (i == 0)
            {
                Emit(row, t, LMul);
                continue;
            }
            if (t is MulExpr { Factors.Length: 2 } m && m.Factors[0] is NumberExpr { Value.IsMinusOne: true })
            {
                row.Add(new MathChar("-"));
                Emit(row, m.Factors[1], LMul);
            }
            else if (t is NumberExpr n && n.Value.IsNegativeReal)
            {
                row.Add(new MathChar("-"));
                EmitNumber(row, -n.Value);
            }
            else
            {
                row.Add(new MathChar("+"));
                Emit(row, t, LMul);
            }
        }
    }

    private enum Prev { None, Number, Other, Fraction, Minus }

    private static void EmitMul(MathRow row, MulExpr m)
    {
        var pending = new MathRow();
        var prev = Prev.None;
        var f = m.Factors;
        for (int i = 0; i < f.Length; i++)
        {
            var factor = f[i];
            if (i > 0 && factor is PowExpr { Exponent: NumberExpr { Value.IsMinusOne: true } } div)
            {
                var frac = new MathTemplate(MathTemplateKind.Fraction);
                bool negative = pending.Count > 0 && pending[0] is MathChar { Text: "-" };
                var moved = pending.RemoveRange(negative ? 1 : 0, pending.Count - (negative ? 1 : 0));
                frac[0].InsertRange(0, moved);
                Emit(frac[1], div.Base, 0);
                pending.Add(frac);
                prev = Prev.Fraction;
                continue;
            }
            if (i == 0)
            {
                if (factor is NumberExpr { Value.IsMinusOne: true } && f.Length >= 2 && f[1] is not NumberExpr
                    && f[1] is not PowExpr { Exponent: NumberExpr { Value.IsMinusOne: true } })
                {
                    pending.Add(new MathChar("-"));
                    prev = Prev.Minus;
                    continue;
                }
                Emit(pending, factor, LUnary);
                prev = factor is NumberExpr { Value.IsReal: true } && Level(factor) >= LUnary ? Prev.Number : Prev.Other;
                continue;
            }
            if (prev == Prev.Minus)
            {
                Emit(pending, factor, LPow);
            }
            else if (prev == Prev.Number && i == 1 && !StartsWithDigit(factor))
            {
                Emit(pending, factor, LPow);
            }
            else
            {
                pending.Add(new MathChar("·"));
                Emit(pending, factor, LUnary);
            }
            prev = Prev.Other;
        }
        row.InsertRange(row.Count, pending.RemoveRange(0, pending.Count));
    }

    private static bool StartsWithDigit(Expr e) => e switch
    {
        NumberExpr n => n.Value.IsReal && !n.Value.IsNegativeReal,
        PowExpr p => Level(p.Base) >= LPost && StartsWithDigit(p.Base),
        CallExpr { Name: "factorial" or "doublefactorial" or "percent" or "deg", Args.Length: 1 } c => Level(c.Args[0]) >= LAtom && StartsWithDigit(c.Args[0]),
        IndexExpr ix => StartsWithDigit(ix.Target),
        _ => false,
    };

    private static void EmitLogic(MathRow row, LogicExpr l)
    {
        if (l.Op == LogicOp.Not)
        {
            row.Add(new MathChar("not"));
            Emit(row, l.Operands[0], LNot);
            return;
        }
        if (l.Op == LogicOp.And && IsChain(l))
        {
            var first = (RelationExpr)l.Operands[0];
            Emit(row, first.Left, LAdd);
            foreach (RelationExpr r in l.Operands)
            {
                row.Add(new MathChar(RelationExpr.Symbol(r.Op)));
                Emit(row, r.Right, LAdd);
            }
            return;
        }
        string word = l.Op switch { LogicOp.And => "and", LogicOp.Or => "or", _ => "xor" };
        int prec = l.Op == LogicOp.And ? LAnd : LOr;
        for (int i = 0; i < l.Operands.Length; i++)
        {
            if (i > 0) row.Add(new MathChar(word));
            Emit(row, l.Operands[i], i == 0 ? prec : prec + 1);
        }
    }

    private static bool IsChain(LogicExpr l)
    {
        if (l.Operands.Length < 2 || l.Operands.Any(o => o is not RelationExpr)) return false;
        for (int i = 0; i + 1 < l.Operands.Length; i++)
            if (!((RelationExpr)l.Operands[i]).Right.Equals(((RelationExpr)l.Operands[i + 1]).Left)) return false;
        return true;
    }

    private static void EmitCall(MathRow row, CallExpr c)
    {
        var a = c.Args;
        switch (c.Name)
        {
            case "sqrt" when a.Length == 1:
                row.Add(Template(MathTemplateKind.Sqrt, a[0]));
                return;
            case "cbrt" when a.Length == 1:
            {
                var t = new MathTemplate(MathTemplateKind.NthRoot) { FunctionName = "cbrt" };
                t[0].Add(new MathChar("3"));
                Emit(t[1], a[0], 0);
                row.Add(t);
                return;
            }
            case "root" when a.Length == 2:
            {
                var t = new MathTemplate(MathTemplateKind.NthRoot);
                Emit(t[0], a[1], 0);
                Emit(t[1], a[0], 0);
                row.Add(t);
                return;
            }
            case "abs" when a.Length == 1:
                row.Add(Template(MathTemplateKind.Abs, a[0]));
                return;
            case "log" when a.Length == 2:
                row.Add(Template(MathTemplateKind.LogBase, a[1], a[0]));
                return;
            case "integrate" when a.Length == 2 && a[1] is SymbolExpr:
                row.Add(Template(MathTemplateKind.Integral, a[0], a[1]));
                return;
            case "integrate" when a.Length == 4 && a[1] is SymbolExpr:
                row.Add(Template(MathTemplateKind.DefiniteIntegral, a[2], a[3], a[0], a[1]));
                return;
            case "diff" when a.Length == 2 && a[1] is SymbolExpr:
                row.Add(Template(MathTemplateKind.Derivative, a[1], a[0]));
                return;
            case "sum" or "product" or "prod" when a.Length == 4 && a[1] is SymbolExpr:
            {
                var kind = c.Name == "sum" ? MathTemplateKind.Sum : MathTemplateKind.Product;
                var t = Template(kind, a[1], a[2], a[3], a[0]);
                if (c.Name == "prod") t.FunctionName = "prod";
                row.Add(t);
                return;
            }
            case "limit" when a.Length == 3 && a[1] is SymbolExpr:
                row.Add(Template(MathTemplateKind.Limit, a[1], a[2], a[0]));
                return;
            case "factorial" when a.Length == 1:
                Emit(row, a[0], LAtom);
                row.Add(new MathChar("!"));
                return;
            case "doublefactorial" when a.Length == 1:
                Emit(row, a[0], LAtom);
                row.Add(new MathChar("!"));
                row.Add(new MathChar("!"));
                return;
            case "percent" when a.Length == 1:
                Emit(row, a[0], LAtom);
                row.Add(new MathChar("%"));
                return;
            case "deg" when a.Length == 1:
                Emit(row, a[0], LAtom);
                row.Add(new MathChar("°"));
                return;
            case "fderiv" when a.Length >= 2 && a[0] is SymbolExpr f && a[1] is NumberExpr { Value.IsExactInteger: true } order && order.Value.Re is >= 1 and <= 10:
            {
                EmitName(row, f.Name);
                for (int k = 0; k < (int)order.Value.Re; k++) row.Add(new MathChar("′"));
                var p = new MathTemplate(MathTemplateKind.Parens);
                EmitJoined(p[0], a.Skip(2));
                row.Add(p);
                return;
            }
        }
        EmitName(row, c.Name);
        var args = new MathTemplate(MathTemplateKind.Parens);
        EmitJoined(args[0], a);
        row.Add(args);
    }

    private static MathTemplate Template(MathTemplateKind kind, params Expr[] slots)
    {
        var t = new MathTemplate(kind);
        for (int i = 0; i < slots.Length; i++) Emit(t[i], slots[i], 0);
        return t;
    }

    /// <summary>Writes a name as letters, turning v_0 into v with a subscript 0.</summary>
    internal static void EmitName(MathRow row, string name)
    {
        int u = name.IndexOf('_');
        if (u <= 0 || u == name.Length - 1)
        {
            Chars(row, name);
            return;
        }
        Chars(row, name[..u]);
        var sub = new MathTemplate(MathTemplateKind.Subscript);
        Chars(sub[0], name[(u + 1)..]);
        row.Add(sub);
    }

    internal static void Chars(MathRow row, string text)
    {
        var e = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext()) row.Add(new MathChar((string)e.Current));
    }

    private static void EmitNumber(MathRow row, Number n)
    {
        if (!n.IsReal)
        {
            Chars(row, ExprPrinter.ToInput(new NumberExpr(n)));
            return;
        }
        if (!n.IsExact)
        {
            double x = n.Re;
            if (double.IsPositiveInfinity(x)) { row.Add(new MathChar("∞")); return; }
            if (double.IsNegativeInfinity(x)) { row.Add(new MathChar("-")); row.Add(new MathChar("∞")); return; }
            if (double.IsNaN(x)) { Chars(row, "undefined"); return; }
            Chars(row, x.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            return;
        }
        var q = n.ReQ;
        if (q.IsInteger)
        {
            Chars(row, q.Num.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return;
        }
        if (IsTerminating(q.Den))
        {
            Chars(row, ExactDecimal(q));
            return;
        }
        if (q.IsNegative) row.Add(new MathChar("-"));
        var frac = new MathTemplate(MathTemplateKind.Fraction);
        Chars(frac[0], BigInteger.Abs(q.Num).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Chars(frac[1], q.Den.ToString(System.Globalization.CultureInfo.InvariantCulture));
        row.Add(frac);
    }

    internal static bool IsTerminating(BigInteger den)
    {
        if (den.IsZero) return false;
        var d = BigInteger.Abs(den);
        while (d % 2 == 0) d /= 2;
        while (d % 5 == 0) d /= 5;
        return d.IsOne;
    }

    /// <summary>Exact decimal text of a rational whose denominator has only the factors 2 and 5.</summary>
    internal static string ExactDecimal(BigRational q)
    {
        var den = q.Den;
        int twos = 0, fives = 0;
        var d = den;
        while (d % 2 == 0) { d /= 2; twos++; }
        while (d % 5 == 0) { d /= 5; fives++; }
        int digits = System.Math.Max(twos, fives);
        var scaled = BigInteger.Abs(q.Num) * BigInteger.Pow(10, digits) / den;
        var s = scaled.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(digits + 1, '0');
        var text = s[..^digits] + "." + s[^digits..];
        return (q.IsNegative ? "-" : "") + text;
    }
}

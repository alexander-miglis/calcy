using System.Collections.Immutable;
using Calcy.Core.Numbers;

namespace Calcy.Core.Expressions;

public enum ExprKind
{
    Number,
    Constant,
    Symbol,
    Add,
    Mul,
    Pow,
    Call,
    Relation,
    Logic,
    List,
    Matrix,
    Tuple,
    String,
    Bool,
    Lambda,
    Piecewise,
    Index,
}

/// <summary>Immutable expression tree node. Equality is structural.</summary>
public abstract class Expr : IEquatable<Expr>
{
    private int _hash;

    public abstract ExprKind Kind { get; }
    public abstract IReadOnlyList<Expr> Children { get; }

    /// <summary>Returns a node of the same type with new children (no simplification).</summary>
    public abstract Expr WithChildren(IReadOnlyList<Expr> children);

    protected abstract int ComputeHash();
    protected abstract bool StructuralEquals(Expr other);

    public override int GetHashCode()
    {
        if (_hash == 0)
        {
            int h = ComputeHash();
            _hash = h == 0 ? 1 : h;
        }
        return _hash;
    }

    public bool Equals(Expr? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null || other.Kind != Kind) return false;
        if (GetHashCode() != other.GetHashCode()) return false;
        return StructuralEquals(other);
    }

    public override bool Equals(object? obj) => obj is Expr e && Equals(e);
    public static bool operator ==(Expr? a, Expr? b) => a is null ? b is null : a.Equals(b);
    public static bool operator !=(Expr? a, Expr? b) => !(a == b);

    public override string ToString() => Printing.ExprPrinter.ToText(this);

    public bool IsNumber => Kind == ExprKind.Number;
    public bool IsZero => this is NumberExpr { Value.IsZero: true };
    public bool IsOne => this is NumberExpr { Value.IsOne: true };

    /// <summary>True when the expression tree contains no free symbols (numbers and constants only).</summary>
    public bool IsConstantValue()
    {
        switch (this)
        {
            case NumberExpr or ConstantExpr: return true;
            case SymbolExpr: return false;
            case CallExpr c: return c.Args.All(a => a.IsConstantValue());
            case AddExpr or MulExpr or PowExpr: return Children.All(c => c.IsConstantValue());
            default: return false;
        }
    }

    /// <summary>True when the symbol occurs anywhere in the tree (lambda parameters shadow).</summary>
    public bool ContainsSymbol(string name)
    {
        if (this is SymbolExpr s) return s.Name == name;
        if (this is LambdaExpr l && l.Parameters.Contains(name)) return false;
        foreach (var c in Children)
            if (c.ContainsSymbol(name)) return true;
        return false;
    }

    public bool ContainsKind(ExprKind kind)
    {
        if (Kind == kind) return true;
        foreach (var c in Children)
            if (c.ContainsKind(kind)) return true;
        return false;
    }

    /// <summary>Collects free symbol names in order of first appearance.</summary>
    public IReadOnlyList<string> FreeSymbols()
    {
        var list = new List<string>();
        var seen = new HashSet<string>();
        Collect(this, ImmutableHashSet<string>.Empty);
        return list;

        void Collect(Expr e, ImmutableHashSet<string> bound)
        {
            switch (e)
            {
                case SymbolExpr s:
                    if (!bound.Contains(s.Name) && seen.Add(s.Name)) list.Add(s.Name);
                    break;
                case LambdaExpr l:
                    Collect(l.Body, bound.Union(l.Parameters));
                    break;
                default:
                    foreach (var c in e.Children) Collect(c, bound);
                    break;
            }
        }
    }

    /// <summary>Applies a bottom-up rewrite without simplification.</summary>
    public Expr Map(Func<Expr, Expr> f)
    {
        var children = Children;
        if (children.Count == 0) return f(this);
        Expr[]? newChildren = null;
        for (int i = 0; i < children.Count; i++)
        {
            var nc = children[i].Map(f);
            if (!ReferenceEquals(nc, children[i]))
            {
                newChildren ??= children.ToArray();
                newChildren[i] = nc;
            }
        }
        return f(newChildren is null ? this : WithChildren(newChildren));
    }

    /// <summary>Counts nodes; useful to bound the size of results.</summary>
    public int NodeCount()
    {
        int n = 1;
        foreach (var c in Children) n += c.NodeCount();
        return n;
    }

    // Convenience factories (no simplification). Use Algebra for simplifying constructors.
    public static NumberExpr Num(Number n) => new(n);
    public static NumberExpr Int(long v) => new(Number.FromInt(v));
    public static NumberExpr Rational(long n, long d) => new(Number.Exact(new BigRational(n, d)));
    public static SymbolExpr Sym(string name) => new(name);

    public static readonly NumberExpr Zero = Int(0);
    public static readonly NumberExpr One = Int(1);
    public static readonly NumberExpr MinusOne = Int(-1);
    public static readonly NumberExpr Two = Int(2);
    public static readonly NumberExpr HalfExpr = Rational(1, 2);
    public static readonly NumberExpr ImaginaryUnit = new(Number.I);
}

public sealed class NumberExpr(Number value) : Expr
{
    public Number Value { get; } = value;
    public override ExprKind Kind => ExprKind.Number;
    public override IReadOnlyList<Expr> Children => [];
    public override Expr WithChildren(IReadOnlyList<Expr> children) => this;
    protected override int ComputeHash() => HashCode.Combine(1, Value);
    protected override bool StructuralEquals(Expr other) => Value.Equals(((NumberExpr)other).Value);
}

public enum ConstantKind
{
    Pi,
    E,
    Infinity,
    NegativeInfinity,
    Undefined,
}

public sealed class ConstantExpr(ConstantKind constant) : Expr
{
    public ConstantKind Constant { get; } = constant;
    public static readonly ConstantExpr Pi = new(ConstantKind.Pi);
    public static readonly ConstantExpr E = new(ConstantKind.E);
    public static readonly ConstantExpr Infinity = new(ConstantKind.Infinity);
    public static readonly ConstantExpr NegativeInfinity = new(ConstantKind.NegativeInfinity);
    public static readonly ConstantExpr Undefined = new(ConstantKind.Undefined);

    public override ExprKind Kind => ExprKind.Constant;
    public override IReadOnlyList<Expr> Children => [];
    public override Expr WithChildren(IReadOnlyList<Expr> children) => this;
    protected override int ComputeHash() => HashCode.Combine(2, Constant);
    protected override bool StructuralEquals(Expr other) => Constant == ((ConstantExpr)other).Constant;

    public double ApproxValue => Constant switch
    {
        ConstantKind.Pi => Math.PI,
        ConstantKind.E => Math.E,
        ConstantKind.Infinity => double.PositiveInfinity,
        ConstantKind.NegativeInfinity => double.NegativeInfinity,
        _ => double.NaN,
    };

    public bool IsInfinite => Constant is ConstantKind.Infinity or ConstantKind.NegativeInfinity;
}

public sealed class SymbolExpr(string name) : Expr
{
    public string Name { get; } = name;
    public override ExprKind Kind => ExprKind.Symbol;
    public override IReadOnlyList<Expr> Children => [];
    public override Expr WithChildren(IReadOnlyList<Expr> children) => this;
    protected override int ComputeHash() => HashCode.Combine(3, Name);
    protected override bool StructuralEquals(Expr other) => Name == ((SymbolExpr)other).Name;
}

/// <summary>Base for nodes holding an ordered list of children.</summary>
public abstract class NaryExpr(ImmutableArray<Expr> items) : Expr
{
    public ImmutableArray<Expr> Items { get; } = items;
    public override IReadOnlyList<Expr> Children => Items;
    protected override int ComputeHash()
    {
        var h = new HashCode();
        h.Add((int)Kind);
        foreach (var t in Items) h.Add(t);
        return h.ToHashCode();
    }
    protected override bool StructuralEquals(Expr other)
    {
        var o = ((NaryExpr)other).Items;
        if (o.Length != Items.Length) return false;
        for (int i = 0; i < Items.Length; i++)
            if (!Items[i].Equals(o[i])) return false;
        return true;
    }
}

/// <summary>Sum of terms. Subtraction is represented as adding (-1)*x.</summary>
public sealed class AddExpr(ImmutableArray<Expr> terms) : NaryExpr(terms)
{
    public ImmutableArray<Expr> Terms => Items;
    public override ExprKind Kind => ExprKind.Add;
    public override Expr WithChildren(IReadOnlyList<Expr> children) => new AddExpr([.. children]);
}

/// <summary>Product of factors. Division is represented as multiplying by x^(-1).</summary>
public sealed class MulExpr(ImmutableArray<Expr> factors) : NaryExpr(factors)
{
    public ImmutableArray<Expr> Factors => Items;
    public override ExprKind Kind => ExprKind.Mul;
    public override Expr WithChildren(IReadOnlyList<Expr> children) => new MulExpr([.. children]);
}

public sealed class PowExpr(Expr @base, Expr exponent) : Expr
{
    public Expr Base { get; } = @base;
    public Expr Exponent { get; } = exponent;
    public override ExprKind Kind => ExprKind.Pow;
    public override IReadOnlyList<Expr> Children => [Base, Exponent];
    public override Expr WithChildren(IReadOnlyList<Expr> children) => new PowExpr(children[0], children[1]);
    protected override int ComputeHash() => HashCode.Combine(6, Base, Exponent);
    protected override bool StructuralEquals(Expr other)
    {
        var o = (PowExpr)other;
        return Base.Equals(o.Base) && Exponent.Equals(o.Exponent);
    }
}

/// <summary>Function application f(args...). The name refers to a builtin or user-defined function.</summary>
public sealed class CallExpr(string name, ImmutableArray<Expr> args) : Expr
{
    public string Name { get; } = name;
    public ImmutableArray<Expr> Args { get; } = args;
    public override ExprKind Kind => ExprKind.Call;
    public override IReadOnlyList<Expr> Children => Args;
    public override Expr WithChildren(IReadOnlyList<Expr> children) => new CallExpr(Name, [.. children]);
    protected override int ComputeHash()
    {
        var h = new HashCode();
        h.Add(7);
        h.Add(Name);
        foreach (var a in Args) h.Add(a);
        return h.ToHashCode();
    }
    protected override bool StructuralEquals(Expr other)
    {
        var o = (CallExpr)other;
        if (o.Name != Name || o.Args.Length != Args.Length) return false;
        for (int i = 0; i < Args.Length; i++)
            if (!Args[i].Equals(o.Args[i])) return false;
        return true;
    }

    public static CallExpr Of(string name, params Expr[] args) => new(name, [.. args]);
}

public enum RelOp
{
    Equal,
    NotEqual,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,
}

public sealed class RelationExpr(RelOp op, Expr left, Expr right) : Expr
{
    public RelOp Op { get; } = op;
    public Expr Left { get; } = left;
    public Expr Right { get; } = right;
    public override ExprKind Kind => ExprKind.Relation;
    public override IReadOnlyList<Expr> Children => [Left, Right];
    public override Expr WithChildren(IReadOnlyList<Expr> children) => new RelationExpr(Op, children[0], children[1]);
    protected override int ComputeHash() => HashCode.Combine(8, Op, Left, Right);
    protected override bool StructuralEquals(Expr other)
    {
        var o = (RelationExpr)other;
        return Op == o.Op && Left.Equals(o.Left) && Right.Equals(o.Right);
    }

    public static RelOp Flip(RelOp op) => op switch
    {
        RelOp.Less => RelOp.Greater,
        RelOp.LessEqual => RelOp.GreaterEqual,
        RelOp.Greater => RelOp.Less,
        RelOp.GreaterEqual => RelOp.LessEqual,
        _ => op,
    };

    public static string Symbol(RelOp op) => op switch
    {
        RelOp.Equal => "=",
        RelOp.NotEqual => "≠",
        RelOp.Less => "<",
        RelOp.LessEqual => "≤",
        RelOp.Greater => ">",
        RelOp.GreaterEqual => "≥",
        _ => "?",
    };
}

public enum LogicOp
{
    And,
    Or,
    Xor,
    Not,
}

public sealed class LogicExpr(LogicOp op, ImmutableArray<Expr> operands) : NaryExpr(operands)
{
    public LogicOp Op { get; } = op;
    public ImmutableArray<Expr> Operands => Items;
    public override ExprKind Kind => ExprKind.Logic;
    public override Expr WithChildren(IReadOnlyList<Expr> children) => new LogicExpr(Op, [.. children]);
    protected override int ComputeHash() => HashCode.Combine(base.ComputeHash(), Op);
    protected override bool StructuralEquals(Expr other) => Op == ((LogicExpr)other).Op && base.StructuralEquals(other);
}

public sealed class ListExpr(ImmutableArray<Expr> items) : NaryExpr(items)
{
    public override ExprKind Kind => ExprKind.List;
    public override Expr WithChildren(IReadOnlyList<Expr> children) => new ListExpr([.. children]);
    public int Count => Items.Length;
    public static readonly ListExpr Empty = new([]);
}

/// <summary>Points and parametric pairs: (a, b) or (a, b, c).</summary>
public sealed class TupleExpr(ImmutableArray<Expr> items) : NaryExpr(items)
{
    public override ExprKind Kind => ExprKind.Tuple;
    public override Expr WithChildren(IReadOnlyList<Expr> children) => new TupleExpr([.. children]);
}

/// <summary>Dense matrix stored row-major.</summary>
public sealed class MatrixExpr : NaryExpr
{
    public int Rows { get; }
    public int Cols { get; }

    public MatrixExpr(int rows, int cols, ImmutableArray<Expr> items) : base(items)
    {
        if (items.Length != rows * cols) throw new ArgumentException("Matrix dimension mismatch");
        Rows = rows;
        Cols = cols;
    }

    public Expr this[int r, int c] => Items[r * Cols + c];
    public override ExprKind Kind => ExprKind.Matrix;
    public override Expr WithChildren(IReadOnlyList<Expr> children) => new MatrixExpr(Rows, Cols, [.. children]);
    protected override int ComputeHash() => HashCode.Combine(base.ComputeHash(), Rows, Cols);
    protected override bool StructuralEquals(Expr other)
    {
        var o = (MatrixExpr)other;
        return Rows == o.Rows && Cols == o.Cols && base.StructuralEquals(other);
    }

    public static MatrixExpr FromRows(IReadOnlyList<IReadOnlyList<Expr>> rows)
    {
        int r = rows.Count, c = r == 0 ? 0 : rows[0].Count;
        var b = ImmutableArray.CreateBuilder<Expr>(r * c);
        foreach (var row in rows)
        {
            if (row.Count != c) throw new ArgumentException("All matrix rows must have the same length");
            b.AddRange(row);
        }
        return new MatrixExpr(r, c, b.MoveToImmutable());
    }

    public IEnumerable<IReadOnlyList<Expr>> RowLists()
    {
        for (int r = 0; r < Rows; r++)
        {
            var row = new Expr[Cols];
            for (int c = 0; c < Cols; c++) row[c] = this[r, c];
            yield return row;
        }
    }
}

public sealed class StringExpr(string value) : Expr
{
    public string Value { get; } = value;
    public override ExprKind Kind => ExprKind.String;
    public override IReadOnlyList<Expr> Children => [];
    public override Expr WithChildren(IReadOnlyList<Expr> children) => this;
    protected override int ComputeHash() => HashCode.Combine(12, Value);
    protected override bool StructuralEquals(Expr other) => Value == ((StringExpr)other).Value;
}

public sealed class BoolExpr : Expr
{
    public bool Value { get; }
    private BoolExpr(bool value) => Value = value;
    public static readonly BoolExpr True = new(true);
    public static readonly BoolExpr False = new(false);
    public static BoolExpr Of(bool b) => b ? True : False;
    public override ExprKind Kind => ExprKind.Bool;
    public override IReadOnlyList<Expr> Children => [];
    public override Expr WithChildren(IReadOnlyList<Expr> children) => this;
    protected override int ComputeHash() => HashCode.Combine(13, Value);
    protected override bool StructuralEquals(Expr other) => Value == ((BoolExpr)other).Value;
}

public sealed class LambdaExpr(ImmutableArray<string> parameters, Expr body) : Expr
{
    public ImmutableArray<string> Parameters { get; } = parameters;
    public Expr Body { get; } = body;
    public override ExprKind Kind => ExprKind.Lambda;
    public override IReadOnlyList<Expr> Children => [Body];
    public override Expr WithChildren(IReadOnlyList<Expr> children) => new LambdaExpr(Parameters, children[0]);
    protected override int ComputeHash()
    {
        var h = new HashCode();
        h.Add(14);
        foreach (var p in Parameters) h.Add(p);
        h.Add(Body);
        return h.ToHashCode();
    }
    protected override bool StructuralEquals(Expr other)
    {
        var o = (LambdaExpr)other;
        return Parameters.SequenceEqual(o.Parameters) && Body.Equals(o.Body);
    }
}

/// <summary>Piecewise definition: the first case whose condition holds; Otherwise when none holds (null means undefined).</summary>
public sealed class PiecewiseExpr : Expr
{
    public ImmutableArray<(Expr Condition, Expr Value)> Cases { get; }
    public Expr? Otherwise { get; }

    public PiecewiseExpr(ImmutableArray<(Expr Condition, Expr Value)> cases, Expr? otherwise)
    {
        Cases = cases;
        Otherwise = otherwise;
    }

    public override ExprKind Kind => ExprKind.Piecewise;

    public override IReadOnlyList<Expr> Children
    {
        get
        {
            var list = new List<Expr>(Cases.Length * 2 + 1);
            foreach (var (c, v) in Cases) { list.Add(c); list.Add(v); }
            if (Otherwise is not null) list.Add(Otherwise);
            return list;
        }
    }

    public override Expr WithChildren(IReadOnlyList<Expr> children)
    {
        var b = ImmutableArray.CreateBuilder<(Expr, Expr)>(Cases.Length);
        for (int i = 0; i < Cases.Length; i++) b.Add((children[2 * i], children[2 * i + 1]));
        return new PiecewiseExpr(b.MoveToImmutable(), Otherwise is null ? null : children[^1]);
    }

    protected override int ComputeHash()
    {
        var h = new HashCode();
        h.Add(15);
        foreach (var (c, v) in Cases) { h.Add(c); h.Add(v); }
        h.Add(Otherwise);
        return h.ToHashCode();
    }

    protected override bool StructuralEquals(Expr other)
    {
        var o = (PiecewiseExpr)other;
        if (o.Cases.Length != Cases.Length) return false;
        for (int i = 0; i < Cases.Length; i++)
            if (!Cases[i].Condition.Equals(o.Cases[i].Condition) || !Cases[i].Value.Equals(o.Cases[i].Value)) return false;
        return Equals(Otherwise, o.Otherwise);
    }
}

/// <summary>Indexing: list[i], matrix[r, c]. Indices are 1-based like TI and Casio.</summary>
public sealed class IndexExpr(Expr target, ImmutableArray<Expr> indices) : Expr
{
    public Expr Target { get; } = target;
    public ImmutableArray<Expr> Indices { get; } = indices;
    public override ExprKind Kind => ExprKind.Index;
    public override IReadOnlyList<Expr> Children => [Target, .. Indices];
    public override Expr WithChildren(IReadOnlyList<Expr> children) => new IndexExpr(children[0], [.. children.Skip(1)]);
    protected override int ComputeHash()
    {
        var h = new HashCode();
        h.Add(16);
        h.Add(Target);
        foreach (var i in Indices) h.Add(i);
        return h.ToHashCode();
    }
    protected override bool StructuralEquals(Expr other)
    {
        var o = (IndexExpr)other;
        return Target.Equals(o.Target) && Indices.SequenceEqual(o.Indices);
    }
}

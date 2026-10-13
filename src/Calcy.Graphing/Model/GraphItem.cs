using Calcy.Core.Expressions;

namespace Calcy.Graphing;

/// <summary>What a line of graph input describes.</summary>
public enum GraphKind
{
    /// <summary>Nothing typed yet.</summary>
    Empty,

    /// <summary>y = f(x), a bare f(x), or a one-variable definition f(x) = ...</summary>
    Function,

    /// <summary>x = g(y).</summary>
    FunctionOfY,

    /// <summary>x = c (a constant, possibly a slider).</summary>
    VerticalLine,

    /// <summary>r = f(θ) over a θ range.</summary>
    Polar,

    /// <summary>(x(t), y(t)) over a t range.</summary>
    Parametric,

    /// <summary>A relation F(x, y) = G(x, y) of any form.</summary>
    Implicit,

    /// <summary>y &lt; f(x), y ≥ f(x) and so on: shading below or above a curve.</summary>
    InequalityY,

    /// <summary>x &lt; g(y), x ≥ g(y) and so on: shading left or right of a curve.</summary>
    InequalityX,

    /// <summary>Any other inequality (or several joined by "and"): a shaded region with implicit boundaries.</summary>
    Region,

    /// <summary>A single point (a, b).</summary>
    Point,

    /// <summary>A list of points.</summary>
    Points,

    /// <summary>A slope field y' = f(x, y).</summary>
    SlopeField,

    /// <summary>a = 1.5: defines a slider value.</summary>
    Variable,

    /// <summary>A function definition that is not plotted (more than one parameter).</summary>
    Definition,

    /// <summary>The input could not be understood; see <see cref="GraphItem.Error"/>.</summary>
    Error,
}

/// <summary>One condition "F op 0" of a shaded region.</summary>
/// <param name="Function">F = left side minus right side, compiled over (x, y).</param>
/// <param name="Op">The comparison with zero.</param>
public sealed record RegionCondition(CompiledExpr Function, RelOp Op)
{
    /// <summary>True for &lt; and &gt; (the boundary is not part of the region and is drawn dashed).</summary>
    public bool IsStrict => Op is RelOp.Less or RelOp.Greater;

    /// <summary>Tests the condition at a point.</summary>
    public bool Holds(double x, double y, double[] p, bool includeBoundary = false)
    {
        double v = Function.Eval(x, y, p);
        if (double.IsNaN(v)) return false;
        if (includeBoundary && Math.Abs(v) < 1e-9) return true;
        return Op switch
        {
            RelOp.Less => v < 0,
            RelOp.LessEqual => v <= 0,
            RelOp.Greater => v > 0,
            RelOp.GreaterEqual => v >= 0,
            RelOp.Equal => v == 0,
            _ => v != 0,
        };
    }
}

/// <summary>
/// One curve of a graph item. Most items have a single member; an expression containing a list
/// (y = {1, 2, 3}·x) gives one member per list element (a family).
/// </summary>
public sealed class GraphMember
{
    private CompiledExpr? _d1, _d2;
    private bool _d1Done, _d2Done;
    private readonly Calcy.Core.Evaluation.EvalContext? _ctx;

    internal GraphMember(int index, Calcy.Core.Evaluation.EvalContext? ctx)
    {
        Index = index;
        _ctx = ctx;
    }

    /// <summary>Position in the family (0 for single curves).</summary>
    public int Index { get; }

    /// <summary>The main compiled expression: f(x), g(y), r(θ), F(x, y), the slope f(x, y), or the constant of x = c.</summary>
    public CompiledExpr? Main { get; internal init; }

    /// <summary>x(t) for parametric curves.</summary>
    public CompiledExpr? X { get; internal init; }

    /// <summary>y(t) for parametric curves.</summary>
    public CompiledExpr? Y { get; internal init; }

    /// <summary>Conditions of a shaded region (all must hold).</summary>
    public IReadOnlyList<RegionCondition> Conditions { get; internal init; } = [];

    /// <summary>Coordinates of points (each pair compiled with no plot variable).</summary>
    public IReadOnlyList<(CompiledExpr X, CompiledExpr Y)> PointCoordinates { get; internal init; } = [];

    /// <summary>The symbolic first derivative of <see cref="Main"/> (functions only), or null when unknown.</summary>
    public CompiledExpr? FirstDerivative
    {
        get
        {
            if (!_d1Done)
            {
                _d1 = Differentiate(Main);
                _d1Done = true;
            }
            return _d1;
        }
    }

    /// <summary>The symbolic second derivative of <see cref="Main"/>, or null when unknown.</summary>
    public CompiledExpr? SecondDerivative
    {
        get
        {
            if (!_d2Done)
            {
                _d2 = Differentiate(FirstDerivative);
                _d2Done = true;
            }
            return _d2;
        }
    }

    private CompiledExpr? Differentiate(CompiledExpr? f)
    {
        if (f is null || _ctx is null || f.PlotVariables.Count != 1) return null;
        try
        {
            var d = Symbolic.Derivative.Of(f.Expression, f.PlotVariables[0], _ctx.WithFree(f.Parameters));
            if (d is null || d.NodeCount() > 4000) return null;
            return new CompiledExpr(d, f.PlotVariables, f.Parameters, _ctx);
        }
        catch (Calcy.Core.CalcException)
        {
            return null;
        }
    }
}

/// <summary>
/// A classified, compiled line of graph input. Create one with <see cref="GraphParser.Parse"/>
/// (or through <see cref="GraphDocument"/>), then sample it with <see cref="GraphSampler"/>.
/// </summary>
public sealed class GraphItem
{
    internal GraphItem(string text, GraphKind kind)
    {
        Text = text;
        Kind = kind;
    }

    /// <summary>The text as typed.</summary>
    public string Text { get; }

    /// <summary>What the text describes.</summary>
    public GraphKind Kind { get; }

    /// <summary>A short label for the expression list ("function", "implicit", "inequality", "point", ...).</summary>
    public string KindLabel { get; internal init; } = "";

    /// <summary>Why the text could not be graphed (null when it can).</summary>
    public string? Error { get; internal init; }

    /// <summary>Character position of the error in <see cref="Text"/>, or -1.</summary>
    public int ErrorPosition { get; internal init; } = -1;

    /// <summary>Length of the error span.</summary>
    public int ErrorLength { get; internal init; }

    /// <summary>Names without a value (not plot variables): each gets a slider.</summary>
    public IReadOnlyList<string> Parameters { get; internal init; } = [];

    /// <summary>The curves of the item (several for a family).</summary>
    public IReadOnlyList<GraphMember> Members { get; internal init; } = [];

    /// <summary>The plot variable: x for functions, y for x = g(y), θ for polar, t for parametric.</summary>
    public string? Variable { get; internal init; }

    /// <summary>The comparison of an inequality (y op f(x) or x op g(y)), with the plotted variable on the left.</summary>
    public RelOp Relation { get; internal init; } = RelOp.Equal;

    /// <summary>True for strict inequalities (dashed boundary).</summary>
    public bool IsStrict => Relation is RelOp.Less or RelOp.Greater ||
                            (Kind == GraphKind.Region && Members.Count > 0 && Members[0].Conditions.All(c => c.IsStrict));

    /// <summary>The label of a named point (A = (2, 2)), or the name of a defined function or slider.</summary>
    public string? Label { get; internal init; }

    /// <summary>The value of a slider definition such as a = 1.5.</summary>
    public double? VariableValue { get; internal init; }

    /// <summary>Range of t (parametric) or θ (polar). Settable from the window dialog.</summary>
    public double TMin { get; set; }

    /// <summary>End of the t or θ range.</summary>
    public double TMax { get; set; }

    /// <summary>Radians per unit of θ (1 in radian mode, π/180 in degree mode); used to draw polar curves.</summary>
    public double AngleToRadians { get; internal init; } = 1;

    /// <summary>True when the range came from a restriction in the text ({0 &lt; t &lt; 3}).</summary>
    public bool RangeFromText { get; internal init; }

    /// <summary>The definition this line made in the session (f for f(x) = ...), so it can be removed again.</summary>
    public Calcy.Core.Evaluation.UserFunction? DefinedFunction { get; internal init; }

    /// <summary>Colour slot: 0 to 4 map to the scheme's graph inks G1 to G5.</summary>
    public int ColorIndex { get; set; }

    /// <summary>Hidden items are kept but not drawn or analysed.</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>Display name used in cards and tables ("y1", "A").</summary>
    public string Name { get; set; } = "";

    /// <summary>True when the item draws something.</summary>
    public bool IsPlottable => Kind is not (GraphKind.Empty or GraphKind.Error or GraphKind.Variable or GraphKind.Definition) && Members.Count > 0;

    /// <summary>True for y = f(x) items (the ones with roots, extrema and tables).</summary>
    public bool IsFunction => Kind == GraphKind.Function && Members.Count > 0;

    /// <summary>True for items described by a parameter range (parametric and polar).</summary>
    public bool HasRange => Kind is GraphKind.Parametric or GraphKind.Polar;

    /// <summary>Kind and text, for debugging.</summary>
    public override string ToString() => $"{Kind}: {Text}";
}

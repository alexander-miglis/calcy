using Calcy.Core;
using Calcy.Core.Expressions;

namespace Calcy.Graphing.Tests;

public class ClassificationTests
{
    private static GraphItem Parse(string text, CalcSession? s = null) => GraphParser.Parse(text, s ?? new CalcSession());

    [Theory]
    [InlineData("y = x^2 - 2", GraphKind.Function, 1)]
    [InlineData("x^2 - 2", GraphKind.Function, 1)]
    [InlineData("sin(x)", GraphKind.Function, 1)]
    [InlineData("y = 3", GraphKind.Function, 1)]
    [InlineData("2x + 1 = y", GraphKind.Function, 1)]
    [InlineData("y = sqrt(x)", GraphKind.Function, 1)]
    [InlineData("y = x^2 {0 < x < 5}", GraphKind.Function, 1)]
    [InlineData("y = {x < 0: -x, x^2}", GraphKind.Function, 1)]
    [InlineData("y = {1, 2, 3}x", GraphKind.Function, 3)]
    [InlineData("y = x + {-1, 0, 1, 2}", GraphKind.Function, 4)]
    [InlineData("f(x) = x^3 - x", GraphKind.Function, 1)]
    [InlineData("x = y^2", GraphKind.FunctionOfY, 1)]
    [InlineData("x = sin(y)", GraphKind.FunctionOfY, 1)]
    [InlineData("x = 3", GraphKind.VerticalLine, 1)]
    [InlineData("x = {-1, 1}", GraphKind.VerticalLine, 2)]
    [InlineData("r = θ", GraphKind.Polar, 1)]
    [InlineData("r = 2cos(3theta)", GraphKind.Polar, 1)]
    [InlineData("r = {1, 2, 3}", GraphKind.Polar, 3)]
    [InlineData("(cos(t), sin(t))", GraphKind.Parametric, 1)]
    [InlineData("(t, t^2) {0 < t < 2}", GraphKind.Parametric, 1)]
    [InlineData("x^2 + y^2 = 9", GraphKind.Implicit, 1)]
    [InlineData("x^2/4 + y^2/9 = 1", GraphKind.Implicit, 1)]
    [InlineData("xy = 1", GraphKind.Implicit, 1)]
    [InlineData("y^2 = x^3 - x", GraphKind.Implicit, 1)]
    [InlineData("sin(x) = cos(y)", GraphKind.Implicit, 1)]
    [InlineData("x^2 + y^2 = {1, 4, 9}", GraphKind.Implicit, 3)]
    [InlineData("y < x/2", GraphKind.InequalityY, 1)]
    [InlineData("y ≥ x^2 - 1", GraphKind.InequalityY, 1)]
    [InlineData("x/2 > y", GraphKind.InequalityY, 1)]
    [InlineData("x > 2", GraphKind.InequalityX, 1)]
    [InlineData("x <= y^2", GraphKind.InequalityX, 1)]
    [InlineData("x^2 + y^2 < 9", GraphKind.Region, 1)]
    [InlineData("0 < y < x", GraphKind.Region, 1)]
    [InlineData("(2, 3)", GraphKind.Point, 1)]
    [InlineData("A = (2, 2)", GraphKind.Point, 1)]
    [InlineData("{(1, 2), (3, 4), (5, 6)}", GraphKind.Points, 1)]
    [InlineData("y' = x*y", GraphKind.SlopeField, 1)]
    [InlineData("dy/dx = x - y", GraphKind.SlopeField, 1)]
    [InlineData("a = 1.5", GraphKind.Variable, 0)]
    [InlineData("g(x, y) = x*y", GraphKind.Definition, 0)]
    [InlineData("", GraphKind.Empty, 0)]
    public void Classifies(string text, GraphKind kind, int members)
    {
        var item = Parse(text);
        Assert.True(item.Kind == kind, $"'{text}' was {item.Kind} ({item.Error})");
        Assert.Equal(members, item.Members.Count);
        Assert.Null(item.Error);
    }

    [Theory]
    [InlineData("y = (x")]
    [InlineData("x^2 + y")]
    [InlineData("1 = 2")]
    [InlineData("y ≠ x")]
    [InlineData("(1, 2, 3)")]
    [InlineData("b = 2c")]
    [InlineData("r = x θ")]
    public void ReportsErrors(string text)
    {
        var item = Parse(text);
        Assert.Equal(GraphKind.Error, item.Kind);
        Assert.False(string.IsNullOrWhiteSpace(item.Error));
        Assert.False(item.IsPlottable);
    }

    [Fact]
    public void FreeParametersBecomeSliders()
    {
        var item = Parse("y = a*sin(b*x) + c");
        Assert.Equal(["a", "b", "c"], item.Parameters);
        var s = new CalcSession();
        s.Evaluate("a = 2");
        var bound = Parse("y = a*sin(x)", s);
        Assert.Empty(bound.Parameters);
        Assert.Equal(2 * Math.Sin(1), bound.Members[0].Main!.Eval(1, []), 12);
    }

    [Fact]
    public void ParametersAreReadPerCall()
    {
        var item = Parse("y = a*x^2");
        var f = item.Members[0].Main!;
        Assert.Equal(12, f.Eval(2, [3.0]), 12);
        Assert.Equal(-4, f.Eval(2, [-1.0]), 12);
    }

    [Fact]
    public void DefinitionDefinesTheFunctionInTheSession()
    {
        var s = new CalcSession();
        var def = Parse("f(x) = x^2 + 1", s);
        Assert.Equal(GraphKind.Function, def.Kind);
        Assert.Equal("f", def.Label);
        Assert.True(s.Env.TryGetFunction("f", out _));
        var use = Parse("y = f(x) - 1", s);
        Assert.Equal(GraphKind.Function, use.Kind);
        Assert.Equal(9, use.Members[0].Main!.Eval(3, []), 12);
    }

    [Fact]
    public void DomainRestrictionGivesNaNOutside()
    {
        var f = Parse("y = x^2 {0 < x < 5}").Members[0].Main!;
        Assert.Equal(4, f.Eval(2, []), 12);
        Assert.True(double.IsNaN(f.Eval(-1, [])));
        Assert.True(double.IsNaN(f.Eval(6, [])));
    }

    [Fact]
    public void FamilyMembersAreSeparateCurves()
    {
        var item = Parse("y = {1, 2, 3}x");
        Assert.Equal(new[] { 2.0, 4, 6 }, item.Members.Select(m => m.Main!.Eval(2, [])));
        Assert.Contains("×3", item.KindLabel);
    }

    [Fact]
    public void ListsFromTheSessionMakeFamiliesAndPointLists()
    {
        var s = new CalcSession();
        s.Evaluate("L1 = {1, 2, 3}");
        Assert.Equal(3, Parse("y = L1*x", s).Members.Count);
        var pts = Parse("(L1, L1^2)", s);
        Assert.Equal(GraphKind.Points, pts.Kind);
        Assert.Equal(3, pts.Members[0].PointCoordinates.Count);
        Assert.Equal(9, pts.Members[0].PointCoordinates[2].Y.Eval([]), 12);
    }

    [Fact]
    public void InequalitiesRecordDirectionAndStrictness()
    {
        var below = Parse("y < x/2");
        Assert.Equal(RelOp.Less, below.Relation);
        Assert.True(below.IsStrict);
        var flipped = Parse("x/2 > y");
        Assert.Equal(RelOp.Less, flipped.Relation);
        var above = Parse("y >= x^2");
        Assert.Equal(RelOp.GreaterEqual, above.Relation);
        Assert.False(above.IsStrict);
        Assert.True(Parse("x^2 + y^2 < 9").IsStrict);
        Assert.False(Parse("x^2 + y^2 <= 9").IsStrict);
    }

    [Fact]
    public void ParametricAndPolarRanges()
    {
        var circle = Parse("(cos(t), sin(t))");
        Assert.Equal(0, circle.TMin);
        Assert.Equal(2 * Math.PI, circle.TMax, 12);
        var restricted = Parse("(t, t^2) {0 < t < 2}");
        Assert.Equal(2, restricted.TMax, 12);
        Assert.True(restricted.RangeFromText);
        var spiral = Parse("r = θ {0 < θ < 4π}");
        Assert.Equal(4 * Math.PI, spiral.TMax, 12);
        var deg = Parse("r = 1", new CalcSession(new CalcSettings { Angle = AngleMode.Degree }));
        Assert.Equal(360, deg.TMax);
    }

    [Fact]
    public void LabelledPointAndSlider()
    {
        var a = Parse("A = (2, 2)");
        Assert.Equal("A", a.Label);
        var slider = Parse("k = 2.5");
        Assert.Equal("k", slider.Label);
        Assert.Equal(2.5, slider.VariableValue);
    }

    [Fact]
    public void ErrorPositionPointsIntoTheText()
    {
        var item = Parse("  y = 2 +* 3");
        Assert.Equal(GraphKind.Error, item.Kind);
        Assert.True(item.ErrorPosition >= 2);
    }
}

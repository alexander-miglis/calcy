using Avalonia;
using Calcy.UI.Math.Layout;
using Calcy.UI.Math.Model;

namespace Calcy.UI.Math.Tests;

/// <summary>Pure layout tests with a deterministic fixed-width measurer (no platform needed).</summary>
public class LayoutTests
{
    private const double F = 20;

    private static (MathLayout Layout, MathRow Root) Lay(string text)
    {
        var s = new MathEditorState(RoundTripTests.Ctx);
        Assert.True(s.SetText(text), text);
        var layout = new MathLayoutEngine(new FixedWidthTextMeasurer(), new MathLayoutOptions { FontSize = F }).Layout(s.Root);
        return (layout, s.Root);
    }

    private static MathTemplate T(MathRow row, int i) => Assert.IsType<MathTemplate>(row[i]);

    [Fact]
    public void FractionIsTallerThanItsPartsAndStacksThem()
    {
        var (layout, root) = Lay("(a+b)/(c+d)");
        var frac = T(root, 0);
        var num = layout.Find(frac[0])!;
        var den = layout.Find(frac[1])!;
        var box = layout.RootRow.ItemBoxes[0]!;
        Assert.True(box.Height > num.Height + den.Height, "fraction is taller than numerator and denominator together");
        Assert.True(num.Bounds.Bottom <= den.Bounds.Top, "numerator is above the denominator");
        Assert.True(num.Origin.Y < layout.RootRow.Origin.Y && den.Origin.Y > layout.RootRow.Origin.Y, "numerator above, denominator below the baseline");
        Assert.Equal(num.Bounds.Center.X, den.Bounds.Center.X, 3);
        var rule = box.Children.OfType<MathRuleBox>().Single();
        Assert.True(rule.Width >= System.Math.Max(num.Width, den.Width));
        Assert.True(rule.Height >= 1);
    }

    [Fact]
    public void FractionBarSitsOnTheMathAxis()
    {
        var (layout, root) = Lay("1/2");
        var rule = layout.RootRow.ItemBoxes[0]!.Children.OfType<MathRuleBox>().Single();
        double axis = new FixedWidthTextMeasurer().GetFontMetrics(F).AxisHeight;
        double center = rule.Origin.Y - rule.Ascent / 2;
        Assert.Equal(layout.RootRow.Origin.Y - axis, center, 3);
    }

    [Fact]
    public void BaselinesInARowAreAligned()
    {
        var (layout, _) = Lay("a+1/2+sqrt(x)+b");
        foreach (var child in layout.RootRow.Children) Assert.Equal(0, child.OffsetY);
        var x = layout.RootRow.CaretX;
        for (int i = 1; i < x.Length; i++) Assert.True(x[i] > x[i - 1], "caret stops increase left to right");
    }

    [Fact]
    public void SuperscriptIsRaisedAndSmaller()
    {
        var (layout, root) = Lay("x^2");
        var pow = T(root, 1);
        var exp = layout.Find(pow[0])!;
        var baseBox = layout.RootRow.ItemBoxes[0]!;
        Assert.True(exp.Origin.Y < baseBox.Origin.Y - 0.3 * F, "exponent baseline is raised");
        Assert.True(exp.FontSize < F);
        Assert.True(exp.Bounds.Left >= baseBox.Bounds.Right - 0.01, "exponent follows the base");
    }

    [Fact]
    public void SubscriptIsLowered()
    {
        var (layout, root) = Lay("v_0");
        var sub = layout.Find(T(root, 1)[0])!;
        Assert.True(sub.Origin.Y > layout.RootRow.Origin.Y);
        Assert.True(sub.FontSize < F);
    }

    [Fact]
    public void SubscriptAndSuperscriptStack()
    {
        var (layout, root) = Lay("v_0^2");
        var sub = layout.Find(T(root, 1)[0])!;
        var sup = layout.Find(T(root, 2)[0])!;
        Assert.Equal(sub.Origin.X, sup.Origin.X, 3);
        Assert.True(sup.Bounds.Bottom < sub.Bounds.Top);
    }

    [Fact]
    public void SuperscriptOnTallBaseIsRaisedMore()
    {
        var (small, _) = Lay("x^2");
        var (tall, root) = Lay("(1/2)^2");
        double lowShift = small.RootRow.Origin.Y - small.Find(T(small.RootRow.Row, 1)[0])!.Origin.Y;
        double highShift = tall.RootRow.Origin.Y - tall.Find(T(root, 1)[0])!.Origin.Y;
        Assert.True(highShift > lowShift);
    }

    [Fact]
    public void RadicalCoversItsContent()
    {
        var (layout, root) = Lay("sqrt(x+1)");
        var rad = layout.Find(T(root, 0)[0])!;
        var box = layout.RootRow.ItemBoxes[0]!;
        var sign = box.Children.OfType<MathShapeBox>().Single();
        var bar = box.Children.OfType<MathRuleBox>().Single();
        Assert.Equal(MathShape.Radical, sign.Shape);
        Assert.True(bar.Bounds.Bottom <= rad.Bounds.Top, "overbar above the radicand");
        Assert.True(bar.Bounds.Right >= rad.Bounds.Right, "overbar spans the radicand");
        Assert.True(sign.Bounds.Right <= rad.Bounds.Left);
        Assert.True(sign.Bounds.Bottom >= rad.Bounds.Bottom);
    }

    [Fact]
    public void NthRootIndexIsSmallAndLeft()
    {
        var (layout, root) = Lay("root(x, 3)");
        var t = T(root, 0);
        var index = layout.Find(t[0])!;
        var rad = layout.Find(t[1])!;
        Assert.True(index.FontSize < rad.FontSize);
        Assert.True(index.Bounds.Right <= rad.Bounds.Left);
        Assert.True(index.Bounds.Bottom < rad.Origin.Y);
    }

    [Fact]
    public void BracketsGrowWithContent()
    {
        var (flat, _) = Lay("2(x+1)");
        var (tall, _) = Lay("2(1/2+1)");
        double h1 = flat.RootRow.ItemBoxes[1]!.Children.OfType<MathShapeBox>().First().Height;
        double h2 = tall.RootRow.ItemBoxes[1]!.Children.OfType<MathShapeBox>().First().Height;
        Assert.True(h2 > h1 * 1.2, $"{h2} > {h1}");
        var (abs, _) = Lay("abs(1/2)");
        var bars = abs.RootRow.ItemBoxes[0]!.Children.OfType<MathShapeBox>().ToList();
        Assert.Equal(2, bars.Count);
        Assert.All(bars, b => Assert.Equal(MathShape.Bar, b.Shape));
    }

    [Fact]
    public void MatrixCellsShareRowBaselinesAndColumns()
    {
        var (layout, root) = Lay("[[1, 22], [333, 4]]");
        var m = T(root, 0);
        var a = layout.Find(m.Cell(0, 0))!;
        var b = layout.Find(m.Cell(0, 1))!;
        var c = layout.Find(m.Cell(1, 0))!;
        Assert.Equal(a.Origin.Y, b.Origin.Y, 3);
        Assert.True(c.Origin.Y > a.Origin.Y);
        Assert.Equal(a.Bounds.Center.X, c.Bounds.Center.X, 3);
        var shapes = layout.RootRow.ItemBoxes[0]!.Children.OfType<MathShapeBox>().ToList();
        Assert.Contains(shapes, s => s.Shape == MathShape.BracketLeft);
        Assert.Contains(shapes, s => s.Shape == MathShape.BracketRight);
    }

    [Fact]
    public void BigOperatorsPutLimitsAboveAndBelow()
    {
        var (layout, root) = Lay("sum(k^2, k, 1, 10)");
        var t = T(root, 0);
        var from = layout.Find(t[1])!;
        var to = layout.Find(t[2])!;
        var body = layout.Find(t[3])!;
        Assert.True(to.Bounds.Bottom < body.Bounds.Top + 1);
        Assert.True(from.Bounds.Top > body.Origin.Y);
        Assert.True(body.Bounds.Left > to.Bounds.Right - 0.01);
    }

    [Fact]
    public void IntegralSignIsTallAndLimitsAreBesideIt()
    {
        var (layout, root) = Lay("integrate(x^2, x, 0, 1)");
        var t = T(root, 0);
        var sign = layout.RootRow.ItemBoxes[0]!.Children.OfType<MathShapeBox>().Single(s => s.Shape == MathShape.Integral);
        Assert.True(sign.Height >= 2 * F - 0.01);
        var upper = layout.Find(t[1])!;
        var lower = layout.Find(t[0])!;
        Assert.True(upper.Origin.Y < lower.Origin.Y);
        Assert.True(layout.Find(t[2])!.Bounds.Left > upper.Bounds.Right - 0.01);
    }

    [Fact]
    public void EmptySlotsAreDashedBoxes()
    {
        var s = new MathEditorState();
        s.InsertTemplate(MathTemplateKind.Fraction);
        var layout = new MathLayoutEngine(new FixedWidthTextMeasurer()).Layout(s.Root);
        var slots = layout.AllBoxes().OfType<MathSlotBox>().ToList();
        Assert.Equal(2, slots.Count);
        Assert.All(slots, b => Assert.True(b.Width > 0 && b.Height > 0));
    }

    [Fact]
    public void HitTestFindsTheDeepestRow()
    {
        var (layout, root) = Lay("12/34+5");
        var frac = T(root, 0);
        var den = layout.Find(frac[1])!;
        var hit = layout.HitTest(new Point(den.Bounds.Right - 1, den.Bounds.Center.Y));
        Assert.Same(frac[1], hit.Row);
        Assert.Equal(2, hit.Index);
        var after = layout.HitTest(new Point(layout.Width + 50, layout.RootRow.Origin.Y));
        Assert.Same(root, after.Row);
        Assert.Equal(root.Count, after.Index);
    }

    [Fact]
    public void CaretRectHasTextHeight()
    {
        var (layout, root) = Lay("x+1");
        var r = layout.GetCaretRect(new MathCaret(root, 1));
        Assert.True(r.Height > 0.7 * F && r.Width > 0);
        Assert.True(r.Top < layout.RootRow.Origin.Y && r.Bottom > layout.RootRow.Origin.Y);
    }

    [Fact]
    public void LinesStackDownwards()
    {
        var engine = new MathLayoutEngine(new FixedWidthTextMeasurer(), new MathLayoutOptions { FontSize = F });
        var a = MathInputBuilder.PlainText("a+1");
        var b = MathInputBuilder.PlainText("+b");
        var c = MathInputBuilder.PlainText("+d");
        var layout = engine.LayoutLines([a, b, c], F, 5);
        var ys = new[] { a, b, c }.Select(r => layout.Find(r)!.Origin.Y).ToList();
        Assert.True(ys[0] < ys[1] && ys[1] < ys[2]);
        Assert.Equal(ys[1] - ys[0], ys[2] - ys[1], 3);
    }

    [Fact]
    public void BreakLinesSplitsAtPlusAndMinus()
    {
        var s = new MathEditorState();
        s.SetText("a+b-c+d-e+f");
        var engine = new MathLayoutEngine(new FixedWidthTextMeasurer(), new MathLayoutOptions { FontSize = F });
        var layout = engine.Layout(s.Root);
        var lines = MathLineBreaker.BreakLines(s.Root, layout, layout.Width / 2.5, F)!;
        Assert.True(lines.Count >= 3);
        Assert.Equal("a+b-c+d-e+f", string.Concat(lines.Select(l => MathSerializer.Serialize(l))));
        Assert.All(lines.Skip(1), l => Assert.True(l[0] is MathChar { Text: "+" or "-" }));
    }
}

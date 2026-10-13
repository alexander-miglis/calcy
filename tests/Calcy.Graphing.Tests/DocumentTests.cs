using Calcy.Core;

namespace Calcy.Graphing.Tests;

public class DocumentTests
{
    [Fact]
    public void SlidersAreCreatedAndRemoved()
    {
        var d = new GraphDocument(new CalcSession());
        d.Add("y = a*sin(x)");
        d.Add("y = k*x");
        Assert.Equal(["a", "k"], d.Parameters.All.Select(p => p.Name));
        Assert.Equal(0, d.ParameterOwner("a"));
        Assert.Equal(1, d.ParameterOwner("k"));
        d.RemoveAt(1);
        Assert.Equal(["a"], d.Parameters.All.Select(p => p.Name));
        Assert.Equal(GraphParameters.DefaultValue, d.ParameterValues(d.Items[0])[0]);
    }

    [Fact]
    public void SliderDefinitionSetsTheValue()
    {
        var d = new GraphDocument(new CalcSession());
        d.Add("y = a*x");
        d.Add("a = 2.5");
        Assert.Equal(2.5, d.Parameters["a"]!.Value);
        Assert.Equal(1, d.ParameterOwner("a"));
    }

    [Fact]
    public void ColoursCycleAndSurviveEdits()
    {
        var d = new GraphDocument(new CalcSession());
        for (int i = 0; i < 6; i++) d.Add($"y = {i}x");
        Assert.Equal([0, 1, 2, 3, 4, 0], d.Items.Select(i => i.ColorIndex));
        d.Items[2].IsVisible = false;
        var edited = d.Replace(2, "y = x^2");
        Assert.Equal(2, edited.ColorIndex);
        Assert.False(edited.IsVisible);
        Assert.Equal("y3", edited.Name);
    }

    [Fact]
    public void DefinitionsAreVisibleToOtherRowsInAnyOrder()
    {
        var s = new CalcSession();
        var d = new GraphDocument(s);
        d.Add("y = f(x) + 1");
        d.Add("f(x) = x^2");
        Assert.Equal(GraphKind.Function, d.Items[0].Kind);
        Assert.Empty(d.Items[0].Parameters);
        Assert.Equal(5, d.Items[0].Members[0].Main!.Eval(2, []), 12);
        d.RemoveAt(1);
        Assert.False(s.Env.TryGetFunction("f", out _));
    }

    [Fact]
    public void SaveAndLoadRoundTrip()
    {
        var d = new GraphDocument(new CalcSession());
        d.Add("y = a*x^2");
        d.Add("(cos(t), sin(t))");
        d.Parameters["a"]!.Value = 0.5;
        d.Items[1].TMax = Math.PI;
        d.Items[0].IsVisible = false;
        var state = d.Save(new Viewport(-3, 3, -2, 2), piTicks: true);
        var json = System.Text.Json.JsonSerializer.Serialize(state);
        var back = System.Text.Json.JsonSerializer.Deserialize<GraphState>(json)!;
        var e = new GraphDocument(new CalcSession());
        e.Load(back);
        Assert.Equal(2, e.Items.Count);
        Assert.False(e.Items[0].IsVisible);
        Assert.Equal(0.5, e.Parameters["a"]!.Value);
        Assert.Equal(Math.PI, e.Items[1].TMax, 12);
        Assert.Equal(new Viewport(-3, 3, -2, 2), back.Viewport);
        Assert.True(back.PiTicks);
    }

    [Fact]
    public void AnimationAdvancesAndLoops()
    {
        var d = new GraphDocument(new CalcSession());
        d.Add("y = a*x");
        var a = d.Parameters["a"]!;
        a.Min = -1;
        a.Max = 1;
        a.Step = 0.5;
        a.Value = 0.5;
        int changes = 0;
        d.Parameters.ValuesChanged += (_, _) => changes++;
        a.Advance();
        Assert.Equal(1, a.Value);
        a.Advance();
        Assert.Equal(-1, a.Value);
        a.Mode = AnimationMode.Bounce;
        a.Value = 1;
        a.Advance();
        Assert.Equal(0.5, a.Value);
        Assert.True(changes >= 3);
    }

    [Fact]
    public void ReparsePicksUpSessionChanges()
    {
        var s = new CalcSession();
        var d = new GraphDocument(s);
        d.Add("y = b*x");
        Assert.Equal(["b"], d.Items[0].Parameters);
        s.Evaluate("b = 3");
        d.Reparse();
        Assert.Empty(d.Items[0].Parameters);
        Assert.Equal(6, d.Items[0].Members[0].Main!.Eval(2, []), 12);
    }
}

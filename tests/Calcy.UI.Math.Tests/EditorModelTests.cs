using Calcy.Core.Parsing;
using Calcy.UI.Math.Layout;
using Calcy.UI.Math.Model;

namespace Calcy.UI.Math.Tests;

/// <summary>Typing, navigation, deletion and serialization of the editing model (no UI needed).</summary>
public class EditorModelTests
{
    private static MathEditorState Typed(string keys)
    {
        var s = new MathEditorState(RoundTripTests.Ctx);
        s.TypeText(keys);
        return s;
    }

    [Theory]
    [InlineData("1/2", "1/2")]
    [InlineData("2x/3", "2x/3")]
    [InlineData("x^2", "x^2")]
    [InlineData("x^2+1", "x^2+1")]
    [InlineData("x^(n+1)", "x^(n+1)")]
    [InlineData("e^-x", "e^(-x)")]
    [InlineData("2^n-1", "2^n-1")]
    [InlineData("x**2", "x^2")]
    [InlineData("x²+1", "x^2+1")]
    [InlineData("3*4", "3*4")]
    [InlineData("sqrt2", "sqrt(2)")]
    [InlineData("sqrt(2)+1", "sqrt(2)+1")]
    [InlineData("pi", "pi")]
    [InlineData("2pi", "2pi")]
    [InlineData("pi r", "pi*r")]
    [InlineData("x/2+1", "x/2+1")]
    [InlineData("1/2*x", "1/2*x")]
    [InlineData("a/b/c", "a/b/c")]
    [InlineData("theta", "θ")]
    [InlineData("alpha+beta", "α+β")]
    [InlineData("epsilon", "ε")]
    [InlineData("x<=1", "x<=1")]
    [InlineData("x>=1", "x>=1")]
    [InlineData("x!=1", "x!=1")]
    [InlineData("x==1", "x==1")]
    [InlineData("5->a", "5->a")]
    [InlineData("a:=5", "a:=5")]
    [InlineData("(x+1)", "(x+1)")]
    [InlineData("(x+1", "(x+1)")]
    [InlineData("(x+1)^2", "(x+1)^2")]
    [InlineData("x+1)", "(x+1)")]
    [InlineData("2(x+1)+3", "2(x+1)+3")]
    [InlineData("sin(x)", "sin(x)")]
    [InlineData("sinx", "sin x")]
    [InlineData("v_0+1", "v_0+1")]
    [InlineData("v_0^2", "v_0^2")]
    [InlineData("2/3x", "2/(3x)")]
    [InlineData("1/2x", "1/(2x)")]
    [InlineData("(1/2)x", "(1/2)x")]
    [InlineData("|x-1|", "abs(x-1)")]
    [InlineData("abs(x)", "abs(x)")]
    [InlineData("sum(", "sum()")]
    [InlineData("int(3.5)", "int(3.5)")]
    [InlineData("x>0 and x<1", "x>0 and x<1")]
    [InlineData("7 mod 3", "7 mod 3")]
    [InlineData("rand()", "rand()")]
    [InlineData("{1,2,3}", "{1, 2, 3}")]
    [InlineData("L[2]", "L[2]")]
    [InlineData("f'(x)", "f'(x)")]
    [InlineData("1.5e3", "1.5e3")]
    [InlineData("√x", "sqrt(x)")]
    [InlineData("∛8", "cbrt(8)")]
    public void TypingProducesLinearText(string keys, string expected)
    {
        var s = Typed(keys);
        Assert.Equal(expected, s.Text);
        Assert.NotNull(MathParsing.TryParseStatement(s.Text, RoundTripTests.Ctx, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void SlashTurnsTheOperandIntoTheNumerator()
    {
        var s = Typed("a+2x");
        s.TypeText("/");
        var frac = Assert.IsType<MathTemplate>(s.Root[2]);
        Assert.Equal(MathTemplateKind.Fraction, frac.Kind);
        Assert.Equal("2x", MathSerializer.Serialize(frac[0]));
        Assert.Same(frac[1], s.Caret.Row);
        s.TypeText("3");
        Assert.Equal("a+2x/3", s.Text);
    }

    [Fact]
    public void SlashWithNothingBeforeStartsInTheNumerator()
    {
        var s = Typed("1+");
        s.TypeText("/");
        var frac = (MathTemplate)s.Root[2];
        Assert.Same(frac[0], s.Caret.Row);
    }

    [Fact]
    public void SlashWithSelectionUsesTheSelection()
    {
        var s = Typed("x+1");
        s.SelectAll();
        s.TypeText("/2");
        Assert.Equal("(x+1)/2", s.Text);
    }

    [Fact]
    public void DefiniteIntegralByTypingAndTab()
    {
        var s = Typed("int ");
        var t = Assert.IsType<MathTemplate>(Assert.Single(s.Root.Items));
        Assert.Equal(MathTemplateKind.DefiniteIntegral, t.Kind);
        Assert.Equal("Typing the lower bound. Tab: next slot.", s.CurrentHint);
        s.TypeText("0");
        Assert.True(s.NextSlot());
        Assert.Equal("Typing the upper bound. Tab: next slot.", s.CurrentHint);
        s.TypeText("1");
        s.NextSlot();
        s.TypeText("x^2");
        Assert.Equal("integrate(x^2, x, 0, 1)", s.Text);
        Assert.Equal("Typing the exponent. Tab: leave the power.", s.CurrentHint);
        s.NextSlot();
        Assert.Equal("Typing the integrand. Tab: next slot.", s.CurrentHint);
        s.NextSlot();
        Assert.Equal("Typing the variable. Tab: leave the integral.", s.CurrentHint);
        s.NextSlot();
        Assert.Same(s.Root, s.Caret.Row);
        Assert.Null(s.CurrentHint);
        Assert.False(s.NextSlot());
    }

    [Fact]
    public void SumByTyping()
    {
        var s = Typed("sum");
        var t = (MathTemplate)s.Root[0];
        Assert.Equal(MathTemplateKind.Sum, t.Kind);
        s.TypeText("1");
        s.NextSlot();
        s.TypeText("10");
        s.NextSlot();
        s.TypeText("k^2");
        Assert.Equal("sum(k^2, k, 1, 10)", s.Text);
    }

    [Fact]
    public void LimitByTypingPutsTheVariableFirst()
    {
        var s = Typed("limx");
        s.NextSlot();
        s.TypeText("0");
        s.NextSlot();
        s.TypeText("sin(x)/x");
        Assert.Equal("limit(sin(x)/x, x, 0)", s.Text);
    }

    [Fact]
    public void LogBaseByTyping()
    {
        var s = Typed("log_2");
        s.NextSlot();
        s.TypeText("8");
        Assert.Equal("log(8, 2)", s.Text);
    }

    [Fact]
    public void MatrixTypingWithCommasAndSemicolons()
    {
        var s = new MathEditorState(RoundTripTests.Ctx);
        s.InsertTemplate(MathTemplateKind.Matrix);
        s.TypeText("1,2;3,4");
        Assert.Equal("[[1, 2], [3, 4]]", s.Text);
        s.TypeText(",5");
        Assert.Equal("[[1, 2, ], [3, 4, 5]]", s.Text);
        Assert.True(s.RemoveGridColumn());
        Assert.Equal("[[1, 2], [3, 4]]", s.Text);
        Assert.True(s.AddGridRow());
        s.TypeText("5,6");
        Assert.Equal("[[1, 2], [3, 4], [5, 6]]", s.Text);
    }

    [Fact]
    public void PiecewiseTyping()
    {
        var s = new MathEditorState(RoundTripTests.Ctx);
        s.InsertTemplate(MathTemplateKind.Piecewise);
        s.TypeText("-x,x<0,x");
        Assert.Equal("{x<0: -x, x}", s.Text);
        var p = Parser.ParseExpression(s.Text, RoundTripTests.Ctx);
        Assert.IsType<Calcy.Core.Expressions.PiecewiseExpr>(p);
    }

    [Fact]
    public void BackspaceIntoFractionRemovesItWhenEmpty()
    {
        var s = Typed("1/2");
        s.Backspace();
        Assert.Equal("1/()", s.Text);
        s.Backspace();
        Assert.Equal("1", s.Text);
        Assert.Same(s.Root, s.Caret.Row);
        Assert.Equal(1, s.Caret.Index);
    }

    [Fact]
    public void BackspaceAfterTemplateEntersIt()
    {
        var s = Typed("x^2");
        s.MoveRight();
        Assert.Same(s.Root, s.Caret.Row);
        s.Backspace();
        Assert.Equal(MathTemplateKind.Power, s.Caret.Row.Owner?.Kind);
        s.Backspace();
        s.Backspace();
        Assert.Equal("x", s.Text);
    }

    [Fact]
    public void BackspaceAtStartOfBracketsUnwraps()
    {
        var s = Typed("(a+b)");
        s.MoveLeft();
        s.MoveHome();
        s.Backspace();
        Assert.Equal("a+b", s.Text);
    }

    [Fact]
    public void DeleteForward()
    {
        var s = Typed("abc");
        s.MoveHome();
        s.DeleteForward();
        Assert.Equal("bc", s.Text);
    }

    [Fact]
    public void ArrowsWalkThroughSlots()
    {
        var s = new MathEditorState();
        s.SetText("12/34");
        var frac = (MathTemplate)s.Root[0];
        s.MoveLeft();
        Assert.Equal(new MathCaret(frac[1], 2), s.Caret);
        s.MoveLeft();
        s.MoveLeft();
        Assert.Equal(new MathCaret(frac[1], 0), s.Caret);
        s.MoveLeft();
        Assert.Equal(new MathCaret(frac[0], 2), s.Caret);
        s.MoveHome();
        s.MoveLeft();
        Assert.Equal(new MathCaret(s.Root, 0), s.Caret);
        s.MoveRight();
        Assert.Equal(new MathCaret(frac[0], 0), s.Caret);
        s.MoveEnd(wholeDocument: true);
        Assert.Equal(new MathCaret(s.Root, 1), s.Caret);
    }

    [Fact]
    public void UpAndDownUseTheLayout()
    {
        var s = new MathEditorState();
        s.SetText("12/34");
        var frac = (MathTemplate)s.Root[0];
        s.SetCaret(new MathCaret(frac[0], 1));
        var layout = new MathLayoutEngine(new FixedWidthTextMeasurer()).Layout(s.Root);
        Assert.True(s.MoveDown(layout));
        Assert.Same(frac[1], s.Caret.Row);
        Assert.True(s.MoveUp(layout));
        Assert.Same(frac[0], s.Caret.Row);
        Assert.False(s.MoveUp(layout));
    }

    [Fact]
    public void ShiftTabGoesBackThroughSlots()
    {
        var s = new MathEditorState(RoundTripTests.Ctx);
        s.SetText("integrate(x^2, x, 0, 1)");
        var t = (MathTemplate)s.Root[0];
        Assert.True(s.PreviousSlot());
        Assert.Same(t[3], s.Caret.Row);
        s.PreviousSlot();
        Assert.Same(t[2].Items.OfType<MathTemplate>().Single()[0], s.Caret.Row);
        s.PreviousSlot();
        Assert.Same(t[2], s.Caret.Row);
        s.PreviousSlot();
        Assert.Same(t[1], s.Caret.Row);
    }

    [Fact]
    public void SelectionLiftsToTheCommonRow()
    {
        var s = new MathEditorState();
        s.SetText("a+1/2+b");
        var frac = (MathTemplate)s.Root[2];
        s.SetCaret(new MathCaret(frac[1], 1));
        s.SetCaret(new MathCaret(s.Root, 1), extendSelection: true);
        var sel = s.Selection!.Value;
        Assert.Same(s.Root, sel.Row);
        Assert.Equal(1, sel.Start);
        Assert.Equal(3, sel.End);
        Assert.Equal("+1/2", s.GetSelectedText());
        s.Backspace();
        Assert.Equal("a+b", s.Text);
    }

    [Fact]
    public void ShiftArrowsSelectWholeTemplates()
    {
        var s = Typed("1/2");
        s.MoveLeft(extendSelection: true);
        s.MoveLeft(extendSelection: true);
        Assert.Equal("1/2", s.GetSelectedText());
    }

    [Fact]
    public void UndoAndRedo()
    {
        var s = Typed("abc");
        s.TypeText("+");
        s.TypeText("1");
        Assert.Equal("abc+1", s.Text);
        Assert.True(s.Undo());
        Assert.Equal("abc+", s.Text);
        s.Undo();
        Assert.Equal("abc", s.Text);
        s.Undo();
        Assert.Equal("", s.Text);
        Assert.False(s.CanUndo);
        s.Redo();
        Assert.Equal("abc", s.Text);
        s.Redo();
        s.Redo();
        Assert.Equal("abc+1", s.Text);
        Assert.False(s.CanRedo);
    }

    [Fact]
    public void PasteKeepsStructure()
    {
        var s = Typed("2*");
        s.Paste("(a+b)/2");
        Assert.Equal("2*(a+b)/2", s.Text);
        Assert.Contains(s.Root.Items, n => n is MathTemplate { Kind: MathTemplateKind.Fraction });
    }

    [Fact]
    public void InsertTemplateWrapsSelection()
    {
        var s = Typed("x+1");
        s.SelectAll();
        s.InsertTemplate(MathTemplateKind.Sqrt);
        Assert.Equal("sqrt(x+1)", s.Text);
    }

    [Fact]
    public void FocusFirstEmptySlotFindsHoles()
    {
        var s = new MathEditorState();
        s.InsertTemplate(MathTemplateKind.Fraction);
        s.TypeText("1");
        s.MoveEnd(wholeDocument: true);
        Assert.True(s.FocusFirstEmptySlot());
        Assert.Equal(1, s.Caret.Row.SlotIndex);
    }

    [Fact]
    public void SetTextFallsBackToPlainText()
    {
        var s = new MathEditorState();
        Assert.False(s.SetText("2 + * 3"));
        Assert.Equal("2 + * 3", s.Text.Replace("·", "*"));
    }

    [Fact]
    public void EventsFire()
    {
        var s = new MathEditorState();
        int content = 0, caret = 0;
        s.ContentChanged += (_, _) => content++;
        s.CaretChanged += (_, _) => caret++;
        s.TypeText("x");
        s.MoveLeft();
        Assert.Equal(1, content);
        Assert.Equal(2, caret);
    }

    [Theory]
    [MemberData(nameof(RoundTripTests.Inputs), MemberType = typeof(RoundTripTests))]
    public void TypingTheSerializedTextGivesTheSameExpression(string input)
    {
        // Typing the linear text key by key must mean the same as the text itself.
        var expected = Parser.ParseStatement(input, RoundTripTests.Ctx);
        var built = new MathEditorState(RoundTripTests.Ctx);
        built.SetText(input);
        var typed = Typed(built.Text);
        var actual = Parser.ParseStatement(typed.Text, RoundTripTests.Ctx);
        AssertSameValue(expected, actual, input, typed.Text);
    }

    private static void AssertSameValue(Statement expected, Statement actual, string input, string text)
    {
        // Typing may regroup products (a/b·c typed as a fraction then ·c), so compare printed canonical forms.
        string Show(Statement st) => st switch
        {
            ExprStatement e => Calcy.Core.Printing.ExprPrinter.ToInput(Simplify(e.Expression)),
            AssignStatement a => a.Name + "=" + Calcy.Core.Printing.ExprPrinter.ToInput(Simplify(a.Value)),
            FunctionDefStatement f => f.Name + "(" + string.Join(",", f.Parameters) + ")=" + Calcy.Core.Printing.ExprPrinter.ToInput(Simplify(f.Body)),
            _ => "?",
        };
        Assert.True(Show(expected) == Show(actual), $"'{input}' typed as '{text}': {Show(expected)} vs {Show(actual)}");
    }

    private static Calcy.Core.Expressions.Expr Simplify(Calcy.Core.Expressions.Expr e) => e.Map(x => x switch
    {
        Calcy.Core.Expressions.AddExpr a => Calcy.Core.Expressions.Algebra.Add(a.Terms),
        Calcy.Core.Expressions.MulExpr m => Calcy.Core.Expressions.Algebra.Mul(m.Factors),
        Calcy.Core.Expressions.PowExpr p => Calcy.Core.Expressions.Algebra.Pow(p.Base, p.Exponent),
        _ => x,
    });
}

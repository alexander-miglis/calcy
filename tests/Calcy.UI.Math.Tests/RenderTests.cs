using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;
using Calcy.UI.Math.Model;

namespace Calcy.UI.Math.Tests;

/// <summary>Headless Skia renders of MathView and MathEditor, written as PNGs for a human to look at.</summary>
public class RenderTests
{
    public static TheoryData<string> ViewInputs => new()
    {
        "x^2 + 2x + 1", "(a + b)/(c + d)", "1/(2x)", "sqrt(2)/2", "(1 + sqrt(5))/2", "root(x, 3) + cbrt(8)", "abs(x - 1)",
        "integrate(x^2, x, 0, 1)", "integrate(sin(x), x)", "diff(x^3 + 2x, x)", "sum(1/k^2, k, 1, inf)", "product(k, k, 1, n)",
        "limit(sin(x)/x, x, 0)", "[[1, 2], [3, 4]]", "[[a, b, c], [d, e, f], [g, h, i]]", "{x < 0: -x, x}", "{1, 2, 3}",
        "log(x, 2)", "e^(-x^2/2)", "v_0 t + a t^2/2", "x_1^2 + x_2^2", "sin(θ)^2 + cos(θ)^2 = 1", "f(x) = x^2 - 4",
        "x > 0 and x < 1", "3 + 4i", "2π r", "n!/(k!(n - k)!)", "((1/2)/(3/4))", "1/(1 + 1/(1 + 1/x))", "x^(n+1)/(n+1)",
        "a = 5", "(x + 1)^3", "|x| + |y|", "0.5 + 1e-7", "∞", "nCr(5, 2)",
    };

    [Theory]
    [MemberData(nameof(ViewInputs))]
    public void MathViewRendersText(string text)
    {
        UI.Run(() =>
        {
            var view = new MathView { Text = text, FontSize = 24 };
            var bmp = UI.Capture(view, "view_" + text);
            Assert.Null(view.ErrorMessage);
            Assert.True(view.Bounds.Width > 0 && view.Bounds.Height > 0);
            Assert.True(UI.CountInkPixels(bmp) > 20, "something should be drawn");
        });
    }

    [Fact]
    public void MathViewRendersExpressionsLikeExprPrinter()
    {
        UI.Run(() =>
        {
            // x^-1 * (x + 1) * 3/4  is shown as a single fraction 3(x + 1)/(4x).
            var x = Expr.Sym("x");
            var e = new MulExpr([Expr.Rational(3, 4), new AddExpr([x, Expr.One]), new PowExpr(x, Expr.MinusOne)]);
            var view = new MathView { Expression = e, FontSize = 24 };
            var bmp = UI.Capture(view, "view_expression_fraction");
            var root = view.CurrentLayout!.RootRow.Row;
            var frac = Assert.IsType<MathTemplate>(Assert.Single(root.Items));
            Assert.Equal(MathTemplateKind.Fraction, frac.Kind);
            Assert.True(UI.CountInkPixels(bmp) > 20);
        });
    }

    [Fact]
    public void LongSumWrapsInsteadOfClipping()
    {
        UI.Run(() =>
        {
            var view = new MathView { Text = "x^10 + 10x^9 + 45x^8 + 120x^7 + 210x^6 + 252x^5 + 210x^4 + 120x^3 + 45x^2 + 10x + 1", FontSize = 24, MaxWidth = 300 };
            UI.Capture(view, "view_wrapped_sum");
            var layout = view.CurrentLayout!;
            Assert.True(layout.Width <= 300 + 0.5, $"width {layout.Width}");
            Assert.True(layout.Height > 2 * 24, "should use several lines");
            Assert.Equal(1.0, view.ActualScale);
        });
    }

    [Fact]
    public void WideFractionScalesDown()
    {
        UI.Run(() =>
        {
            var view = new MathView { Text = "(x^10 + 10x^9 + 45x^8 + 120x^7 + 210x^6)/(252x^5 + 210x^4 + 120x^3 + 45x^2)", FontSize = 24, MaxWidth = 250 };
            UI.Capture(view, "view_scaled_fraction");
            Assert.True(view.ActualScale < 1);
            Assert.True(view.Bounds.Width <= 250.5);
        });
    }

    [Fact]
    public void ThemeResourcesAreUsedAndUpdateAtRuntime()
    {
        UI.Run(() =>
        {
            var view = new MathView { Text = "x^2 + 1", FontSize = 32 };
            view.Resources["Calcy.Ink"] = new SolidColorBrush(Colors.Red);
            var bmp = UI.Capture(view, "view_theme_red");
            int red = UI.CountInkPixels(bmp, (r, g, b) => r > 200 && g < 80 && b < 80);
            Assert.True(red > 20, "ink should come from the Calcy.Ink resource");

            var view2 = new MathView { Text = "x^2 + 1", FontSize = 32 };
            view2.Resources["Calcy.Ink"] = new SolidColorBrush(Colors.Red);
            var window = new Window { SizeToContent = SizeToContent.WidthAndHeight, Content = view2 };
            window.Show();
            window.CaptureRenderedFrame();
            view2.Resources["Calcy.Ink"] = new SolidColorBrush(Colors.Blue);
            var frame = window.CaptureRenderedFrame()!;
            int blue = UI.CountInkPixels(frame, (r, g, b) => b > 200 && r < 80 && g < 80);
            Assert.True(blue > 20, "changing the resource re-renders with the new colour");
            window.Close();
        });
    }

    public static TheoryData<string> EditorInputs => new()
    {
        "", "x^2 + 1", "(a + b)/(c + d)", "integrate(x^2, x, 0, 1)", "sum(k^2, k, 1, 10)", "[[1, 2], [3, 4]]",
        "{x < 0: -x, x}", "sqrt(x + 1)", "limit(sin(x)/x, x, 0)", "diff(x^3, x)", "log(x, 2)",
    };

    [Theory]
    [MemberData(nameof(EditorInputs))]
    public void MathEditorRendersTextWithCaret(string text)
    {
        UI.Run(() =>
        {
            var editor = new MathEditor { Text = text, FontSize = 24, Width = 420, PlaceholderText = "Type maths" };
            var bmp = UI.Capture(editor, "editor_" + (text.Length == 0 ? "empty" : text), beforeCapture: _ =>
            {
                editor.Focus();
                editor.FocusFirstEmptySlot();
                if (editor.EditorState.Caret.Row.Owner is null) editor.EditorState.MoveLeft();
            });
            Assert.True(editor.Bounds.Width > 0 && editor.Bounds.Height > 0);
            Assert.True(UI.CountInkPixels(bmp) > 50);
        });
    }

    [Fact]
    public void MathEditorShowsEmptySlotsAndActiveSlot()
    {
        UI.Run(() =>
        {
            var editor = new MathEditor { FontSize = 28, Width = 420 };
            UI.Capture(editor, "editor_templates", beforeCapture: w =>
            {
                editor.Focus();
                editor.InsertTemplate(MathTemplateKind.DefiniteIntegral);
                editor.InsertText("0");
                editor.EditorState.NextSlot();
                Assert.Equal("Typing the upper bound. Tab: next slot.", editor.HintText);
            });
            int sel;
            var bmp = UI.Capture(new MathEditor { Text = "1/2", FontSize = 28, Width = 300 }, "editor_fraction_active", beforeCapture: w =>
            {
                var ed = (MathEditor)((Border)w.Content!).Child!;
                ed.Focus();
                ed.EditorState.SetCaret(new MathCaret(((MathTemplate)ed.EditorState.Root[0])[1], 1));
            });
            sel = UI.CountInkPixels(bmp, (r, g, b) => r > 230 && g > 180 && g < 230 && b < 120);
            Assert.True(sel > 50, "the active slot is filled with the Calcy.Sel colour");
        });
    }

    [Fact]
    public void TypingThroughKeyboardEventsUpdatesText()
    {
        UI.Run(() =>
        {
            var editor = new MathEditor { FontSize = 24, Width = 400 };
            var window = new Window { SizeToContent = SizeToContent.Height, Width = 420, Content = editor };
            window.Show();
            editor.Focus();
            string? submitted = null;
            editor.Submitted += (_, e) => submitted = e.Text;
            Avalonia.Headless.HeadlessWindowExtensions.KeyTextInput(window, "1/2");
            Avalonia.Headless.HeadlessWindowExtensions.KeyPress(window, Avalonia.Input.Key.Right, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.ArrowRight, null);
            Avalonia.Headless.HeadlessWindowExtensions.KeyTextInput(window, "+x");
            Assert.Equal("1/2+x", editor.Text);
            Avalonia.Headless.HeadlessWindowExtensions.KeyPress(window, Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
            Assert.Equal("1/2+x", submitted);
            var frame = window.CaptureRenderedFrame()!;
            frame.Save(Path.Combine(UI.OutputFolder, "editor_typed.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            window.Close();
        });
    }

    [Fact]
    public void ClickPlacesCaretInDenominator()
    {
        UI.Run(() =>
        {
            var editor = new MathEditor { Text = "12/34", FontSize = 32, Width = 300 };
            var window = new Window { SizeToContent = SizeToContent.Height, Width = 320, Content = editor };
            window.Show();
            window.CaptureRenderedFrame();
            var layout = editor.CurrentLayout!;
            var frac = (MathTemplate)editor.EditorState.Root[0];
            var den = layout.Find(frac[1])!;
            // Click on the right half of the denominator's last digit.
            var local = new Point(den.Bounds.Right - 2, den.Origin.Y - 4);
            var origin = editor.TranslatePoint(new Point(0, 0), window)!.Value;
            var offset = ContentOffset(editor);
            var p = new Point(origin.X + offset.X + local.X, origin.Y + offset.Y + local.Y);
            Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window, p, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
            Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window, p, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
            Assert.Same(frac[1], editor.EditorState.Caret.Row);
            Assert.Equal(2, editor.EditorState.Caret.Index);
            window.Close();
        });
    }

    private static Point ContentOffset(MathEditor editor)
    {
        var m = typeof(MathEditor).GetMethod("ContentOrigin", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (Point)m.Invoke(editor, null)!;
    }
}

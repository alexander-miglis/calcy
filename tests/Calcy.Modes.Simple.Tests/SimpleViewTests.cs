using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Calcy.UI.Controls;
using Calcy.UI.Services;
using Calcy.UI.TestKit;
using Calcy.UI.Theming;

namespace Calcy.Modes.Simple.Tests;

public class SimpleViewTests
{
    // The app shows a mode below the 64 px top bar and above the status line.
    private const double Width = 1360;
    private const double Height = 790;

    private static (SimpleView View, Window Window, AppServices Services) Open(string scheme = "nes", double width = Width, double height = Height)
    {
        var services = Headless.CreateServices(scheme);
        var view = (SimpleView)new SimpleMode().CreateView(services);
        var window = Headless.Show(view, width, height);
        view.FocusFirst();
        Headless.Pump();
        return (view, window, services);
    }

    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Headless.Pump(1);
    }

    private static void Click(SimpleView view, params CalcKey[] keys)
    {
        foreach (var k in keys) Click(view.Keys[k]);
    }

    private static void Type(Window window, string text)
    {
        foreach (var c in text) window.KeyTextInput(c.ToString());
        Headless.Pump(1);
    }

    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        Headless.Pump(1);
    }

    private static void EndFlashes(SimpleView view)
    {
        foreach (var k in view.Keys.Values) k.EndFlash();
        Headless.Pump();
    }

    /// <summary>Builds the state shown in docs/design/Simple.dc.html, mixing clicks and typing.</summary>
    private static void FillLikeDesign(SimpleView view, Window window)
    {
        Type(window, "19.99*3");
        Press(window, Key.Enter);
        Click(view, CalcKey.MemoryAdd);
        Click(view, CalcKey.Digit1, CalcKey.Divide, CalcKey.Digit3, CalcKey.Equals);
        Type(window, "2@");
        Type(window, "250+15%");
        Click(view, CalcKey.Digit8, CalcKey.Digit6, CalcKey.Multiply, CalcKey.Digit1, CalcKey.Digit2, CalcKey.Equals);
        Type(window, "1200+34.5");
        Press(window, Key.Enter);
        EndFlashes(view);
    }

    [Theory]
    [InlineData("nes")]
    [InlineData("pico")]
    [InlineData("gameboy")]
    [InlineData("amber")]
    [InlineData("paper")]
    [InlineData("c64")]
    [InlineData("mono")]
    [InlineData("synth")]
    public void Screenshot_matches_the_design(string scheme)
    {
        var path = Screens.Run(() =>
        {
            var (view, window, _) = Open(scheme);
            FillLikeDesign(view, window);

            Assert.Equal("1,234.5", view.Calculator.Display);
            Assert.Equal("1,200 + 34.5 =", view.Calculator.Expression);
            Assert.Equal(6, view.Cards.Count);
            Assert.Equal(["#6", "#5", "#4", "#3", "#2", "#1"], view.Cards.Select(c => c.Card.Title));
            Assert.Equal(["= 1,234.5", "= 1,032", "= 287.5", "= 1.414213562", "= 0.3333333333", "= 59.97"],
                view.Cards.Select(c => "= " + c.Line.ResultText));
            Assert.True(view.MemoryIndicator.IsVisible);
            return Headless.Save(window, $"simple-{scheme}.png");
        });
        Assert.True(new FileInfo(path).Length > 10_000, path);
    }

    [Fact]
    public void Editing_in_a_small_window_keeps_the_box_inside_the_card()
    {
        // The size from a user report: a 1118 px wide app window, where the box used to stick out of the card.
        var path = Screens.Run(() =>
        {
            var (view, window, _) = Open("paper", 1100, 900);
            Type(window, "5*5=");
            EndFlashes(view);
            var card = view.Cards[0];
            Click(card.EditButton);
            Headless.Pump();
            var box = card.Editor;
            var topLeft = Avalonia.VisualExtensions.TranslatePoint(box, new Avalonia.Point(0, 0), card.Card)!.Value;
            Assert.True(topLeft.X >= 0, $"box starts {topLeft.X} px left of the card");
            Assert.True(topLeft.X + box.Bounds.Width <= card.Card.Bounds.Width, "box ends past the card's right edge");
            Assert.True(box.Bounds.Width >= card.Card.Bounds.Width * 0.6, $"box is only {box.Bounds.Width} px wide");
            return Headless.Save(window, "simple-edit-small-paper.png");
        });
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Screenshot_while_editing_a_tape_line()
    {
        var path = Screens.Run(() =>
        {
            var (view, window, _) = Open();
            Type(window, "100+50=*2=-25=");
            EndFlashes(view);
            var first = view.Cards[^1];
            Click(first.EditButton);
            Assert.True(first.IsEditing);
            Assert.Equal("100 + 50", first.Editor.Text);
            first.Editor.Text = "100 + 70";
            Headless.Pump();
            return Headless.Save(window, "simple-edit-nes.png");
        });
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Screenshot_of_an_error_and_a_small_window()
    {
        Screens.Run(() =>
        {
            // The smallest main window (900 x 600) leaves 900 x 510 for the mode below the top bar.
            var (view, window, _) = Open("nes", 900, 510);
            Type(window, "5/0=");
            EndFlashes(view);
            Assert.Equal("Error", view.Calculator.Display);
            Headless.Save(window, "simple-small-error-nes.png");
        });
    }

    [Fact]
    public void Screenshot_of_long_numbers_and_flashing_key()
    {
        Screens.Run(() =>
        {
            var (view, window, _) = Open("pico");
            Type(window, "12345678901234*99999=");
            Type(window, "12345678901234");
            EndFlashes(view);
            Headless.Save(window, "simple-long-pico.png");
            Assert.True(view.DisplayText.RenderedPixelSize < 7);
            Assert.Equal("12,345,678,901,234", view.DisplayText.RenderedText);
            Assert.Equal("= 1.234555544E18", view.Cards[0].Card.Content is StackPanel ? "= " + view.Cards[0].Line.ResultText : "");
            Type(window, "7");
            Assert.True(view.Keys[CalcKey.Digit7].IsFlashing);
            Headless.Save(window, "simple-flash-pico.png");
            EndFlashes(view);
        });
    }

    [Fact]
    public void Clicking_keys_calculates_left_to_right()
    {
        Screens.Run(() =>
        {
            var (view, _, _) = Open();
            Click(view, CalcKey.Digit2, CalcKey.Add, CalcKey.Digit3, CalcKey.Multiply, CalcKey.Digit4);
            Assert.Equal("4", view.DisplayText.Text);
            Assert.Equal("2 + 3 ×", view.ExpressionText.Text);
            Click(view, CalcKey.Equals);
            Assert.Equal("20", view.DisplayText.Text);
            Assert.Equal("2 + 3 × 4 =", view.ExpressionText.Text);
            Assert.Single(view.Cards);
        });
    }

    [Fact]
    public void Keyboard_drives_every_key()
    {
        Screens.Run(() =>
        {
            var (view, window, _) = Open();
            Type(window, "1,5*4");
            Assert.Equal("4", view.Calculator.Display);
            Press(window, Key.Enter);
            Assert.Equal("6", view.Calculator.Display);
            Type(window, "123");
            Press(window, Key.Back);
            Assert.Equal("12", view.Calculator.Display);
            Press(window, Key.F9);
            Assert.Equal("-12", view.Calculator.Display);
            Press(window, Key.Delete);
            Assert.Equal("0", view.Calculator.Display);
            Type(window, "9");
            Press(window, Key.P, RawInputModifiers.Control);
            Assert.True(view.MemoryIndicator.IsVisible);
            Press(window, Key.Escape);
            Assert.Equal("0", view.Calculator.Display);
            Press(window, Key.R, RawInputModifiers.Control);
            Assert.Equal("9", view.Calculator.Display);
            Type(window, "q");
            Assert.Equal("81", view.Calculator.Display);
            Type(window, "r");
            Assert.Equal("0.01234567901", view.Calculator.Display);
            Press(window, Key.L, RawInputModifiers.Control);
            Assert.False(view.MemoryIndicator.IsVisible);
            Type(window, "200-10%");
            Assert.Equal("180", view.Calculator.Display);
            Type(window, "8/2=");
            Assert.Equal("4", view.Calculator.Display);
            EndFlashes(view);
        });
    }

    [Fact]
    public void Typed_keys_flash_on_the_keypad()
    {
        Screens.Run(() =>
        {
            var (view, window, _) = Open();
            Type(window, "+");
            Assert.True(view.Keys[CalcKey.Add].IsFlashing);
            Assert.Contains(":pressed", view.Keys[CalcKey.Add].Classes);
            Press(window, Key.Enter);
            Assert.True(view.Keys[CalcKey.Equals].IsFlashing);
            EndFlashes(view);
            Assert.False(view.Keys[CalcKey.Add].IsFlashing);
        });
    }

    [Fact]
    public void Keys_have_accessible_names_and_design_colours()
    {
        Screens.Run(() =>
        {
            var (view, _, _) = Open();
            Assert.Equal(27, view.Keys.Count);
            foreach (var (key, button) in view.Keys)
            {
                Assert.Equal(CalcKeys.AutomationName(key), AutomationProperties.GetName(button));
                Assert.False(button.Focusable);
            }
            Assert.Equal("memory plus", AutomationProperties.GetName(view.Keys[CalcKey.MemoryAdd]));
            Assert.Equal("square root", AutomationProperties.GetName(view.Keys[CalcKey.SquareRoot]));
            Assert.Contains("mem", view.Keys[CalcKey.MemoryClear].Classes);
            Assert.Contains("head", view.Keys[CalcKey.Divide].Classes);
            Assert.Contains("primary", view.Keys[CalcKey.Equals].Classes);
            Assert.Contains("err", view.Keys[CalcKey.Clear].Classes);
            Assert.Contains("paper", view.Keys[CalcKey.Digit5].Classes);
            Assert.Equal(2, Grid.GetColumnSpan(view.Keys[CalcKey.Digit0]));
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(view.DisplayText));
        });
    }

    [Fact]
    public void Colours_follow_the_scheme()
    {
        Screens.Run(() =>
        {
            var (view, _, services) = Open("nes");
            Assert.Equal(ColorSchemes.Get("nes")["LcdInk"], ((ISolidColorBrush)view.DisplayText.Foreground!).Color);
            services.SetScheme("amber");
            Headless.Pump();
            Assert.Equal(ColorSchemes.Get("amber")["LcdInk"], ((ISolidColorBrush)view.DisplayText.Foreground!).Color);
            Assert.Equal(ColorSchemes.Get("amber")["Mem"], ((ISolidColorBrush)view.Keys[CalcKey.MemoryAdd].Background!).Color);
        });
    }

    [Fact]
    public void Use_puts_a_tape_result_on_the_display()
    {
        Screens.Run(() =>
        {
            var (view, window, _) = Open();
            Type(window, "6*7=1+1=");
            var first = view.Cards[^1];
            Click(first.UseButton);
            Assert.Equal("42", view.Calculator.Display);
            Assert.True(view.IsFocused);
            Type(window, "+8=");
            Assert.Equal("50", view.Calculator.Display);
            EndFlashes(view);
        });
    }

    [Fact]
    public void Editing_a_card_recalculates_the_cards_below()
    {
        Screens.Run(() =>
        {
            var (view, window, _) = Open();
            Type(window, "100+50=*2=-25=");
            var first = view.Cards[^1];
            Click(first.EditButton);
            Assert.True(first.Editor.IsFocused);
            first.Editor.Text = "100 + 70";
            Press(window, Key.Enter);
            Assert.False(first.IsEditing);
            Assert.Equal("#1 · EDITED", first.Card.Title);
            Assert.Equal(["= 315", "= 340", "= 170"], view.Cards.Select(c => "= " + c.Line.ResultText));
            Assert.Equal("315", view.Calculator.Display);
            Assert.True(view.IsFocused);
            EndFlashes(view);
        });
    }

    [Fact]
    public void A_rejected_edit_keeps_the_editor_open_with_a_message()
    {
        Screens.Run(() =>
        {
            var (view, window, _) = Open();
            Type(window, "2+3=");
            var card = view.Cards[0];
            card.BeginEdit();
            card.Editor.Text = "2 × 3";
            Assert.False(card.Commit());
            Assert.True(card.IsEditing);
            Type(window, "9");
            Assert.Equal("5", view.Calculator.Display);
            Press(window, Key.Escape);
            Assert.False(card.IsEditing);
            Assert.Equal("= 5", "= " + card.Line.ResultText);
            EndFlashes(view);
        });
    }

    [Fact]
    public void Clear_empties_the_tape()
    {
        Screens.Run(() =>
        {
            var (view, window, _) = Open();
            Assert.False(view.ClearTapeButton.IsEnabled);
            Type(window, "1+1=2+2=");
            Assert.True(view.ExportButton.IsEnabled);
            Click(view.ClearTapeButton);
            Assert.Empty(view.Cards);
            Assert.Empty(view.Calculator.Tape);
            Assert.False(view.TapeScroll.IsVisible);
            Assert.Equal("4", view.Calculator.Display);
            Click(view.ExportButton);
            EndFlashes(view);
        });
    }

    [Fact]
    public void Export_copies_the_tape_as_text()
    {
        Screens.Run(() =>
        {
            var (view, window, _) = Open();
            Type(window, "250+15%");
            Click(view.ExportButton);
            Headless.Pump();
            Assert.EndsWith("250 + 15% = 287.5", view.Calculator.ExportTape());
            EndFlashes(view);
        });
    }

    [Fact]
    public void Receives_numbers_and_simple_expressions_from_other_modes()
    {
        Screens.Run(() =>
        {
            var (view, _, services) = Open();
            view.Receive(new InputRequest("1234.5"));
            Assert.Equal("1,234.5", view.Calculator.Display);
            view.Receive(new InputRequest("1/3", Evaluate: true));
            Assert.Equal("0.3333333333", view.Calculator.Display);
            view.Calculator.Type("*3=");
            Assert.Equal("1", view.Calculator.Display);
            view.Receive(new InputRequest("sqrt(2)"));
            Assert.Equal("1.414213562", view.Calculator.Display);
            view.Receive(2.5);
            Assert.Equal("2.5", view.Calculator.Display);
            Assert.True(services.Session.Env.TryGetVariable("ans", out _));
        });
    }

    [Fact]
    public void Mode_metadata()
    {
        var mode = (ICalcyMode)Activator.CreateInstance(typeof(SimpleMode))!;
        Assert.Equal("simple", mode.Id);
        Assert.Equal("Simple", mode.Title);
        Assert.Equal("Everyday sums, memory keys, tape", mode.Description);
        Assert.Equal("Ctrl+1", mode.Shortcut);
        Assert.Equal("Lcd", mode.AccentToken);
        Assert.Equal("simple", mode.Icon);
        Screens.Run(() =>
        {
            var view = mode.CreateView(Headless.CreateServices());
            Assert.IsAssignableFrom<IReceivesPayload>(view);
            Assert.IsAssignableFrom<IFocusFirst>(view);
        });
    }
}

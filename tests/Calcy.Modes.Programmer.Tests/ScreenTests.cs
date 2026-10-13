using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Calcy.UI.Services;
using Calcy.UI.TestKit;

namespace Calcy.Modes.Programmer.Tests;

/// <summary>Headless tests of the Programmer screen: interaction through real input, and screenshots in several schemes.</summary>
public class ScreenTests
{
    public static TheoryData<string> Schemes => ["nes", "pico", "c64", "amber", "mono"];

    private static T Run<T>(Func<T> body) => Headless.Run(body);

    private static (Window Window, ProgrammerView View) Open(string scheme = "nes")
    {
        var services = Headless.CreateServices(scheme);
        var view = (ProgrammerView)new ProgrammerMode().CreateView(services);
        var window = Headless.Show(view);
        view.FocusFirst();
        Headless.Pump();
        return (window, view);
    }

    private static void Type(Window w, string text)
    {
        w.KeyTextInput(text);
        Headless.Pump(1);
    }

    private static void Press(Window w, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        w.KeyPress(key, modifiers, PhysicalKey.None, null);
        w.KeyRelease(key, modifiers, PhysicalKey.None, null);
        Headless.Pump(1);
    }

    private static void Click(Window w, Control target, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var p = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), w) ?? throw new InvalidOperationException("Control is not in the window");
        w.MouseMove(p, modifiers);
        w.MouseDown(p, MouseButton.Left, modifiers);
        w.MouseUp(p, MouseButton.Left, modifiers);
        Headless.Pump(1);
    }

    /// <summary>The state shown in the design: value 0xB5, HEX input, a few cards.</summary>
    private static void Populate(Window w, ProgrammerView v)
    {
        Type(w, "0xFF AND 0x0F");
        Press(w, Key.Enter);
        v.Calculator.SetInputBase(NumberBase.Dec);
        foreach (var e in new[] { "1 << 10", "NOT 0", "0x7FFF + 1", "i8(255)" })
        {
            Type(w, e);
            Press(w, Key.Enter);
        }
        v.Calculator.SetInputBase(NumberBase.Hex);
        Type(w, "B5");
        Press(w, Key.Enter);
        Headless.Pump();
    }

    [Fact]
    public void Typing_and_enter_add_cards()
    {
        var (count, value, expr) = Run(() =>
        {
            var (w, v) = Open();
            Type(w, "FF AND 0F");
            Press(w, Key.Enter);
            var r = (v.Calculator.Cards.Count, v.Calculator.Value, v.Integer.ExpressionBox.Text);
            w.Close();
            return r;
        });
        Assert.Equal(1, count);
        Assert.Equal(new BigInteger(15), value);
        Assert.True(string.IsNullOrEmpty(expr));
    }

    [Fact]
    public void Live_preview_and_escape()
    {
        var (status, after) = Run(() =>
        {
            var (w, v) = Open();
            Type(w, "2 * 3");
            var s = v.Integer.StatusText;
            Press(w, Key.Escape);
            var r = (s, v.Integer.ExpressionBox.Text ?? "");
            w.Close();
            return r;
        });
        Assert.StartsWith("= 0x6", status);
        Assert.Equal("", after);
    }

    [Fact]
    public void Keypad_buttons_edit_the_expression()
    {
        var (expr, value) = Run(() =>
        {
            var (w, v) = Open();
            foreach (var k in new[] { "B", "5", "AND", "F" }) Click(w, v.Integer.KeyButton(k));
            var e = v.Integer.ExpressionBox.Text;
            Click(w, v.Integer.KeyButton("="));
            var r = (e, v.Calculator.Value);
            w.Close();
            return r;
        });
        Assert.Equal("B5 AND F", expr);
        Assert.Equal(new BigInteger(5), value);
    }

    [Fact]
    public void Clicking_a_row_changes_the_input_base_and_disables_digits()
    {
        var (b, twoEnabled, oneEnabled) = Run(() =>
        {
            var (w, v) = Open();
            Click(w, v.Integer.RowButton(NumberBase.Bin));
            var r = (v.Calculator.InputBase, v.Integer.KeyButton("2").IsEnabled, v.Integer.KeyButton("1").IsEnabled);
            w.Close();
            return r;
        });
        Assert.Equal(NumberBase.Bin, b);
        Assert.False(twoEnabled);
        Assert.True(oneEnabled);
    }

    [Fact]
    public void Clicking_bits_flips_them_and_shift_click_selects_a_field()
    {
        var (value, info, extracted) = Run(() =>
        {
            var (w, v) = Open();
            Click(w, v.Integer.BitButton(15));
            Click(w, v.Integer.BitButton(0));
            var val = v.Calculator.Value;
            Click(w, v.Integer.BitButton(7));
            Click(w, v.Integer.BitButton(4), RawInputModifiers.Shift);
            Headless.Pump();
            var field = FindText(v, t => t.StartsWith("bits 7..4", StringComparison.Ordinal));
            w.Close();
            return (val, field, v.Calculator.Value);
        });
        Assert.Equal(new BigInteger(-32767), value);
        Assert.NotNull(info);
        Assert.Equal(new BigInteger(-32767 ^ 0x80), extracted);
    }

    [Fact]
    public void Typing_outside_the_box_goes_to_the_expression()
    {
        var expr = Run(() =>
        {
            var (w, v) = Open();
            v.Integer.RowButton(NumberBase.Hex).Focus();
            Headless.Pump(1);
            Type(w, "A1");
            var e = v.Integer.ExpressionBox.Text;
            w.Close();
            return e;
        });
        Assert.Equal("A1", expr);
    }

    [Fact]
    public void Input_requests_from_other_modes_are_evaluated()
    {
        var value = Run(() =>
        {
            var (w, v) = Open();
            ((IReceivesPayload)v).Receive(new InputRequest("0x10 << 2", Evaluate: true));
            Headless.Pump();
            var r = v.Calculator.Value;
            w.Close();
            return r;
        });
        Assert.Equal(new BigInteger(64), value);
    }

    [Fact]
    public void Decimal_payloads_keep_their_value_in_hex()
    {
        var (value, expr) = Run(() =>
        {
            var (w, v) = Open();
            ((IReceivesPayload)v).Receive(new InputRequest("255 + 1", Evaluate: true));
            Headless.Pump();
            var r = (v.Calculator.Value, v.Calculator.Cards[^1].Expression);
            w.Close();
            return r;
        });
        Assert.Equal(new BigInteger(256), value);
        Assert.Equal("FF + 1", expr);
    }

    [Fact]
    public void Function_keys_switch_base_and_arrows_recall_history()
    {
        var (b, up1, up2, down) = Run(() =>
        {
            var (w, v) = Open();
            Press(w, Key.F6);
            var nb = v.Calculator.InputBase;
            foreach (var e in new[] { "1 + 1", "2 * 3" })
            {
                Type(w, e);
                Press(w, Key.Enter);
            }
            Press(w, Key.Up);
            var a = v.Integer.ExpressionBox.Text;
            Press(w, Key.Up);
            var c = v.Integer.ExpressionBox.Text;
            Press(w, Key.Down);
            var d = v.Integer.ExpressionBox.Text;
            w.Close();
            return (nb, a, c, d);
        });
        Assert.Equal(NumberBase.Dec, b);
        Assert.Equal("2 * 3", up1);
        Assert.Equal("1 + 1", up2);
        Assert.Equal("2 * 3", down);
    }

    [Fact]
    public void Settings_survive_a_new_view()
    {
        var (bits, signed, b, tab, format) = Run(() =>
        {
            var services = Headless.CreateServices("nes");
            var first = (ProgrammerView)new ProgrammerMode().CreateView(services);
            first.Calculator.SetWordSize(32);
            first.Calculator.SetSigned(false);
            first.Calculator.SetInputBase(NumberBase.Bin);
            first.Float.SetFormat(IeeeFormat.Single);
            first.SelectTab("Float");
            var second = (ProgrammerView)new ProgrammerMode().CreateView(services);
            return (second.Calculator.Format.Bits, second.Calculator.Format.Signed, second.Calculator.InputBase, second.CurrentTab, second.Float.Format.Name);
        });
        Assert.Equal(32, bits);
        Assert.False(signed);
        Assert.Equal(NumberBase.Bin, b);
        Assert.Equal("Float", tab);
        Assert.Equal("single", format);
    }

    [Fact]
    public void Mode_metadata()
    {
        var m = new ProgrammerMode();
        Assert.Equal("programmer", m.Id);
        Assert.Equal("Programmer", m.Title);
        Assert.Equal("Hex, binary, bits, word sizes", m.Description);
        Assert.Equal("Ctrl+4", m.Shortcut);
        Assert.Equal("Ok", m.AccentToken);
        Assert.Equal("programmer", m.Icon);
    }

    [Theory, MemberData(nameof(Schemes))]
    public void Screenshot_integer(string scheme)
    {
        var path = Run(() =>
        {
            var (w, v) = Open(scheme);
            Populate(w, v);
            var p = Headless.Save(w, $"programmer-{scheme}.png");
            w.Close();
            return p;
        });
        Assert.True(File.Exists(path));
    }

    [Theory, MemberData(nameof(Schemes))]
    public void Screenshot_float(string scheme)
    {
        var path = Run(() =>
        {
            var (w, v) = Open(scheme);
            v.SelectTab("Float");
            Headless.Pump();
            var p = Headless.Save(w, $"programmer-float-{scheme}.png");
            w.Close();
            return p;
        });
        Assert.True(File.Exists(path));
    }

    [Theory, MemberData(nameof(Schemes))]
    public void Screenshot_text(string scheme)
    {
        var path = Run(() =>
        {
            var (w, v) = Open(scheme);
            v.SelectTab("Text");
            Headless.Pump();
            var p = Headless.Save(w, $"programmer-text-{scheme}.png");
            w.Close();
            return p;
        });
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Screenshot_variants()
    {
        var paths = Run(() =>
        {
            var list = new List<string>();
            var (w, v) = Open("nes");
            Populate(w, v);
            Type(w, "ans XOR 0F");
            Click(w, v.Integer.BitButton(7));
            Click(w, v.Integer.BitButton(4), RawInputModifiers.Shift);
            list.Add(Headless.Save(w, "programmer-live-nes.png"));
            v.Integer.Escape();
            Type(w, "B5 +* 2");
            list.Add(Headless.Save(w, "programmer-error-nes.png"));
            v.Integer.Escape();
            v.Calculator.SetWordSize(64);
            v.Calculator.SetSigned(false);
            Type(w, "0xDEAD_BEEF_CAFE_F00D");
            Press(w, Key.Enter);
            list.Add(Headless.Save(w, "programmer-64-nes.png"));
            v.SelectTab("Float");
            v.Float.SetFormat(IeeeFormat.Quad);
            v.Float.SetText("1/3");
            Headless.Pump();
            list.Add(Headless.Save(w, "programmer-float-quad-nes.png"));
            v.Float.SetFormat(IeeeFormat.Half);
            v.Float.SetText("-nan(0x2A)");
            Headless.Pump();
            list.Add(Headless.Save(w, "programmer-float-nan-nes.png"));
            ((ScrollViewer)v.Float.Content!).ScrollToEnd();
            Headless.Pump();
            list.Add(Headless.Save(w, "programmer-float-fixed-nes.png"));
            v.SelectTab("Text");
            v.Text.SetText("Hi \U0001F600\n");
            v.Text.SetBytes("C3 28", TextEncodingKind.Utf8);
            Headless.Pump();
            ((ScrollViewer)v.Text.Content!).ScrollToEnd();
            Headless.Pump();
            list.Add(Headless.Save(w, "programmer-text-ascii-nes.png"));
            w.Close();

            // The size the app gives a mode (1360 x 880 window minus the top bar and the status line).
            var services = Headless.CreateServices("nes");
            var view = (ProgrammerView)new ProgrammerMode().CreateView(services);
            var app = Headless.Show(view, 1360, 786);
            view.FocusFirst();
            Populate(app, view);
            list.Add(Headless.Save(app, "programmer-app-size-nes.png"));
            app.Close();
            return list;
        });
        Assert.All(paths, p => Assert.True(File.Exists(p)));
    }

    private static string? FindText(Control root, Func<string, bool> match)
    {
        foreach (var d in root.GetVisualDescendants())
            if (d is TextBlock t && t.Text is { } s && match(s)) return s;
        return null;
    }
}

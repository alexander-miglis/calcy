using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Calcy.UI.Controls;
using Calcy.UI.Services;

namespace Calcy.Modes.Programmer;

/// <summary>Programmer settings kept between sessions (in the user's preferences).</summary>
public sealed class ProgrammerSettings
{
    /// <summary>Word size in bits.</summary>
    public int Bits { get; set; } = 16;

    /// <summary>Signed (two's complement) or unsigned.</summary>
    public bool Signed { get; set; } = true;

    /// <summary>Overflow behaviour.</summary>
    public OverflowMode Overflow { get; set; } = OverflowMode.Wrap;

    /// <summary>Input base.</summary>
    public NumberBase Base { get; set; } = NumberBase.Hex;

    /// <summary>Open tab: Integer, Float or Text.</summary>
    public string Tab { get; set; } = "Integer";

    /// <summary>IEEE format name in the Float tab.</summary>
    public string FloatFormat { get; set; } = "double";

    /// <summary>Fixed point: integer bits m.</summary>
    public int FixedM { get; set; }

    /// <summary>Fixed point: fraction bits n.</summary>
    public int FixedN { get; set; } = 15;

    /// <summary>Fixed point: signed or unsigned.</summary>
    public bool FixedSigned { get; set; } = true;
}

/// <summary>The Programmer screen: Integer, Float and Text tabs on the left, the cards on the right.</summary>
internal sealed class ProgrammerView : UserControl, IReceivesPayload, IFocusFirst
{
    /// <summary>Tab names in order.</summary>
    public static readonly string[] TabNames = ["Integer", "Float", "Text"];

    private readonly AppServices _services;
    private readonly Dictionary<string, PixelToggle> _tabButtons = [];
    private readonly ContentControl _host = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private bool _restoring;
    private ProgrammerSettings? _saved;

    /// <summary>Builds the screen on the shared services (status messages, saved preferences).</summary>
    public ProgrammerView(AppServices services)
    {
        _services = services;
        Calculator = new ProgrammerCalculator();
        Integer = new IntegerTab(Calculator, services.ShowStatus);
        Float = new FloatTab(services.ShowStatus);
        Text = new TextTab();
        Cards = new CardsPanel(Calculator);
        Cards.Use += n => { SelectTab("Integer"); Integer.InsertText($"#{n}"); };

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 14) };
        foreach (var name in TabNames)
        {
            var t = new PixelToggle
            {
                Content = Ui.Pixel(name.ToUpperInvariant(), 2),
                MinHeight = 44,
                MinWidth = 110,
                Padding = new Thickness(16, 0),
                BorderThickness = new Thickness(3),
                Classes = { "shadow" },
                Focusable = false,
            };
            ToolTip.SetTip(t, name switch
            {
                "Integer" => "Hex, decimal, octal and binary; bits; C-style expressions",
                "Float" => "IEEE 754 bits of half, single, double and quad; fixed point Qm.n",
                _ => "Characters, code points, UTF-8 and UTF-16 bytes, checksums",
            });
            var n = name;
            t.Click += (_, _) => SelectTab(n);
            _tabButtons[name] = t;
            tabs.Children.Add(t);
        }

        var left = new DockPanel();
        DockPanel.SetDock(tabs, Dock.Top);
        left.Children.Add(tabs);
        left.Children.Add(_host);

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,372"), ColumnSpacing = 20, Margin = new Thickness(20, 20, 14, 14) };
        root.Children.Add(left.Cell(0, 0));
        Cards.Margin = new Thickness(0, 0, 6, 6);
        root.Children.Add(Cards.Cell(0, 1));
        Content = root;
        this.Token(FontFamilyProperty, "Font.Body");

        Restore();
        Calculator.Changed += (_, _) => Save();
        Float.SettingsChanged += (_, _) => Save();

        AddHandler(TextInputEvent, OnTextInput, RoutingStrategies.Bubble);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
    }

    /// <summary>The integer calculator behind the screen.</summary>
    public ProgrammerCalculator Calculator { get; }

    /// <summary>The Integer tab.</summary>
    public IntegerTab Integer { get; }

    /// <summary>The Float tab.</summary>
    public FloatTab Float { get; }

    /// <summary>The Text tab.</summary>
    public TextTab Text { get; }

    /// <summary>The cards panel.</summary>
    public CardsPanel Cards { get; }

    /// <summary>The open tab name.</summary>
    public string CurrentTab { get; private set; } = "Integer";

    /// <summary>Shows a tab: Integer, Float or Text.</summary>
    public void SelectTab(string name)
    {
        if (!TabNames.Contains(name)) name = "Integer";
        CurrentTab = name;
        foreach (var (n, t) in _tabButtons) t.IsChecked = n == name;
        _host.Content = name switch
        {
            "Float" => Float,
            "Text" => Text,
            _ => Integer,
        };
        Save();
        Dispatcher.UIThread.Post(FocusFirst, DispatcherPriority.Background);
    }

    /// <inheritdoc />
    public void FocusFirst()
    {
        switch (CurrentTab)
        {
            case "Float": Float.Input.Focus(); break;
            case "Text": Text.Input.Focus(); break;
            default: Integer.FocusExpression(); break;
        }
    }

    /// <inheritdoc />
    public void Receive(object payload)
    {
        switch (payload)
        {
            case InputRequest input:
                SelectTab("Integer");
                Integer.SetExpression(FromOtherMode(input.Text), input.Evaluate);
                break;
            case string text:
                SelectTab("Integer");
                Integer.SetExpression(FromOtherMode(text), false);
                break;
        }
    }

    /// <summary>Other modes write decimal numbers; rewrite them so they keep their value in the current input base.</summary>
    private string FromOtherMode(string text) => ExpressionText.ChangeBase(text, NumberBase.Dec, Calculator.InputBase);

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        // Typing while the focus is on a row, a bit or a button still goes to the expression.
        if (e.Handled || CurrentTab != "Integer" || e.Source is TextBox || string.IsNullOrEmpty(e.Text)) return;
        if (e.Text.Any(char.IsControl)) return;
        Integer.InsertText(e.Text);
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || CurrentTab != "Integer") return;
        if (e.KeyModifiers == KeyModifiers.None)
        {
            NumberBase? b = e.Key switch
            {
                Key.F5 => NumberBase.Hex,
                Key.F6 => NumberBase.Dec,
                Key.F7 => NumberBase.Oct,
                Key.F8 => NumberBase.Bin,
                _ => null,
            };
            if (b is { } nb)
            {
                Calculator.SetInputBase(nb);
                e.Handled = true;
                return;
            }
        }
        if (e.Source is TextBox) return;
        switch (e.Key)
        {
            case Key.Enter:
                Integer.PressKey("=");
                e.Handled = true;
                break;
            case Key.Escape:
                Integer.Escape();
                e.Handled = true;
                break;
            case Key.Back:
                Integer.PressKey("⌫");
                e.Handled = true;
                break;
        }
    }

    private void Restore()
    {
        _restoring = true;
        var s = _services.Prefs.GetModeState<ProgrammerSettings>("programmer") ?? new ProgrammerSettings();
        _saved = s;
        try
        {
            if (s.Bits is >= 1 and <= IntegerFormat.MaxBits) Calculator.SetWordSize(s.Bits);
            Calculator.SetSigned(s.Signed);
            if (Enum.IsDefined(s.Overflow)) Calculator.SetOverflow(s.Overflow);
            if (Enum.IsDefined(s.Base)) Calculator.SetInputBase(s.Base);
            Float.Restore(s.FloatFormat, Math.Clamp(s.FixedM, 0, 255), Math.Clamp(s.FixedN, 0, 255), s.FixedSigned);
        }
        catch (ProgrammerException)
        {
            // Saved settings that no longer make sense are ignored.
        }
        _restoring = false;
        SelectTab(s.Tab);
    }

    private void Save()
    {
        if (_restoring) return;
        var f = Calculator.Format;
        var (m, n, fs) = Float.FixedSettings;
        var s = new ProgrammerSettings
        {
            Bits = f.Bits,
            Signed = f.Signed,
            Overflow = f.Overflow,
            Base = Calculator.InputBase,
            Tab = CurrentTab,
            FloatFormat = Float.Format.Name,
            FixedM = m,
            FixedN = n,
            FixedSigned = fs,
        };
        if (_saved is { } last && last.Bits == s.Bits && last.Signed == s.Signed && last.Overflow == s.Overflow && last.Base == s.Base
            && last.Tab == s.Tab && last.FloatFormat == s.FloatFormat && last.FixedM == s.FixedM && last.FixedN == s.FixedN && last.FixedSigned == s.FixedSigned)
            return;
        _saved = s;
        _services.Prefs.SetModeState("programmer", s);
        _services.Prefs.Save();
    }
}

using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;
using Calcy.UI.Controls;
using Calcy.UI.Services;

namespace Calcy.Modes.Simple;

/// <summary>
/// The Simple screen: a desk calculator (LCD and 4-column keypad) next to its editable tape.
/// All maths lives in <see cref="SimpleCalculator"/>; this view forwards clicks and keys and shows the state.
/// </summary>
public sealed class SimpleView : UserControl, IReceivesPayload, IFocusFirst
{
    private const double BodyWidth = 460;

    private readonly AppServices? _services;
    private readonly Dictionary<CalcKey, KeyButton> _keys = [];
    private readonly Dictionary<TapeLine, TapeCardView> _cards = [];
    private readonly LcdText _display;
    private readonly LcdText _expression;
    private readonly PixelText _memoryFlag;
    private readonly StackPanel _tapeList;
    private readonly ScrollViewer _tapeScroll;
    private readonly Control _tapeEmpty;
    private TapeCardView? _editing;

    /// <summary>Creates the screen. <paramref name="services"/> may be null for previews and tests without an engine.</summary>
    public SimpleView(AppServices? services, SimpleCalculator? calculator = null)
    {
        _services = services;
        Calculator = calculator ?? new SimpleCalculator();
        Focusable = true;
        FocusAdorner = null;

        _memoryFlag = Ui.Pixel("M", 2, "Calcy.LcdInk");
        _memoryFlag.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(_memoryFlag, "memory in use");
        _expression = new LcdText { MaxPixelSize = 2, MinPixelSize = 2, Height = 16, HorizontalAlignment = HorizontalAlignment.Stretch }
            .Token(LcdText.ForegroundProperty, "Calcy.LcdInk");
        AutomationProperties.SetName(_expression, "");
        _display = new LcdText { MaxPixelSize = 7, MinPixelSize = 2, Height = 52, HorizontalAlignment = HorizontalAlignment.Stretch }
            .Token(LcdText.ForegroundProperty, "Calcy.LcdInk");
        AutomationProperties.SetLiveSetting(_display, AutomationLiveSetting.Polite);

        _tapeList = new StackPanel { Spacing = 12, Margin = new Thickness(14, 14, 18, 18) };
        _tapeScroll = new ScrollViewer { Content = _tapeList, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        _tapeEmpty = TapeEmptyState();

        var root = new Grid { Margin = new Thickness(32, 28) };
        root.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        root.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        root.ColumnDefinitions.Add(new ColumnDefinition(32, GridUnitType.Pixel));
        root.ColumnDefinitions.Add(new ColumnDefinition(12, GridUnitType.Star) { MinWidth = 320, MaxWidth = 560 });
        root.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        var body = new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            VerticalAlignment = VerticalAlignment.Top,
            Child = CalculatorBody(),
        };
        Grid.SetColumn(body, 1);
        root.Children.Add(body);
        var tape = TapePanel();
        Grid.SetColumn(tape, 3);
        root.Children.Add(tape);
        Content = root;

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, OnTextInput, RoutingStrategies.Bubble);
        Calculator.Changed += (_, _) => ShowState();
        Calculator.TapeChanged += OnTapeChanged;
        foreach (var line in Calculator.Tape) AddCard(line);
        UpdateTapeEmpty();
        ShowState();
    }

    /// <summary>The calculator behind the screen.</summary>
    public SimpleCalculator Calculator { get; }

    /// <summary>The main LCD readout.</summary>
    public LcdText DisplayText => _display;

    /// <summary>The small expression line above the readout.</summary>
    public LcdText ExpressionText => _expression;

    /// <summary>The "M" memory indicator.</summary>
    public Control MemoryIndicator => _memoryFlag;

    internal IReadOnlyDictionary<CalcKey, KeyButton> Keys => _keys;

    /// <summary>The tape cards, newest first (for tests).</summary>
    internal IReadOnlyList<TapeCardView> Cards => [.. _tapeList.Children.OfType<Card>().Select(c => (TapeCardView)c.Tag!)];

    internal ScrollViewer TapeScroll => _tapeScroll;

    internal PixelButton ExportButton { get; private set; } = null!;

    internal PixelButton ClearTapeButton { get; private set; } = null!;

    /// <inheritdoc />
    public void FocusFirst() => Focus();

    /// <summary>Accepts an <see cref="InputRequest"/> (a number or a simple expression), a number string, or a number.</summary>
    public void Receive(object payload)
    {
        switch (payload)
        {
            case InputRequest request:
                EnterText(request.Text);
                break;
            case string text:
                EnterText(text);
                break;
            case BigRational q:
                Calculator.Enter(q);
                break;
            case double d when double.IsFinite(d):
                Calculator.TryEnter(d.ToString("R", CultureInfo.InvariantCulture));
                break;
        }
    }

    /// <summary>Presses a key as if it was clicked or typed. Typed keys flash on the keypad.</summary>
    public void PressKey(CalcKey key, bool fromKeyboard = false)
    {
        if (fromKeyboard && _keys.TryGetValue(key, out var button)) button.Flash();
        Calculator.Press(key);
    }

    // Layout

    private Control CalculatorBody()
    {
        var lcd = new Border
        {
            BorderThickness = new Thickness(4),
            Padding = new Thickness(12, 12, 12, 14),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12, Children = { _memoryFlag, Column(_expression, 1) } },
                    _display,
                },
            },
        }
            .Token(Border.BackgroundProperty, "Calcy.Lcd")
            .Token(Border.BorderBrushProperty, "Calcy.Line")
            .Shadow("Calcy.LcdShade.Color", 4, inset: true);
        AutomationProperties.SetName(lcd, "display");

        var keypad = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 12, RowSpacing = 12 };
        for (int r = 0; r < 7; r++) keypad.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        int col = 0, row = 0;
        foreach (var key in CalcKeys.Layout)
        {
            var b = new KeyButton(key);
            foreach (var c in KeyClasses(key)) b.Classes.Add(c);
            var k = key;
            b.Click += (_, _) => PressKey(k);
            Grid.SetRow(b, row);
            Grid.SetColumn(b, col);
            int span = key == CalcKey.Digit0 ? 2 : 1;
            Grid.SetColumnSpan(b, span);
            keypad.Children.Add(b);
            _keys[key] = b;
            col += span;
            if (col == 4) { col = 0; row++; }
        }

        var hint = Ui.Text("Type on your keyboard too. Enter is =, Esc is C.", 11, "Calcy.Font.Label", "Calcy.Ink");
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.TextAlignment = TextAlignment.Center;
        hint.TextWrapping = TextWrapping.Wrap;

        var body = new Border
        {
            Width = BodyWidth,
            BorderThickness = new Thickness(4),
            Padding = new Thickness(20),
            // Room for the hard shadow inside the Viewbox, so it is not clipped.
            Margin = new Thickness(0, 0, 10, 10),
            Child = new StackPanel { Spacing = 18, Children = { lcd, keypad, hint } },
        }
            .Token(Border.BackgroundProperty, "Calcy.Key")
            .Token(Border.BorderBrushProperty, "Calcy.Line")
            .Shadow("Calcy.Line.Color", 10);
        AutomationProperties.SetName(body, "calculator");
        return body;
    }

    private static IEnumerable<string> KeyClasses(CalcKey key) => key switch
    {
        CalcKey.MemoryClear or CalcKey.MemoryRecall or CalcKey.MemoryAdd or CalcKey.MemorySubtract => ["mem"],
        CalcKey.Add or CalcKey.Subtract or CalcKey.Multiply or CalcKey.Divide => ["head"],
        CalcKey.Equals => ["primary"],
        CalcKey.Clear => ["err"],
        _ when CalcKeys.IsDigit(key) || key == CalcKey.Point => ["paper"],
        _ => [],
    };

    private Control TapePanel()
    {
        ExportButton = Ui.SmallButton("Export", "export tape", "Copy every line of the tape as text", "paper");
        ExportButton.Click += async (_, _) => await ExportAsync();
        ClearTapeButton = Ui.SmallButton("Clear", "clear tape", "Remove every line from the tape", "paper");
        ClearTapeButton.Click += (_, _) =>
        {
            Calculator.ClearTape();
            Focus();
        };

        var footerText = Ui.Text("Edit an old line and every line below it recalculates.", 13, "Calcy.Font.Body", "Calcy.Muted");
        footerText.TextWrapping = TextWrapping.Wrap;
        var footer = new Border
        {
            BorderThickness = new Thickness(0, 3, 0, 0),
            Padding = new Thickness(14, 10),
            Child = footerText,
        }.Token(Border.BorderBrushProperty, "Calcy.Line");
        DockPanel.SetDock(footer, Dock.Bottom);

        var panel = new PixelPanel
        {
            Header = "Tape",
            HeaderContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { ExportButton, ClearTapeButton } },
            VerticalAlignment = VerticalAlignment.Top,
            MaxHeight = 760,
            Margin = new Thickness(0, 0, 8, 8),
            ClipToBounds = false,
            Content = new DockPanel { Children = { footer, new Grid { Children = { _tapeScroll, _tapeEmpty } } } },
        };
        AutomationProperties.SetName(panel, "tape");
        return panel;
    }

    private static Control TapeEmptyState()
    {
        var title = Ui.Pixel("NO LINES YET", 2);
        var text = Ui.Text("Every calculation lands here when you press =. Use puts a result back on the display.", 13, "Calcy.Font.Body", "Calcy.Muted");
        text.TextWrapping = TextWrapping.Wrap;
        text.TextAlignment = TextAlignment.Center;
        text.MaxWidth = 320;
        return new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(18, 28),
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { title, text },
        };
    }

    private static Control Column(Control c, int column)
    {
        Grid.SetColumn(c, column);
        return c;
    }

    // State

    private void ShowState()
    {
        _display.Text = Calculator.Display;
        _expression.Text = Calculator.Expression;
        _memoryFlag.IsVisible = Calculator.HasMemory;
        AutomationProperties.SetName(_display, Calculator.Display);
    }

    private void OnTapeChanged(object? sender, TapeChangedEventArgs e)
    {
        switch (e.Change)
        {
            case TapeChange.Added when e.Line is not null:
                AddCard(e.Line);
                _tapeScroll.Offset = default;
                PublishAnswer(e.Line);
                break;
            case TapeChange.Removed when e.Line is not null:
                if (_cards.Remove(e.Line, out var removed)) _tapeList.Children.Remove(removed.Card);
                break;
            case TapeChange.Recalculated:
                foreach (var card in _cards.Values.Where(c => !c.IsEditing)) card.Refresh();
                if (Calculator.Tape.Count > 0) PublishAnswer(Calculator.Tape[^1]);
                break;
            case TapeChange.Cleared:
                _editing = null;
                _cards.Clear();
                _tapeList.Children.Clear();
                break;
        }
        UpdateTapeEmpty();
    }

    private void AddCard(TapeLine line)
    {
        var card = new TapeCardView(line, UseLine, CommitEdit, OnEditingChanged);
        _cards[line] = card;
        _tapeList.Children.Insert(0, card.Card);
    }

    private void UpdateTapeEmpty()
    {
        bool empty = Calculator.Tape.Count == 0;
        _tapeEmpty.IsVisible = empty;
        _tapeScroll.IsVisible = !empty;
        ExportButton.IsEnabled = !empty;
        ClearTapeButton.IsEnabled = !empty;
    }

    private void UseLine(TapeLine line)
    {
        Calculator.Use(line);
        Focus();
    }

    private string? CommitEdit(TapeLine line, string text) =>
        Calculator.TryEdit(line, text, out var error) ? null : error;

    private void OnEditingChanged(TapeCardView card, bool editing)
    {
        if (editing)
        {
            if (_editing is not null && _editing != card) _editing.EndEdit();
            _editing = card;
        }
        else if (_editing == card)
        {
            _editing = null;
            Focus();
        }
    }

    /// <summary>Shares the latest result as "ans" with the other modes (MO-002).</summary>
    private void PublishAnswer(TapeLine line)
    {
        if (_services is null || line.Result is not { } r) return;
        // Square roots carry 45 digits; share those as decimals rather than as huge fractions.
        var n = r.Den.GetBitLength() > 64 ? Number.Approx(r.ToDouble()) : Number.Exact(r);
        _services.Session.Env.SetVariable("ans", new NumberExpr(n));
    }

    private void EnterText(string text)
    {
        if (Calculator.TryEnter(text)) return;
        if (_services is not null && TryEvaluate(_services.Session, text) is { } value)
        {
            Calculator.Enter(value);
            return;
        }
        _services?.ShowStatus("Simple mode takes a number: " + text);
    }

    /// <summary>Evaluates a simple expression from another mode ("1/3", "sqrt(2)") to a real number.</summary>
    private static BigRational? TryEvaluate(CalcSession session, string text)
    {
        try
        {
            if (session.EvaluateExpression(text) is NumberExpr { Value: { IsExactRational: true } exact }) return exact.ReQ;
            if (session.EvaluateExpression(text, approximate: true) is NumberExpr { Value: { IsReal: true, IsFinite: true } approx }
                && DeskMath.TryParseNumber(approx.Re.ToString("R", CultureInfo.InvariantCulture), out var v))
                return v;
        }
        catch (Exception ex) when (ex is CalcException or ArithmeticException or ArgumentException or FormatException or InvalidOperationException)
        {
            // Not a number Simple mode can show; the caller reports it.
        }
        return null;
    }

    private async Task ExportAsync()
    {
        var text = Calculator.ExportTape();
        int count = Calculator.Tape.Count;
        Focus();
        if (count == 0 || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        try
        {
            await clipboard.SetTextAsync(text);
            _services?.ShowStatus(count == 1 ? "Tape copied: 1 line" : $"Tape copied: {count} lines");
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.ExternalException)
        {
            _services?.ShowStatus("Could not copy the tape to the clipboard");
        }
    }

    private async Task CopyDisplayAsync()
    {
        if (Calculator.Value is not { } v || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        try
        {
            await clipboard.SetTextAsync(DisplayFormat.Format(v, grouping: false));
            _services?.ShowStatus("Copied " + Calculator.Display);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.ExternalException)
        {
            _services?.ShowStatus("Could not copy to the clipboard");
        }
    }

    private async Task PasteAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        try
        {
            var text = await clipboard.TryGetTextAsync();
            if (string.IsNullOrWhiteSpace(text)) return;
            EnterText(text.Trim());
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.ExternalException)
        {
            _services?.ShowStatus("Could not read the clipboard");
        }
    }

    // Keyboard

    private static bool FromTextBox(RoutedEventArgs e) =>
        e.Source is Visual v && (v is TextBox || v.FindAncestorOfType<TextBox>() is not null);

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (FromTextBox(e)) return;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        CalcKey? key = null;
        if (ctrl && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            switch (e.Key)
            {
                case Key.L: key = CalcKey.MemoryClear; break;
                case Key.R: key = CalcKey.MemoryRecall; break;
                case Key.P: key = CalcKey.MemoryAdd; break;
                case Key.Q: key = CalcKey.MemorySubtract; break;
                case Key.C:
                    e.Handled = true;
                    await CopyDisplayAsync();
                    return;
                case Key.V:
                    e.Handled = true;
                    await PasteAsync();
                    return;
            }
        }
        else if (!ctrl)
        {
            key = e.Key switch
            {
                Key.Enter => CalcKey.Equals,
                Key.Back => CalcKey.Backspace,
                Key.Escape => CalcKey.Clear,
                Key.Delete => CalcKey.ClearEntry,
                Key.F9 => CalcKey.Negate,
                _ => null,
            };
        }
        if (key is { } k)
        {
            PressKey(k, fromKeyboard: true);
            e.Handled = true;
        }
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (FromTextBox(e) || string.IsNullOrEmpty(e.Text)) return;
        bool any = false;
        foreach (var c in e.Text)
        {
            if (CalcKeys.FromChar(c) is not { } key) continue;
            PressKey(key, fromKeyboard: true);
            any = true;
        }
        if (any) e.Handled = true;
    }
}

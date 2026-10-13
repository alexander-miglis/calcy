using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Calcy.UI.Controls;

namespace Calcy.Modes.Programmer;

/// <summary>
/// The Integer tab: the value in every base (click a row to make it the input base), word size and signedness,
/// the C-style expression box, the clickable bit grid and the 8-column keypad.
/// </summary>
internal sealed class IntegerTab : UserControl
{
    private static readonly NumberBase[] Bases = [NumberBase.Hex, NumberBase.Dec, NumberBase.Oct, NumberBase.Bin];
    private static readonly HashSet<string> BitwiseKeys = ["AND", "OR", "XOR", "NOT", "NAND", "NOR", "<<", ">>", "ROL", "ROR"];

    private readonly ProgrammerCalculator _calc;
    private readonly Action<string> _status;
    private readonly TextBlock _statusLine;
    private readonly TokenSlot _statusInk;
    private readonly List<(NumberBase Base, TextBlock Value, TokenSlot Background)> _rows = [];
    private readonly Dictionary<NumberBase, Button> _rowButtons = [];
    private readonly Dictionary<int, PixelToggle> _sizes = [];
    private readonly TextBox _width;
    private readonly PixelToggle _signed;
    private readonly PixelToggle _unsigned;
    private readonly Dictionary<OverflowMode, PixelToggle> _overflow = [];
    private readonly Grid _bitGrid = new() { ColumnSpacing = 4, RowSpacing = 6 };
    private readonly List<BitCell> _cells = [];
    private readonly TextBlock _bitInfo;
    private readonly StackPanel _fieldActions;
    private readonly Dictionary<string, KeyView> _keys = [];
    private int _builtBits = -1;
    private int? _anchor;
    private (int Hi, int Lo)? _field;
    private bool _syncing;
    private bool _pushCaret;
    private int _recall = -1;

    private sealed record KeyView(PixelButton Button, TokenSlot LabelInk, string ColourClass);

    private sealed record BitCell(int Index, FlatButton Button, TextBlock Digit, TokenSlot Background, TokenSlot Ink, TokenSlot Edge);

    /// <summary>Creates the tab on top of the calculator; <paramref name="status"/> shows short messages in the app's status bar.</summary>
    public IntegerTab(ProgrammerCalculator calc, Action<string> status)
    {
        _calc = calc;
        _status = status;

        // ---- header: word size, signedness, overflow ----
        var sizes = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (int bits in IntegerFormat.Presets)
        {
            var t = Ui.Segment(bits.ToString(CultureInfo.InvariantCulture), $"{bits}-bit word", 46);
            int b = bits;
            t.Click += (_, _) => _calc.SetWordSize(b);
            _sizes[bits] = t;
            sizes.Children.Add(t);
        }
        _width = new TextBox { Width = 62, MinHeight = 34, FontSize = 15, Margin = new Thickness(10, 0, 0, 0), PlaceholderText = "bits", HorizontalContentAlignment = HorizontalAlignment.Center, MaxLength = 3 };
        _width.Token(TextBox.FontFamilyProperty, "Font.Math");
        ToolTip.SetTip(_width, "Any word size from 1 to 256 bits (type and press Enter)");
        _width.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { ApplyWidth(); e.Handled = true; }
            else if (e.Key == Key.Escape) { _width.Text = _calc.Format.Bits.ToString(CultureInfo.InvariantCulture); e.Handled = true; }
        };
        _width.LostFocus += (_, _) => ApplyWidth();
        sizes.Children.Add(_width);

        _signed = Ui.Segment("SIGNED", "Two's complement: the top bit is the sign", 84);
        _signed.Click += (_, _) => _calc.SetSigned(true);
        _unsigned = Ui.Segment("UNSIGNED", "All bits are magnitude", 100);
        _unsigned.Click += (_, _) => _calc.SetSigned(false);
        var sign = new StackPanel { Orientation = Orientation.Horizontal, Children = { _signed, _unsigned } };

        var overflow = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (mode, caption, tip) in new[]
        {
            (OverflowMode.Wrap, "WRAP", "Overflow wraps around, like a CPU (0x7FFF + 1 = -32768)"),
            (OverflowMode.Saturate, "SATURATE", "Overflow sticks at the smallest or largest value"),
            (OverflowMode.Flag, "FLAG", "Overflow is reported as an error"),
        })
        {
            var t = Ui.Segment(caption, tip, 60);
            var m = mode;
            t.Click += (_, _) => _calc.SetOverflow(m);
            _overflow[mode] = t;
            overflow.Children.Add(t);
        }

        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var group in new Control[] { sizes, sign, overflow })
        {
            group.Margin = new Thickness(0, 2, 18, 2);
            toolbar.Children.Add(group);
        }
        var header = new Border { BorderThickness = new Thickness(0, 0, 0, 4), Padding = new Thickness(12, 8), Child = toolbar };
        header.Token(Border.BackgroundProperty, "Head").Token(Border.BorderBrushProperty, "Line");

        // ---- expression box and status line ----
        ExpressionBox = Ui.Input(22, double.NaN, "Type C-style: 0xFF & (ans << 3), a XOR b, ~x, 0b1010");
        ExpressionBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        ExpressionBox.MinHeight = 44;
        AutomationName(ExpressionBox, "Expression");
        ExpressionBox.TextChanged += (_, _) =>
        {
            // TextChanged can arrive after our own update; only a different text is a user edit.
            var text = ExpressionBox.Text ?? "";
            if (_syncing || text == _calc.Expression) return;
            _recall = -1;
            _calc.SetExpression(text, ExpressionBox.CaretIndex);
        };
        ExpressionBox.AddHandler(KeyDownEvent, OnExpressionKeyDown, RoutingStrategies.Tunnel);

        _statusLine = Ui.Text("", 14, "Body", "Muted");
        _statusLine.TextTrimming = TextTrimming.CharacterEllipsis;
        _statusLine.Margin = new Thickness(2, 5, 0, 0);
        _statusLine.Height = 19;
        _statusInk = new TokenSlot(_statusLine, TextBlock.ForegroundProperty);
        var exprArea = new StackPanel { Margin = new Thickness(14, 10, 14, 8), Children = { ExpressionBox, _statusLine } };

        // ---- value rows ----
        var rows = new StackPanel();
        foreach (var b in Bases)
        {
            var value = Ui.Text("0", b == NumberBase.Bin ? 18 : 26, "Math", "Ink", FontWeight.SemiBold);
            value.TextWrapping = TextWrapping.Wrap;
            var label = Ui.Pixel(BaseFormatter.Name(b), 2);
            label.HorizontalAlignment = HorizontalAlignment.Left;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("70,*"), ColumnSpacing = 14 };
            grid.Children.Add(label.Cell(0, 0));
            grid.Children.Add(value.Cell(0, 1));
            var row = new FlatButton { Content = grid, Padding = new Thickness(14, 3), MinHeight = 40, BorderThickness = new Thickness(0, 0, 0, 2) };
            row.Token(BorderBrushProperty, "Key");
            var bg = new TokenSlot(row, BackgroundProperty);
            var nb = b;
            row.Click += (_, _) => { _calc.SetInputBase(nb); FocusExpression(); };
            ToolTip.SetTip(row, $"Type in {BaseFormatter.DigitWord(b)} (F{5 + Array.IndexOf(Bases, b)})");
            AutomationName(row, $"{BaseFormatter.Name(b)} value");
            _rows.Add((b, value, bg));
            _rowButtons[b] = row;
            rows.Children.Add(row);
        }

        var valuePanel = Ui.Section(new StackPanel { Children = { header, exprArea, rows } }, new Thickness(0));

        // ---- bits ----
        _bitInfo = Ui.Text("", 13, "Label", "Ink");
        var extract = Ui.SmallButton("EXTRACT", "Card with the value of this field: bits(x, hi, lo)", "ok");
        extract.Click += (_, _) => ApplyField(insert: false);
        var insert = Ui.SmallButton("INSERT", "Replace this field: insert(x, hi, lo, value). Type the value, then Enter", "head");
        insert.Click += (_, _) => ApplyField(insert: true);
        var clearField = Ui.SmallButton("X", "Clear the field selection");
        clearField.Click += (_, _) => { _field = null; Refresh(); };
        _fieldActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(10, 0, 0, 0),
            IsVisible = false,
            Children = { extract, insert, clearField },
        };
        var bitTop = new DockPanel { LastChildFill = false, MinHeight = 30 };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Children = { _bitInfo, _fieldActions } };
        DockPanel.SetDock(right, Dock.Right);
        bitTop.Children.Add(right);
        bitTop.Children.Add(Ui.Pixel("BITS: CLICK TO FLIP, SHIFT+CLICK A FIELD", 1.5));
        var bitsPanel = Ui.Section(new StackPanel { Spacing = 8, Children = { bitTop, _bitGrid } }, new Thickness(14, 10));

        // ---- keypad ----
        var keypad = new Grid { ColumnSpacing = 8, RowSpacing = 7 };
        for (int c = 0; c < 8; c++) keypad.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        var keys = ProgrammerCalculator.KeypadKeys;
        for (int r = 0; r < (keys.Count + 7) / 8; r++) keypad.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            var label = Ui.Pixel(key, 2, ink: null);
            var button = new PixelButton
            {
                Content = label,
                Height = 40,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(4, 0),
                Focusable = false,
                // Disabled keys look like the design (Key colour, muted label) instead of fading out.
                Opacity = 1,
            };
            ToolTip.SetTip(button, KeyTip(key));
            Avalonia.Controls.ToolTip.SetShowOnDisabled(button, true);
            AutomationName(button, KeyTip(key));
            button.Click += (_, _) => PressKey(key);
            _keys[key] = new KeyView(button, new TokenSlot(label, PixelText.ForegroundProperty), KeyClass(key));
            keypad.Children.Add(button.Cell(i / 8, i % 8));
        }

        var stack = new StackPanel { Spacing = 12, Margin = new Thickness(0, 0, 10, 10), Children = { valuePanel, bitsPanel, keypad } };
        Content = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

        // Click carries no modifiers, so remember them from the pointer press (Shift+click selects a field).
        AddHandler(PointerPressedEvent, (_, args) => _lastModifiers = args.KeyModifiers, RoutingStrategies.Tunnel, handledEventsToo: true);
        _calc.Changed += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>The C-style expression input.</summary>
    public TextBox ExpressionBox { get; }

    /// <summary>The keypad button for a key label (for tests and automation).</summary>
    public PixelButton KeyButton(string key) => _keys[key].Button;

    /// <summary>The bit grid cell for a bit index (for tests and automation).</summary>
    public Button BitButton(int index) => _cells.First(c => c.Index == index).Button;

    /// <summary>The value row of a base (for tests and automation).</summary>
    public Button RowButton(NumberBase b) => _rowButtons[b];

    /// <summary>The status line under the expression.</summary>
    public string StatusText => _statusLine.Text ?? "";

    /// <summary>Moves keyboard focus to the expression box.</summary>
    public void FocusExpression()
    {
        ExpressionBox.Focus();
    }

    /// <summary>Inserts text at the caret of the expression (keyboard typing outside the box, card USE buttons).</summary>
    public void InsertText(string text)
    {
        SyncFromBox();
        _pushCaret = true;
        _calc.InsertText(text);
        FocusExpression();
    }

    /// <summary>Replaces the expression (payloads from other modes) and optionally evaluates it.</summary>
    public void SetExpression(string text, bool evaluate)
    {
        _pushCaret = true;
        _calc.SetExpression(text, text.Length);
        if (evaluate) _calc.Evaluate();
        FocusExpression();
    }

    /// <summary>Presses a keypad key (also used for Enter, Escape and Backspace typed outside the box).</summary>
    public void PressKey(string key)
    {
        SyncFromBox();
        _pushCaret = true;
        if (!_calc.Press(key) && key.Length == 1 && BaseFormatter.DigitValue(key[0]) >= 0)
            _status($"{key} is not a {BaseFormatter.DigitWord(_calc.InputBase)} digit; click the HEX row to type hex");
        FocusExpression();
    }

    /// <summary>Escape: clears the expression, then the value.</summary>
    public void Escape()
    {
        SyncFromBox();
        _pushCaret = true;
        _calc.Escape();
        _field = null;
        Refresh();
    }

    private void SyncFromBox()
    {
        var text = ExpressionBox.Text ?? "";
        int caret = ExpressionBox.CaretIndex;
        int s = Math.Min(ExpressionBox.SelectionStart, ExpressionBox.SelectionEnd);
        int e = Math.Max(ExpressionBox.SelectionStart, ExpressionBox.SelectionEnd);
        if (e > s && e <= text.Length)
        {
            text = text.Remove(s, e - s);
            caret = s;
        }
        _calc.SetExpression(text, caret);
    }

    private void OnExpressionKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                SyncFromBox();
                _pushCaret = true;
                _recall = -1;
                _calc.Evaluate();
                e.Handled = true;
                break;
            case Key.Escape:
                Escape();
                e.Handled = true;
                break;
            case Key.Up when _calc.Cards.Count > 0:
                Recall(_recall < 0 ? _calc.Cards.Count - 1 : Math.Max(0, _recall - 1));
                e.Handled = true;
                break;
            case Key.Down when _recall >= 0:
                Recall(_recall + 1);
                e.Handled = true;
                break;
        }
    }

    /// <summary>Puts the expression of an earlier card in the box (Up and Down walk through them; past the newest clears).</summary>
    private void Recall(int index)
    {
        var text = index < _calc.Cards.Count ? _calc.Cards[index].Expression : "";
        _pushCaret = true;
        _calc.SetExpression(text, text.Length);
        _recall = index < _calc.Cards.Count ? index : -1;
    }

    private void ApplyWidth()
    {
        var text = (_width.Text ?? "").Trim();
        if (text.Length == 0 || text == _calc.Format.Bits.ToString(CultureInfo.InvariantCulture)) { Refresh(); return; }
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int bits) && bits is >= 1 and <= IntegerFormat.MaxBits)
        {
            _calc.SetWordSize(bits);
            return;
        }
        _status($"Word size must be 1 to {IntegerFormat.MaxBits} bits");
        _width.Text = _calc.Format.Bits.ToString(CultureInfo.InvariantCulture);
    }

    // ---- refresh ----------------------------------------------------------------------------------------------

    /// <summary>Brings every control in line with the calculator state.</summary>
    public void Refresh()
    {
        var f = _calc.Format;
        foreach (var (bits, t) in _sizes) t.IsChecked = bits == f.Bits;
        if (!_width.IsFocused) _width.Text = f.Bits.ToString(CultureInfo.InvariantCulture);
        _signed.IsChecked = f.Signed;
        _unsigned.IsChecked = !f.Signed;
        foreach (var (mode, t) in _overflow) t.IsChecked = mode == f.Overflow;

        if ((ExpressionBox.Text ?? "") != _calc.Expression)
        {
            _syncing = true;
            ExpressionBox.Text = _calc.Expression;
            _syncing = false;
            _pushCaret = true;
        }
        if (_pushCaret)
        {
            ExpressionBox.CaretIndex = _calc.Caret;
            ExpressionBox.SelectionStart = ExpressionBox.SelectionEnd = _calc.Caret;
            _pushCaret = false;
        }

        var preview = _calc.GetPreview();
        UpdateStatus(preview);
        foreach (var (b, value, bg) in _rows)
        {
            var text = BaseFormatter.Format(f, preview.Value, b);
            if (b == NumberBase.Bin && f.Bits > 64) text = SplitBinary(text);
            value.Text = text;
            value.FontSize = RowFontSize(b, text);
            bg.Set(b == _calc.InputBase ? "Sel" : "Paper");
        }
        UpdateBits(f, preview.Value);
        foreach (var (key, k) in _keys)
        {
            bool enabled = _calc.IsKeyEnabled(key);
            k.Button.IsEnabled = enabled;
            k.Button.Classes.Set(k.ColourClass, enabled);
            k.Button.Classes.Set("flat", !enabled);
            k.LabelInk.Set(enabled ? "Ink" : "Muted");
        }
    }

    /// <summary>Wide binary values go on lines of 64 bits so the groups line up.</summary>
    private static string SplitBinary(string grouped)
    {
        var groups = grouped.Split(' ');
        var lines = new List<string>();
        for (int i = 0; i < groups.Length; i += 16) lines.Add(string.Join(' ', groups.Skip(i).Take(16)));
        return string.Join('\n', lines);
    }

    /// <summary>The design sizes (26, binary 18), smaller for long values so a 64-bit word stays on one line.</summary>
    private static double RowFontSize(NumberBase b, string text)
    {
        int longest = text.Split('\n').Max(l => l.Length);
        double max = b == NumberBase.Bin ? 18 : 26;
        return Math.Clamp(Math.Floor(760 / (longest * 0.6)), 14, max);
    }

    private void UpdateStatus(ProgrammerPreview p)
    {
        string text;
        string ink;
        if (p.Error is { } err && !string.IsNullOrWhiteSpace(_calc.Expression))
        {
            text = err;
            ink = "Err";
        }
        else if (p.IsLive)
        {
            var notes = p.Notes.Count > 0 ? $" ({string.Join(", ", p.Notes)})" : "";
            text = $"= {BaseFormatter.FormatLiteral(_calc.Format, p.Value, _calc.InputBase)}{notes}. Enter adds a card.";
            ink = "Ink";
        }
        else
        {
            text = $"Input base {BaseFormatter.Name(_calc.InputBase)}. Type 0x, 0b or 0o to switch. Enter adds a card.";
            ink = "Muted";
        }
        _statusLine.Text = text;
        _statusInk.Set(ink);
        ToolTip.SetTip(_statusLine, text);
    }

    // ---- bit grid ---------------------------------------------------------------------------------------------

    private void BuildBits(int bits)
    {
        _bitGrid.Children.Clear();
        _bitGrid.ColumnDefinitions.Clear();
        _bitGrid.RowDefinitions.Clear();
        _cells.Clear();
        for (int c = 0; c < 16; c++) _bitGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        int rows = (bits + 15) / 16;
        for (int r = 0; r < rows; r++) _bitGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        bool compact = bits > 64;
        for (int i = bits - 1; i >= 0; i--)
        {
            int row = rows - 1 - i / 16;
            int col = 15 - i % 16;
            var digit = new TextBlock { Text = "0", FontSize = compact ? 14 : 16, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center };
            digit.Token(TextBlock.FontFamilyProperty, "Font.Math");
            var index = new TextBlock { Text = i.ToString(CultureInfo.InvariantCulture), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center };
            index.Token(TextBlock.FontFamilyProperty, "Font.Math");
            var cell = new FlatButton
            {
                Content = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center, Children = { digit, index } },
                Height = compact ? 36 : 40,
                BorderThickness = new Thickness(2),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(col > 0 && col % 4 == 0 ? 6 : 0, 0, 0, 0),
            };
            var bc = new BitCell(i, cell, digit, new TokenSlot(cell, BackgroundProperty), new TokenSlot(cell, ForegroundProperty), new TokenSlot(cell, BorderBrushProperty));
            int bitIndex = i;
            cell.Click += (_, e) => OnBitClick(bitIndex);
            _cells.Add(bc);
            _bitGrid.Children.Add(cell.Cell(row, col));
        }
        _builtBits = bits;
        _anchor = null;
        _field = null;
    }

    private void UpdateBits(IntegerFormat f, BigInteger value)
    {
        if (_builtBits != f.Bits) BuildBits(f.Bits);
        if (_field is { } fl && (fl.Hi >= f.Bits || fl.Lo >= f.Bits)) _field = null;
        foreach (var c in _cells)
        {
            bool on = IntegerOps.GetBit(f, value, c.Index);
            bool selected = _field is { } sel && c.Index <= sel.Hi && c.Index >= sel.Lo;
            c.Digit.Text = on ? "1" : "0";
            c.Background.Set(on ? "Sel" : "Off");
            c.Ink.Set(on ? "Ink" : "OffInk");
            c.Edge.Set(selected ? "Primary" : "Line");
            c.Button.BorderThickness = new Thickness(selected ? 3 : 2);
            ToolTip.SetTip(c.Button, $"Bit {c.Index}: {(on ? 1 : 0)} (value 2^{c.Index}). Click to flip, Shift+click to select a field");
        }
        _fieldActions.IsVisible = _field is not null;
        if (_field is { } field)
        {
            var pattern = f.ToPattern(IntegerOps.ExtractField(f, value, field.Hi, field.Lo));
            _bitInfo.Text = $"bits {field.Hi}..{field.Lo} = 0x{BaseFormatter.ToDigits(pattern, NumberBase.Hex)} = {pattern}";
        }
        else
        {
            _bitInfo.Text = $"set bits: {IntegerOps.PopCount(f, value)} of {f.Bits}";
        }
    }

    private void OnBitClick(int index)
    {
        bool shift = _lastModifiers.HasFlag(KeyModifiers.Shift);
        if (shift && _anchor is { } a)
        {
            _field = (Math.Max(a, index), Math.Min(a, index));
            Refresh();
            return;
        }
        _anchor = index;
        _field = null;
        SyncFromBox();
        var preview = _calc.GetPreview();
        if (preview.IsLive)
        {
            // Editing the number being typed: the box gets the new value in the input base.
            var flipped = IntegerOps.FlipBit(_calc.Format, preview.Value, index);
            var text = _calc.InputBase == NumberBase.Dec
                ? BaseFormatter.Format(_calc.Format, flipped, NumberBase.Dec)
                : BaseFormatter.ToDigits(_calc.Format.ToPattern(flipped), _calc.InputBase);
            _pushCaret = true;
            _calc.SetExpression(text, text.Length);
        }
        else
        {
            _calc.FlipBit(index);
        }
    }

    private void ApplyField(bool insert)
    {
        if (_field is not { } f) return;
        SyncFromBox();
        var operand = _calc.GetPreview().IsLive ? $"({_calc.Expression.Trim()})" : "ans";
        _pushCaret = true;
        if (insert)
        {
            var text = $"insert({operand}, {Literal(f.Hi)}, {Literal(f.Lo)}, )";
            _calc.SetExpression(text, text.Length - 1);
            _status("Type the new field value, then press Enter");
        }
        else
        {
            _calc.Apply($"bits({operand}, {Literal(f.Hi)}, {Literal(f.Lo)})");
        }
        _field = null;
        Refresh();
        FocusExpression();
    }

    /// <summary>A bit index written so it reads correctly in the current input base (digits of that base).</summary>
    private string Literal(int n) => _calc.InputBase == NumberBase.Dec
        ? n.ToString(CultureInfo.InvariantCulture)
        : BaseFormatter.ToDigits(n, _calc.InputBase);

    private KeyModifiers _lastModifiers;

    // ---- keypad helpers ---------------------------------------------------------------------------------------

    private static string KeyClass(string key)
    {
        if (key == "=") return "primary";
        if (key == "CLR") return "paper";
        if (key.Length == 1 && BaseFormatter.DigitValue(key[0]) >= 0) return "paper";
        return BitwiseKeys.Contains(key) ? "ok" : "head";
    }

    private static string KeyTip(string key) => key switch
    {
        "AND" => "AND: bits set in both",
        "OR" => "OR: bits set in either",
        "XOR" => "XOR: bits set in exactly one",
        "NOT" => "NOT: flip every bit (acts on the value when the input is empty)",
        "NAND" => "NAND: NOT (a AND b)",
        "NOR" => "NOR: NOT (a OR b)",
        "<<" => "Shift left",
        ">>" => "Shift right (arithmetic when signed, logical when unsigned)",
        "ROL" => "Rotate left (by 1 bit when the input is empty)",
        "ROR" => "Rotate right (by 1 bit when the input is empty)",
        "MOD" => "Remainder, like C's %",
        "÷" => "Divide (truncates toward zero)",
        "×" => "Multiply",
        "−" => "Subtract",
        "+" => "Add",
        "±" => "Change sign of the number (negates the value when the input is empty)",
        "." => "Integers only: use the Float tab for fractions",
        "⌫" => "Delete (Backspace)",
        "=" => "Evaluate and add a card (Enter)",
        "CLR" => "Clear the input and the value (Escape clears the input)",
        "(" or ")" => "Parenthesis",
        _ => $"Digit {key}",
    };

    private static void AutomationName(Control c, string name) => Avalonia.Automation.AutomationProperties.SetName(c, name);
}

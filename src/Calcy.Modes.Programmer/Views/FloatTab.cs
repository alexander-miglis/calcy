using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Calcy.UI.Controls;

namespace Calcy.Modes.Programmer;

/// <summary>
/// The Float tab: an IEEE 754 inspector for half, single, double and quad precision (clickable bits, sign, exponent,
/// mantissa, exact stored value, shortest decimal, ULP, next and previous value, NaN payloads) and a Qm.n fixed-point converter.
/// </summary>
internal sealed class FloatTab : UserControl
{
    private const int MaxExactChars = 420;

    private readonly Action<string> _status;
    private readonly TextBox _input;
    private readonly Dictionary<IeeeFormat, PixelToggle> _formats = [];
    private readonly TextBlock _describe;
    private readonly TokenSlot _describeInk;
    private readonly StackPanel _bitRows = new() { Spacing = 6 };
    private readonly List<(int Index, TextBlock Digit, TokenSlot Background)> _cells = [];
    private readonly TextBlock _sign;
    private readonly TextBlock _exponent;
    private readonly TextBlock _mantissa;
    private readonly TextBlock _mantissaHex;
    private readonly TextBlock _exact;
    private readonly TextBlock _shortest;
    private readonly TextBlock _hex;
    private readonly TextBlock _ulp;
    private readonly TextBlock _class;
    private readonly TextBlock _prevText;
    private readonly TextBlock _nextText;
    private readonly PixelButton _prev;
    private readonly PixelButton _next;
    private IeeeFormat _format = IeeeFormat.Double;
    private IeeeFloat _value;
    private IeeeFormat? _builtFor;
    private bool _syncing;

    private readonly TextBox _fxInput;
    private readonly TextBox _fxM;
    private readonly TextBox _fxN;
    private readonly PixelToggle _fxSigned;
    private readonly PixelToggle _fxUnsigned;
    private readonly TextBlock _fxError;
    private readonly TokenSlot _fxErrorInk;
    private readonly Grid _fxResults = new() { ColumnDefinitions = new ColumnDefinitions("150,*"), RowSpacing = 6, ColumnSpacing = 12 };
    private bool _fxIsSigned = true;

    /// <summary>Raised when the format or fixed-point settings change (for saving preferences).</summary>
    public event EventHandler? SettingsChanged;

    /// <summary>Creates the tab; <paramref name="status"/> shows short messages in the status bar.</summary>
    public FloatTab(Action<string> status, string initialText = "0.1")
    {
        _status = status;

        // ---- inspector ----
        _input = Ui.Input(22, 260, "0.1, 1/3, inf, nan, 0x3FB9...");
        Avalonia.Automation.AutomationProperties.SetName(_input, "Floating point number");
        _input.Text = initialText;
        _input.TextChanged += (_, _) => { if (!_syncing) ParseInput(); };

        var formatRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        foreach (var f in IeeeFormat.All)
        {
            var t = Ui.Segment(f.Name.ToUpperInvariant(), $"{f.Describe()}: {f.ExponentBits} exponent and {f.FractionBits} fraction bits", 70);
            var format = f;
            t.Click += (_, _) => SetFormat(format);
            _formats[f] = t;
            formatRow.Children.Add(t);
        }
        var top = new WrapPanel { Orientation = Orientation.Horizontal, Children = { Ui.Caption("NUMBER"), _input, formatRow } };
        _describe = Ui.Text("", 15, "Body", "Muted");
        _describeInk = new TokenSlot(_describe, TextBlock.ForegroundProperty);

        _sign = Ui.Text("", 18);
        _exponent = Ui.Text("", 18);
        _mantissa = Ui.Text("", 18);
        _mantissaHex = Ui.Text("", 13, "Math", "Ink");
        _mantissaHex.TextWrapping = TextWrapping.Wrap;
        var boxes = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 12 };
        boxes.Children.Add(FieldBox("SIGN", "Mem", _sign).Cell(0, 0));
        boxes.Children.Add(FieldBox("EXPONENT", "Sel", _exponent).Cell(0, 1));
        boxes.Children.Add(FieldBox("MANTISSA", "Head", _mantissa, _mantissaHex).Cell(0, 2));

        _exact = Value(wrap: true);
        _shortest = Value();
        _hex = Value();
        _ulp = Value(wrap: true);
        _class = Value(wrap: true);
        var copy = Ui.SmallButton("COPY", "Copy the exact stored value");
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
            {
                await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(cb, _value.ExactDecimal());
                _status("Exact value copied");
            }
        };
        var info = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*,Auto"), RowSpacing = 8, ColumnSpacing = 12 };
        AddInfo(info, 0, "STORED EXACTLY", _exact, copy);
        AddInfo(info, 1, "SHORTEST", _shortest);
        AddInfo(info, 2, "HEX", _hex);
        AddInfo(info, 3, "ULP", _ulp);
        AddInfo(info, 4, "CLASS", _class);

        var prev = new PixelButton { Content = Ui.Pixel("← PREV", 2), Classes = { "head" }, Focusable = false };
        ToolTip.SetTip(prev, "The next smaller value (one step toward minus infinity)");
        prev.Click += (_, _) => SetValue(_value.NextDown());
        var next = new PixelButton { Content = Ui.Pixel("NEXT →", 2), Classes = { "head" }, Focusable = false };
        ToolTip.SetTip(next, "The next larger value (one step toward plus infinity)");
        next.Click += (_, _) => SetValue(_value.NextUp());
        _prevText = Ui.Text("", 15, "Math", "Muted");
        _prevText.Margin = new Thickness(12, 0);
        _nextText = Ui.Text("", 15, "Math", "Muted");
        _nextText.Margin = new Thickness(12, 0);
        _prev = prev;
        _next = next;
        prev.HorizontalAlignment = next.HorizontalAlignment = HorizontalAlignment.Stretch;
        var neighbours = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), RowSpacing = 8 };
        neighbours.Children.Add(prev.Cell(0, 0));
        neighbours.Children.Add(_prevText.Cell(0, 1));
        neighbours.Children.Add(next.Cell(1, 0));
        neighbours.Children.Add(_nextText.Cell(1, 1));

        var inspector = Ui.Section(new StackPanel
        {
            Spacing = 14,
            Children = { top, _describe, _bitRows, boxes, info, neighbours },
        }, new Thickness(16));

        // ---- fixed point ----
        _fxInput = Ui.Input(18, 200, "0.1, -1/3, 0x0CCD");
        _fxInput.Text = "0.1";
        _fxM = Ui.Input(18, 58, "m");
        _fxM.Text = "0";
        _fxN = Ui.Input(18, 58, "n");
        _fxN.Text = "15";
        foreach (var box in new[] { _fxInput, _fxM, _fxN }) box.TextChanged += (_, _) => UpdateFixed();
        _fxM.HorizontalContentAlignment = _fxN.HorizontalContentAlignment = HorizontalAlignment.Center;
        _fxSigned = Ui.Segment("SIGNED", "Qm.n: a sign bit, m integer bits and n fraction bits", 84);
        _fxUnsigned = Ui.Segment("UNSIGNED", "UQm.n: m integer bits and n fraction bits, no sign", 100);
        _fxSigned.Click += (_, _) => { _fxIsSigned = true; UpdateFixed(); SettingsChanged?.Invoke(this, EventArgs.Empty); };
        _fxUnsigned.Click += (_, _) => { _fxIsSigned = false; UpdateFixed(); SettingsChanged?.Invoke(this, EventArgs.Empty); };
        var q = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 16, 0),
            Spacing = 6,
            Children = { Ui.Pixel("Q", 2), _fxM, Ui.Pixel(".", 2), _fxN },
        };
        var fxTop = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { Ui.Caption("VALUE"), _fxInput, q, new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _fxSigned, _fxUnsigned } } },
        };
        _fxError = Ui.Text("", 14, "Body", "Err");
        _fxErrorInk = new TokenSlot(_fxError, TextBlock.ForegroundProperty);
        var fixedPanel = new PixelPanel
        {
            Header = "Fixed point Qm.n",
            Content = new StackPanel { Margin = new Thickness(16), Spacing = 12, Children = { fxTop, _fxResults, _fxError } },
        };
        fixedPanel.Token(PixelPanel.HeaderBrushProperty, "Mem");

        var stack = new StackPanel { Spacing = 16, Margin = new Thickness(0, 0, 10, 10), Children = { inspector, fixedPanel } };
        Content = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

        ParseInput();
        UpdateFixed();
    }

    /// <summary>The number input (focused when the tab is shown).</summary>
    public TextBox Input => _input;

    /// <summary>The selected IEEE format.</summary>
    public IeeeFormat Format => _format;

    /// <summary>Fixed-point settings (m, n, signed) for saving.</summary>
    public (int M, int N, bool Signed) FixedSettings =>
        (int.TryParse(_fxM.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var m) ? m : 0,
         int.TryParse(_fxN.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 15,
         _fxIsSigned);

    /// <summary>Restores saved settings.</summary>
    public void Restore(string? formatName, int m, int n, bool signed)
    {
        var f = IeeeFormat.All.FirstOrDefault(x => x.Name == formatName) ?? IeeeFormat.Double;
        _fxM.Text = m.ToString(CultureInfo.InvariantCulture);
        _fxN.Text = n.ToString(CultureInfo.InvariantCulture);
        _fxIsSigned = signed;
        SetFormat(f, notify: false);
        UpdateFixed();
    }

    /// <summary>Shows a number (from another mode or a test).</summary>
    public void SetText(string text)
    {
        _input.Text = text;
        ParseInput();
    }

    /// <summary>Selects a format and converts the current value to it.</summary>
    public void SetFormat(IeeeFormat format, bool notify = true)
    {
        var text = (_input.Text ?? "").Trim();
        bool raw = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && !text.Contains('p', StringComparison.OrdinalIgnoreCase)
                   || text.StartsWith("0b", StringComparison.OrdinalIgnoreCase)
                   || text.StartsWith("-0x", StringComparison.OrdinalIgnoreCase);
        if (raw && format != _format)
        {
            _syncing = true;
            _input.Text = _value.IsNaN ? (_value.Sign ? "-nan" : "nan") : _value.ShortestDecimal();
            _syncing = false;
        }
        _format = format;
        ParseInput();
        if (notify) SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetValue(IeeeFloat v)
    {
        _value = v;
        _syncing = true;
        _input.Text = v.IsNaN ? "0x" + v.Hex(group: false) : v.ShortestDecimal();
        _input.CaretIndex = _input.Text.Length;
        _syncing = false;
        Show(null);
    }

    private void ParseInput()
    {
        string? error = null;
        try
        {
            _value = IeeeFloat.Parse(_input.Text ?? "", _format);
        }
        catch (ProgrammerException ex)
        {
            error = ex.Message;
            if (_value.Format != _format) _value = IeeeFloat.Zero(_format);
        }
        Show(error);
    }

    private void Show(string? error)
    {
        var v = _value;
        var f = v.Format;
        foreach (var (format, t) in _formats) t.IsChecked = format == _format;
        if (error is null)
        {
            _describe.Text = $"{f.Describe()}: {v.ClassName}";
            _describeInk.Set("Muted");
        }
        else
        {
            _describe.Text = error;
            _describeInk.Set("Err");
        }

        if (_builtFor != f) BuildBits(f);
        var binary = v.Binary();
        foreach (var (index, digit, bg) in _cells)
        {
            digit.Text = binary[f.TotalBits - 1 - index].ToString();
            bg.Set(index == f.TotalBits - 1 ? "Mem" : index >= f.FractionBits ? "Sel" : "Head");
        }

        _sign.Text = v.Sign ? "1 (negative)" : "0 (positive)";
        _exponent.Text = v.Class switch
        {
            FloatClass.Normal => $"{v.BiasedExponent} - {f.Bias} = {v.UnbiasedExponent}",
            FloatClass.Zero or FloatClass.Subnormal => $"0 (2^{f.MinExponent}, no hidden 1)",
            _ => $"{v.BiasedExponent} (all ones)",
        };
        var sig = v.BinarySignificand();
        _mantissa.Text = v.Class switch
        {
            FloatClass.QuietNaN => "quiet NaN",
            FloatClass.SignalingNaN => "signalling NaN",
            FloatClass.Infinity => "0 (infinity)",
            _ => sig.Length > 16 ? sig[..15] + "…" : sig,
        };
        ToolTip.SetTip(_mantissa, v.IsFinite ? sig : null);
        var fractionHex = BaseFormatter.Group(BaseFormatter.ToDigits(v.Fraction, NumberBase.Hex).PadLeft((f.FractionBits + 3) / 4, '0'), 4);
        _mantissaHex.Text = v.IsNaN ? $"payload 0x{BaseFormatter.ToDigits(v.NaNPayload, NumberBase.Hex)}" : $"fraction {fractionHex}";

        var exact = v.ExactDecimal();
        _exact.Text = exact.Length > MaxExactChars ? exact[..MaxExactChars] + $"… ({exact.Length} characters; COPY copies all)" : exact;
        _shortest.Text = v.ShortestDecimal();
        _hex.Text = "0x" + v.Hex();
        _ulp.Text = v.UlpExponent is int k ? $"2^{k} = {UlpText(v, k)}" : "none (not a finite number)";
        _class.Text = v.Class switch
        {
            FloatClass.QuietNaN or FloatClass.SignalingNaN => $"{v.ClassName}, payload 0x{BaseFormatter.ToDigits(v.NaNPayload, NumberBase.Hex)}",
            FloatClass.Subnormal => "subnormal: below the normal range, precision is lost",
            _ => v.ClassName,
        };
        _prev.IsEnabled = _next.IsEnabled = !v.IsNaN;
        _prevText.Text = v.IsNaN ? "NaN has no neighbours" : v.NextDown().ShortestDecimal();
        _nextText.Text = v.IsNaN ? "" : v.NextUp().ShortestDecimal();
    }

    /// <summary>The ULP as an exact decimal, or in short scientific form when the exact digits are long.</summary>
    private static string UlpText(IeeeFloat v, int k)
    {
        var exact = v.Ulp() ?? "";
        if (exact.Length <= 60) return exact;
        // Every ULP of these formats is a power of two that quad precision holds exactly.
        return k >= 0
            ? IeeeFloat.FromRational(IeeeFormat.Quad, false, System.Numerics.BigInteger.One << k, System.Numerics.BigInteger.One).ShortestDecimal()
            : IeeeFloat.FromRational(IeeeFormat.Quad, false, System.Numerics.BigInteger.One, System.Numerics.BigInteger.One << -k).ShortestDecimal();
    }

    private void BuildBits(IeeeFormat f)
    {
        _bitRows.Children.Clear();
        _cells.Clear();
        int perRow = Math.Min(32, f.TotalBits);
        for (int top = f.TotalBits - 1; top >= 0; top -= perRow)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var label = Ui.Text(top.ToString(CultureInfo.InvariantCulture), 11, "Math", "Muted");
            label.Width = 30;
            label.TextAlignment = TextAlignment.Right;
            label.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(label);
            for (int col = 0; col < perRow && top - col >= 0; col++)
            {
                int index = top - col;
                var digit = new TextBlock { Text = "0", FontSize = 15, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center };
                digit.Token(TextBlock.FontFamilyProperty, "Font.Math");
                var cell = new FlatButton
                {
                    Content = digit,
                    Width = 22,
                    Height = 30,
                    BorderThickness = new Thickness(2),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(col == 0 ? 0 : col % 4 == 0 ? 8 : 3, 0, 0, 0),
                    Focusable = false,
                };
                cell.Token(BorderBrushProperty, "Line").Token(ForegroundProperty, "Ink");
                string field = index == f.TotalBits - 1 ? "sign" : index >= f.FractionBits ? "exponent" : "fraction";
                ToolTip.SetTip(cell, $"Bit {index} ({field}). Click to flip");
                cell.Click += (_, _) => SetValue(_value.FlipBit(index));
                _cells.Add((index, digit, new TokenSlot(cell, BackgroundProperty)));
                row.Children.Add(cell);
            }
            _bitRows.Children.Add(row);
        }
        _builtFor = f;
    }

    private void UpdateFixed()
    {
        foreach (var t in new[] { _fxSigned, _fxUnsigned }) t.IsChecked = (t == _fxSigned) == _fxIsSigned;
        _fxResults.Children.Clear();
        _fxResults.RowDefinitions.Clear();
        try
        {
            if (!int.TryParse(_fxM.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int m) || !int.TryParse(_fxN.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int n))
                throw new ProgrammerException("m and n must be whole numbers of 0 or more");
            var format = new FixedPointFormat(m, n, _fxIsSigned);
            var v = format.Parse(_fxInput.Text ?? "");
            int row = 0;
            void Add(string label, string value)
            {
                _fxResults.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var l = Ui.Pixel(label, 1.5);
                l.HorizontalAlignment = HorizontalAlignment.Left;
                var t = Value(wrap: true);
                t.Text = value;
                _fxResults.Children.Add(l.Cell(row, 0));
                _fxResults.Children.Add(t.Cell(row, 1));
                row++;
            }
            Add("FORMAT", $"{format.Name}: {format.TotalBits} bits ({(format.Signed ? "1 sign, " : "")}{m} integer, {n} fraction)");
            Add("RAW", $"{v.RawHex} = {v.Raw}");
            Add("BINARY", v.RawBinary);
            Add("STORED", v.ExactDecimal() + (v.Saturated ? "  (saturated: outside the range)" : ""));
            Add("ERROR", v.ErrorText);
            Add("RANGE", $"{format.MinText} to {format.MaxText}, step {format.ResolutionText}");
            _fxError.Text = "";
        }
        catch (ProgrammerException ex)
        {
            _fxError.Text = ex.Message;
            _fxErrorInk.Set("Err");
        }
    }

    private static Border FieldBox(string caption, string token, params Control[] values)
    {
        var label = Ui.Pixel(caption, 1.5);
        label.HorizontalAlignment = HorizontalAlignment.Left;
        var stack = new StackPanel { Spacing = 6, Children = { label } };
        foreach (var v in values)
        {
            if (v is TextBlock tb) tb.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(v);
        }
        var b = new Border { BorderThickness = new Thickness(3), Padding = new Thickness(10), Child = stack };
        b.Token(Border.BackgroundProperty, token).Token(Border.BorderBrushProperty, "Line");
        return b;
    }

    private static TextBlock Value(bool wrap = false)
    {
        var t = Ui.Text("", 15);
        t.HorizontalAlignment = HorizontalAlignment.Left;
        t.VerticalAlignment = VerticalAlignment.Top;
        if (wrap) t.TextWrapping = TextWrapping.Wrap;
        return t;
    }

    private static void AddInfo(Grid grid, int row, string label, TextBlock value, Control? extra = null)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var l = Ui.Pixel(label, 1.5);
        l.HorizontalAlignment = HorizontalAlignment.Left;
        l.VerticalAlignment = VerticalAlignment.Top;
        l.Margin = new Thickness(0, 4, 0, 0);
        grid.Children.Add(l.Cell(row, 0));
        grid.Children.Add(value.Cell(row, 1));
        if (extra is not null)
        {
            extra.VerticalAlignment = VerticalAlignment.Top;
            grid.Children.Add(extra.Cell(row, 2));
        }
    }
}

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Calcy.UI.Controls;

namespace Calcy.Modes.Programmer;

/// <summary>
/// The Text tab: characters to code points with their UTF-8 and UTF-16 bytes, checksums of the text,
/// hex bytes back to text, and a clickable ASCII table.
/// </summary>
internal sealed class TextTab : UserControl
{
    private const int MaxCharCards = 16;

    private readonly TextBox _text;
    private readonly TextBlock _summary;
    private readonly UniformGrid _chars = new() { Columns = 4 };
    private readonly TextBlock _more;
    private readonly TextBlock _utf8;
    private readonly TextBlock _utf16be;
    private readonly TextBlock _utf16le;
    private readonly TextBlock _crc;
    private readonly TextBlock _adler;
    private readonly TextBox _bytes;
    private readonly Dictionary<TextEncodingKind, PixelToggle> _encodings = [];
    private readonly TextBlock _decoded;
    private readonly TokenSlot _decodedInk;
    private readonly PixelButton _use;
    private readonly List<(int Code, TokenSlot Background)> _asciiCells = [];
    private TextEncodingKind _decodeKind = TextEncodingKind.Utf8;
    private string? _decodedText;

    /// <summary>Creates the tab with an initial text.</summary>
    public TextTab(string initialText = "Calcy €")
    {
        // ---- text to code points and bytes ----
        _text = Ui.Input(22, 380, "Type any text");
        Avalonia.Automation.AutomationProperties.SetName(_text, "Text");
        _text.Text = initialText;
        _text.TextChanged += (_, _) => UpdateText();
        _summary = Ui.Text("", 15, "Body", "Muted");
        _summary.Margin = new Thickness(16, 0, 0, 0);
        var top = new WrapPanel { Orientation = Orientation.Horizontal, Children = { Ui.Caption("TEXT"), _text, _summary } };
        _more = Ui.Text("", 14, "Body", "Muted");

        _utf8 = Value();
        _utf16be = Value();
        _utf16le = Value();
        _crc = Value();
        _adler = Value();
        var rows = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*"), RowSpacing = 8, ColumnSpacing = 12 };
        AddRow(rows, 0, "UTF-8", _utf8);
        AddRow(rows, 1, "UTF-16 BE", _utf16be);
        AddRow(rows, 2, "UTF-16 LE", _utf16le);
        AddRow(rows, 3, "CRC-32", _crc);
        AddRow(rows, 4, "ADLER-32", _adler);

        var textPanel = Ui.Section(new StackPanel { Spacing = 14, Children = { top, _chars, _more, rows } }, new Thickness(16));

        // ---- bytes to text ----
        _bytes = Ui.Input(18, 320, "E2 82 AC or 0xE2,0x82,0xAC");
        Avalonia.Automation.AutomationProperties.SetName(_bytes, "Hex bytes");
        _bytes.Text = "E2 82 AC 20 31 30";
        _bytes.TextChanged += (_, _) => UpdateDecode();
        var kinds = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0) };
        foreach (var k in new[] { TextEncodingKind.Utf8, TextEncodingKind.Utf16BigEndian, TextEncodingKind.Utf16LittleEndian })
        {
            var t = Ui.Segment(TextCodec.Name(k), $"Read the bytes as {TextCodec.Name(k)}", 70);
            var kind = k;
            t.Click += (_, _) => { _decodeKind = kind; UpdateDecode(); };
            _encodings[k] = t;
            kinds.Children.Add(t);
        }
        _use = Ui.SmallButton("USE AS TEXT", "Put the decoded text in the text box above", "ok");
        _use.Click += (_, _) => { if (_decodedText is not null) _text.Text = _decodedText; };
        var decodeTop = new WrapPanel { Orientation = Orientation.Horizontal, Children = { Ui.Caption("BYTES"), _bytes, kinds, _use } };
        _decoded = Ui.Text("", 20);
        _decoded.TextWrapping = TextWrapping.Wrap;
        _decodedInk = new TokenSlot(_decoded, TextBlock.ForegroundProperty);
        var decodePanel = new PixelPanel
        {
            Header = "Bytes to text",
            Content = new StackPanel { Margin = new Thickness(16), Spacing = 12, Children = { decodeTop, _decoded } },
        };
        decodePanel.Token(PixelPanel.HeaderBrushProperty, "Sel");

        // ---- ASCII table ----
        var asciiPanel = new PixelPanel { Header = "ASCII", Content = BuildAscii() };
        asciiPanel.Token(PixelPanel.HeaderBrushProperty, "Key");

        var stack = new StackPanel { Spacing = 16, Margin = new Thickness(0, 0, 10, 10), Children = { textPanel, decodePanel, asciiPanel } };
        Content = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        UpdateText();
        UpdateDecode();
    }

    /// <summary>The text input (focused when the tab is shown).</summary>
    public TextBox Input => _text;

    /// <summary>Replaces the text being inspected.</summary>
    public void SetText(string text) => _text.Text = text;

    /// <summary>Replaces the hex bytes being decoded.</summary>
    public void SetBytes(string hex, TextEncodingKind kind)
    {
        _decodeKind = kind;
        _bytes.Text = hex;
        UpdateDecode();
    }

    private void UpdateText()
    {
        var text = _text.Text ?? "";
        var info = TextCodec.Analyze(text);
        var utf8 = TextCodec.Encode(text, TextEncodingKind.Utf8);
        var be = TextCodec.Encode(text, TextEncodingKind.Utf16BigEndian);
        _summary.Text = $"{info.Count} code point{(info.Count == 1 ? "" : "s")}, {utf8.Length} UTF-8 byte{(utf8.Length == 1 ? "" : "s")}, {be.Length} UTF-16 bytes";

        _chars.Children.Clear();
        foreach (var c in info.Take(MaxCharCards)) _chars.Children.Add(CharCard(c));
        _chars.IsVisible = info.Count > 0;
        _more.Text = info.Count > MaxCharCards ? $"+ {info.Count - MaxCharCards} more (all of them are in the byte rows below)" : "";
        _more.IsVisible = info.Count > MaxCharCards;

        _utf8.Text = utf8.Length == 0 ? "(empty)" : TextCodec.Hex(utf8);
        _utf16be.Text = be.Length == 0 ? "(empty)" : TextCodec.Hex(be);
        var le = TextCodec.Encode(text, TextEncodingKind.Utf16LittleEndian);
        _utf16le.Text = le.Length == 0 ? "(empty)" : TextCodec.Hex(le);
        _crc.Text = $"0x{Checksums.Crc32(utf8):X8} (of the UTF-8 bytes)";
        _adler.Text = $"0x{Checksums.Adler32(utf8):X8}";

        var present = new HashSet<int>(info.Select(i => i.CodePoint));
        foreach (var (code, bg) in _asciiCells) bg.Set(present.Contains(code) ? "Sel" : "Card");
    }

    private void UpdateDecode()
    {
        foreach (var (k, t) in _encodings) t.IsChecked = k == _decodeKind;
        try
        {
            var bytes = TextCodec.ParseHexBytes(_bytes.Text ?? "");
            var text = TextCodec.Decode(bytes, _decodeKind);
            _decodedText = text;
            _decoded.Text = bytes.Length == 0 ? "Type bytes in hex to see the text" : $"\"{Visible(text)}\"  ({bytes.Length} byte{(bytes.Length == 1 ? "" : "s")})";
            _decodedInk.Set(bytes.Length == 0 ? "Muted" : "Ink");
            _use.IsEnabled = bytes.Length > 0;
        }
        catch (ProgrammerException ex)
        {
            _decodedText = null;
            _decoded.Text = ex.Message;
            _decodedInk.Set("Err");
            _use.IsEnabled = false;
        }
    }

    /// <summary>Control characters shown by name, so decoded text never hides them.</summary>
    private static string Visible(string text)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in TextCodec.Analyze(text))
            sb.Append(c.IsLoneSurrogate ? "�" : c.CodePoint < 0x20 || c.CodePoint == 0x7F ? $"<{TextCodec.ControlName(c.CodePoint)}>" : char.ConvertFromUtf32(c.CodePoint));
        return sb.ToString();
    }

    private static Control CharCard(CharInfo c)
    {
        var glyph = Ui.Text(c.Display, c.Display.Length > 2 ? 18 : 26, "Math", c.IsLoneSurrogate ? "Err" : "Ink", FontWeight.Bold);
        glyph.HorizontalAlignment = HorizontalAlignment.Left;
        glyph.Height = 34;
        var code = Ui.Text($"{c.UPlus}  ({c.CodePoint})", 13);
        var utf8 = Ui.Text($"UTF-8 {c.Utf8}", 13, "Math", "Muted");
        var utf16 = Ui.Text($"UTF-16 {c.Utf16}", 13, "Math", "Muted");
        foreach (var t in new[] { code, utf8, utf16 }) t.HorizontalAlignment = HorizontalAlignment.Left;
        var stack = new StackPanel { Spacing = 2, Children = { glyph, code, utf8, utf16 } };
        var b = new Border { BorderThickness = new Thickness(3), Padding = new Thickness(8), Margin = new Thickness(0, 0, 8, 8), Child = stack };
        b.Token(Border.BackgroundProperty, "Card").Token(Border.BorderBrushProperty, "Line");
        ToolTip.SetTip(b, c.IsLoneSurrogate ? "A lone surrogate: half of a pair, not a character" : $"{c.UPlus}, decimal {c.CodePoint}");
        return b;
    }

    private Control BuildAscii()
    {
        var grid = new Grid { ColumnSpacing = 3, RowSpacing = 3, Margin = new Thickness(16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(34)));
        for (int c = 0; c < 16; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (int r = 0; r < 9; r++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int c = 0; c < 16; c++)
        {
            var h = Ui.Text(c.ToString("X", CultureInfo.InvariantCulture), 12, "Math", "Muted");
            h.HorizontalAlignment = HorizontalAlignment.Center;
            grid.Children.Add(h.Cell(0, c + 1));
        }
        for (int r = 0; r < 8; r++)
        {
            var label = Ui.Text($"{r:X}0", 12, "Math", "Muted");
            label.HorizontalAlignment = HorizontalAlignment.Left;
            grid.Children.Add(label.Cell(r + 1, 0));
            for (int c = 0; c < 16; c++)
            {
                int code = r * 16 + c;
                var name = TextCodec.ControlName(code);
                bool printable = code is >= 0x20 and < 0x7F;
                var t = Ui.Text(printable ? ((char)code).ToString() : name ?? "", printable ? 15 : 9, "Math", printable ? "Ink" : "Muted", printable ? FontWeight.Bold : FontWeight.Normal);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                string tip = $"0x{code:X2} = {code}{(name is null ? $" '{(char)code}'" : $" {name}")}";
                Control cell;
                if (printable)
                {
                    var b = new FlatButton { Content = t, Height = 28, BorderThickness = new Thickness(2), HorizontalContentAlignment = HorizontalAlignment.Center, Focusable = false };
                    b.Token(BorderBrushProperty, "Line");
                    var ch = ((char)code).ToString();
                    b.Click += (_, _) =>
                    {
                        int caret = Math.Clamp(_text.CaretIndex, 0, (_text.Text ?? "").Length);
                        _text.Text = (_text.Text ?? "").Insert(caret, ch);
                        _text.CaretIndex = caret + 1;
                    };
                    _asciiCells.Add((code, new TokenSlot(b, BackgroundProperty)));
                    ToolTip.SetTip(b, tip + ". Click to add it to the text");
                    cell = b;
                }
                else
                {
                    var b = new Border { Child = t, Height = 28, BorderThickness = new Thickness(2) };
                    b.Token(Border.BorderBrushProperty, "Key").Token(Border.BackgroundProperty, "Paper");
                    ToolTip.SetTip(b, tip + " (control character)");
                    cell = b;
                }
                grid.Children.Add(cell.Cell(r + 1, c + 1));
            }
        }
        return grid;
    }

    private static TextBlock Value()
    {
        var t = Ui.Text("", 15);
        t.HorizontalAlignment = HorizontalAlignment.Left;
        t.VerticalAlignment = VerticalAlignment.Top;
        t.TextWrapping = TextWrapping.Wrap;
        return t;
    }

    private static void AddRow(Grid grid, int row, string label, TextBlock value)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var l = Ui.Pixel(label, 1.5);
        l.HorizontalAlignment = HorizontalAlignment.Left;
        l.VerticalAlignment = VerticalAlignment.Top;
        l.Margin = new Thickness(0, 4, 0, 0);
        grid.Children.Add(l.Cell(row, 0));
        grid.Children.Add(value.Cell(row, 1));
    }
}

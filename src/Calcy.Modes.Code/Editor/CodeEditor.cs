using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;

namespace Calcy.Modes.Code.Editor;

/// <summary>Where the caret and the view were, for saving and restoring editor state.</summary>
/// <param name="CaretLine">Zero based caret line.</param>
/// <param name="CaretColumn">Zero based caret column.</param>
/// <param name="ScrollY">Vertical scroll offset in pixels.</param>
public sealed record EditorViewState(int CaretLine, int CaretColumn, double ScrollY);

/// <summary>
/// The Python code editor: line numbers, syntax highlighting, auto-indent, Tab and Shift+Tab indentation, comment toggle,
/// bracket matching, find, undo and inline error markers. Only the visible lines are laid out and drawn, so files of
/// thousands of lines stay responsive.
/// </summary>
public sealed partial class CodeEditor : UserControl
{
    /// <summary>Height of one line in pixels (design: 24).</summary>
    public const double LineHeight = 24;

    private const double PadTop = 10;
    private const double PadLeft = 14;
    private const double GutterPad = 10;
    private const double GutterBorder = 3;

    private readonly EditorSurface _surface;
    private readonly ScrollViewer _scroll;
    private readonly DispatcherTimer _blink = new() { Interval = TimeSpan.FromMilliseconds(530) };
    private readonly List<LexState> _lineStates = [LexState.Start];
    private readonly Dictionary<int, LineLayout> _layouts = [];
    private readonly List<UndoStep> _undo = [];
    private readonly List<UndoStep> _redo = [];
    private TextPos _caret;
    private TextPos _anchor;
    private double _desiredX = -1;
    private bool _caretOn = true;
    private int _themeVersion;
    private double _charWidth = 9;
    private Typeface _typeface;
    private int? _errorLine;
    private int? _errorColumn;
    private string? _errorMessage;
    private string _findText = "";
    private bool _findMatchCase;
    private (int Version, string Text, bool Case, int Count) _findCount = (-1, "", false, 0);

    /// <summary>Creates an empty editor.</summary>
    public CodeEditor()
    {
        Document = new TextDocument();
        Document.Changed += OnDocumentChanged;
        _surface = new EditorSurface(this);
        _scroll = new ScrollViewer
        {
            Content = _surface,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _scroll.ScrollChanged += (_, e) =>
        {
            if (e.ViewportDelta != default) _surface.InvalidateMeasure();
            _surface.InvalidateVisual();
        };
        Content = _scroll;
        _blink.Tick += (_, _) =>
        {
            _caretOn = !_caretOn;
            if (_surface.IsFocused) _surface.InvalidateVisual();
        };
        BindResources();
        UpdateTypeface();
        _surface.ContextMenu = BuildContextMenu();
    }

    /// <summary>The text being edited.</summary>
    public TextDocument Document { get; }

    /// <summary>The whole text. Setting it resets the caret, the selection and undo.</summary>
    public string Text
    {
        get => Document.Text;
        set
        {
            Document.Text = value ?? "";
            _undo.Clear();
            _redo.Clear();
            _caret = _anchor = new TextPos(0, 0);
            _desiredX = -1;
            ClearError();
            _scroll.Offset = default;
            TextChanged?.Invoke(this, EventArgs.Empty);
            CaretMoved?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The caret position.</summary>
    public TextPos Caret => _caret;

    /// <summary>The other end of the selection (equal to <see cref="Caret"/> when nothing is selected).</summary>
    public TextPos Anchor => _anchor;

    /// <summary>True when text is selected.</summary>
    public bool HasSelection => _caret != _anchor;

    /// <summary>The selected text.</summary>
    public string SelectedText => HasSelection ? Document.GetText(_anchor, _caret) : "";

    /// <summary>True when there is something to undo.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>True when there is something to redo.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>The line (1 based) with an error marker, or null.</summary>
    public int? ErrorLine => _errorLine + 1;

    /// <summary>The message shown next to the error marker.</summary>
    public string? ErrorMessage => _errorMessage;

    /// <summary>The editing surface (the focusable part).</summary>
    public Control Surface => _surface;

    /// <summary>Raised after every change of the text.</summary>
    public event EventHandler? TextChanged;

    /// <summary>Raised when the caret or the selection moves.</summary>
    public event EventHandler? CaretMoved;

    /// <summary>Raised for Ctrl+F so the screen can show its find bar.</summary>
    public event EventHandler? FindRequested;

    /// <summary>Gives the editor keyboard focus.</summary>
    public void FocusEditor() => _surface.Focus();

    /// <summary>The highlighted tokens of a line (0 based).</summary>
    public IReadOnlyList<Token> TokensOf(int line) => PythonHighlighter.Tokenize(Document[line], StateAt(line)).Tokens;

    // ------------------------------------------------------------------ caret and selection

    /// <summary>Moves the caret; with <paramref name="extend"/> the selection grows from the anchor.</summary>
    public void SetCaret(TextPos pos, bool extend = false, bool keepDesiredX = false)
    {
        pos = Document.Clamp(pos);
        _caret = pos;
        if (!extend) _anchor = pos;
        if (!keepDesiredX) _desiredX = -1;
        CaretChanged();
    }

    /// <summary>Selects from <paramref name="anchor"/> to <paramref name="caret"/>.</summary>
    public void Select(TextPos anchor, TextPos caret)
    {
        _anchor = Document.Clamp(anchor);
        _caret = Document.Clamp(caret);
        _desiredX = -1;
        CaretChanged();
    }

    /// <summary>Selects everything.</summary>
    public void SelectAll() => Select(new TextPos(0, 0), Document.End);

    /// <summary>Shows a line (1 based), selecting it, or puts the caret on a column (1 based) of it.</summary>
    public void GoToLine(int line, int? column = null)
    {
        int l = Math.Clamp(line - 1, 0, Document.LineCount - 1);
        if (column is { } c) SetCaret(new TextPos(l, Math.Max(0, c - 1)));
        else Select(new TextPos(l, 0), new TextPos(l, Document[l].Length));
        ScrollToCaret(center: true);
    }

    private void CaretChanged()
    {
        _caretOn = true;
        _blink.Stop();
        _blink.Start();
        ScrollToCaret();
        _surface.InvalidateVisual();
        CaretMoved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The current view state (caret and scroll position).</summary>
    public EditorViewState ViewState => new(_caret.Line, _caret.Column, _scroll.Offset.Y);

    /// <summary>Restores a saved view state.</summary>
    public void RestoreViewState(EditorViewState? state)
    {
        if (state is null) return;
        var p = Document.Clamp(new TextPos(state.CaretLine, state.CaretColumn));
        _caret = _anchor = p;
        Dispatcher.UIThread.Post(() =>
        {
            _scroll.Offset = new Vector(0, Math.Max(0, state.ScrollY));
            _surface.InvalidateVisual();
        }, DispatcherPriority.Loaded);
        CaretMoved?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------------ error marker

    /// <summary>Marks a line (1 based) as the place of an error, with an optional column (1 based) and a message.</summary>
    public void SetError(int line, int? column, string message)
    {
        _errorLine = Math.Clamp(line - 1, 0, Document.LineCount - 1);
        _errorColumn = column is { } c && c > 0 ? c - 1 : null;
        _errorMessage = message;
        _surface.InvalidateVisual();
    }

    /// <summary>Removes the error marker.</summary>
    public void ClearError()
    {
        if (_errorLine is null) return;
        _errorLine = null;
        _errorColumn = null;
        _errorMessage = null;
        _surface.InvalidateVisual();
    }

    // ------------------------------------------------------------------ find

    /// <summary>The text searched for (highlighted in the editor); empty for none.</summary>
    public string FindText
    {
        get => _findText;
        set
        {
            _findText = value ?? "";
            _surface.InvalidateVisual();
        }
    }

    /// <summary>When true, find matches case.</summary>
    public bool FindMatchCase
    {
        get => _findMatchCase;
        set
        {
            _findMatchCase = value;
            _surface.InvalidateVisual();
        }
    }

    private StringComparison FindComparison => _findMatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>How many times <see cref="FindText"/> occurs.</summary>
    public int FindMatchCount
    {
        get
        {
            if (_findText.Length == 0) return 0;
            if (_findCount.Version == Document.Version && _findCount.Text == _findText && _findCount.Case == _findMatchCase) return _findCount.Count;
            int count = 0;
            for (int i = 0; i < Document.LineCount; i++)
            {
                var s = Document[i];
                int at = 0;
                while ((at = s.IndexOf(_findText, at, FindComparison)) >= 0)
                {
                    count++;
                    at += _findText.Length;
                }
            }
            _findCount = (Document.Version, _findText, _findMatchCase, count);
            return count;
        }
    }

    /// <summary>Selects the next match after the caret (wrapping); false when there is none.</summary>
    public bool FindNext()
    {
        if (_findText.Length == 0) return false;
        var start = TextPos.Max(_caret, _anchor);
        for (int k = 0; k <= Document.LineCount; k++)
        {
            int line = (start.Line + k) % Document.LineCount;
            int from = k == 0 ? start.Column : 0;
            if (k == Document.LineCount) from = 0;
            var s = Document[line];
            int at = from <= s.Length ? s.IndexOf(_findText, from, FindComparison) : -1;
            if (at >= 0)
            {
                Select(new TextPos(line, at), new TextPos(line, at + _findText.Length));
                ScrollToCaret(center: true);
                return true;
            }
        }
        return false;
    }

    /// <summary>Selects the previous match before the caret (wrapping); false when there is none.</summary>
    public bool FindPrevious()
    {
        if (_findText.Length == 0) return false;
        var start = TextPos.Min(_caret, _anchor);
        for (int k = 0; k <= Document.LineCount; k++)
        {
            int line = ((start.Line - k) % Document.LineCount + Document.LineCount) % Document.LineCount;
            var s = Document[line];
            int before = k == 0 ? start.Column - 1 : s.Length - 1;
            if (k == Document.LineCount) before = s.Length - 1;
            int at = before >= 0 && s.Length > 0 ? s.LastIndexOf(_findText, Math.Min(before, s.Length - 1), FindComparison) : -1;
            if (at >= 0)
            {
                Select(new TextPos(line, at), new TextPos(line, at + _findText.Length));
                ScrollToCaret(center: true);
                return true;
            }
        }
        return false;
    }

    /// <summary>Replaces the selected match with <paramref name="replacement"/> and finds the next one.</summary>
    public bool ReplaceNext(string replacement)
    {
        if (_findText.Length == 0) return false;
        if (HasSelection && string.Equals(SelectedText, _findText, FindComparison))
        {
            var step = Begin();
            ReplaceRange(step, _anchor, _caret, replacement);
            Commit(step);
        }
        return FindNext();
    }

    /// <summary>Replaces every match; returns how many were replaced.</summary>
    public int ReplaceAll(string replacement)
    {
        if (_findText.Length == 0) return 0;
        var step = Begin();
        int count = 0;
        for (int line = 0; line < Document.LineCount; line++)
        {
            int at = 0;
            while (at <= Document[line].Length && (at = Document[line].IndexOf(_findText, at, FindComparison)) >= 0)
            {
                ReplaceRange(step, new TextPos(line, at), new TextPos(line, at + _findText.Length), replacement);
                at += replacement.Length;
                count++;
            }
        }
        if (count > 0)
        {
            _caret = _anchor = Document.Clamp(_caret);
            Commit(step);
        }
        return count;
    }

    // ------------------------------------------------------------------ bracket matching

    /// <summary>The bracket next to the caret and its partner, or null.</summary>
    public (TextPos Bracket, TextPos Match)? BracketPair()
    {
        if (HasSelection) return null;
        foreach (var col in new[] { _caret.Column - 1, _caret.Column })
        {
            if (col < 0 || col >= Document[_caret.Line].Length) continue;
            var tokens = TokensOf(_caret.Line);
            if (!tokens.Any(t => t.Kind == TokenKind.Bracket && t.Start == col)) continue;
            var at = new TextPos(_caret.Line, col);
            var match = FindMatch(at);
            if (match is { } m) return (at, m);
        }
        return null;
    }

    private TextPos? FindMatch(TextPos at)
    {
        char c = Document[at.Line][at.Column];
        int idx = "([{)]}".IndexOf(c);
        if (idx < 0) return null;
        bool forward = idx < 3;
        char open = "([{"[idx % 3], close = ")]}"[idx % 3];
        int depth = 0;
        int maxLines = 4000;
        for (int line = at.Line; forward ? line < Document.LineCount : line >= 0; line += forward ? 1 : -1)
        {
            if (Math.Abs(line - at.Line) > maxLines) break;
            var tokens = TokensOf(line).Where(t => t.Kind == TokenKind.Bracket);
            if (!forward) tokens = tokens.Reverse();
            foreach (var t in tokens)
            {
                if (line == at.Line && (forward ? t.Start < at.Column : t.Start > at.Column)) continue;
                char b = Document[line][t.Start];
                if (b == open) depth += forward ? 1 : -1;
                else if (b == close) depth += forward ? -1 : 1;
                else continue;
                if (depth == 0) return new TextPos(line, t.Start);
            }
        }
        return null;
    }

    // ------------------------------------------------------------------ highlighting state

    private LexState StateAt(int line)
    {
        line = Math.Clamp(line, 0, Document.LineCount - 1);
        while (_lineStates.Count <= line)
        {
            int i = _lineStates.Count - 1;
            _lineStates.Add(PythonHighlighter.Tokenize(Document[i], _lineStates[i]).End);
        }
        return _lineStates[line];
    }

    private void OnDocumentChanged(object? sender, int firstLine)
    {
        if (_lineStates.Count > firstLine + 1) _lineStates.RemoveRange(firstLine + 1, _lineStates.Count - firstLine - 1);
        if (_layouts.Count > 600) ClearLayouts();
        _surface.InvalidateMeasure();
        _surface.InvalidateVisual();
    }

    private void ClearLayouts()
    {
        foreach (var l in _layouts.Values) l.Layout.Dispose();
        _layouts.Clear();
    }

    // ------------------------------------------------------------------ theme

    /// <summary>Colours and font, bound to the scheme resources.</summary>
    private static readonly string[] BrushTokens = ["Ink", "Muted", "Card", "Paper", "Line", "Sel", "Err", "Grid", "Head", "Key", "G1", "G2", "G3", "G4", "G5"];

    private readonly Dictionary<string, IBrush> _brushes = [];

    private void BindResources()
    {
        foreach (var token in BrushTokens)
        {
            var key = "Calcy." + token;
            this.GetResourceObservable(key).Subscribe(new ActionObserver<object?>(v =>
            {
                if (v is IBrush b) _brushes[token] = b;
                ThemeChanged();
            }));
        }
        this.GetResourceObservable("Calcy.Font.Math").Subscribe(new ActionObserver<object?>(v =>
        {
            if (v is FontFamily f) FontFamily = f;
            UpdateTypeface();
            ThemeChanged();
        }));
        this.Bind(BackgroundProperty, this.GetResourceObservable("Calcy.Card"));
    }

    private IBrush Brush(string token) => _brushes.TryGetValue(token, out var b) ? b : Brushes.Gray;

    private void ThemeChanged()
    {
        _themeVersion++;
        ClearLayouts();
        _surface.InvalidateMeasure();
        _surface.InvalidateVisual();
    }

    private void UpdateTypeface()
    {
        _typeface = new Typeface(FontFamily);
        using var probe = new TextLayout("MMMMMMMMMM", _typeface, FontSizeValue, Brushes.Black);
        _charWidth = Math.Max(4, probe.WidthIncludingTrailingWhitespace / 10);
    }

    private const double FontSizeValue = 15;

    private double GutterWidth => GutterPad * 2 + Math.Max(2, Document.LineCount.ToString(System.Globalization.CultureInfo.InvariantCulture).Length) * _charWidth + GutterBorder;

    private double TextLeft => GutterWidth + PadLeft;

    private sealed record LineLayout(string Text, LexState State, int Theme, TextLayout Layout);

    private TextLayout LayoutOf(int line)
    {
        var text = Document[line];
        var state = StateAt(line);
        if (_layouts.TryGetValue(line, out var cached) && cached.Text == text && cached.State == state && cached.Theme == _themeVersion)
            return cached.Layout;
        if (cached is not null) cached.Layout.Dispose();
        var tokens = PythonHighlighter.Tokenize(text, state).Tokens;
        var overrides = new List<Avalonia.Utilities.ValueSpan<TextRunProperties>>(tokens.Count);
        foreach (var t in tokens)
        {
            if (t.Kind == TokenKind.Text) continue;
            var (brush, weight, style) = StyleOf(t.Kind);
            var face = new Typeface(FontFamily, style, weight);
            overrides.Add(new(t.Start, t.Length, new GenericTextRunProperties(face, FontSizeValue, null, brush, null, BaselineAlignment.Baseline, null, null)));
        }
        var layout = new TextLayout(text, _typeface, FontSizeValue, Brush("Ink"), TextAlignment.Left, TextWrapping.NoWrap,
            null, null, FlowDirection.LeftToRight, double.PositiveInfinity, double.PositiveInfinity, double.NaN, 0, 0, null, overrides);
        _layouts[line] = new LineLayout(text, state, _themeVersion, layout);
        return layout;
    }

    private (IBrush Brush, FontWeight Weight, FontStyle Style) StyleOf(TokenKind kind) => kind switch
    {
        TokenKind.Comment => (Brush("Muted"), FontWeight.Normal, FontStyle.Italic),
        TokenKind.String => (Brush("G1"), FontWeight.Normal, FontStyle.Normal),
        TokenKind.Number => (Brush("G2"), FontWeight.Normal, FontStyle.Normal),
        TokenKind.Keyword => (Brush("G4"), FontWeight.Bold, FontStyle.Normal),
        TokenKind.Builtin => (Brush("G4"), FontWeight.Normal, FontStyle.Normal),
        TokenKind.Decorator => (Brush("G5"), FontWeight.Bold, FontStyle.Normal),
        TokenKind.Definition => (Brush("Ink"), FontWeight.Bold, FontStyle.Normal),
        _ => (Brush("Ink"), FontWeight.Normal, FontStyle.Normal),
    };

    /// <summary>The colour token used for a kind of token (for tests and documentation).</summary>
    public static string ColorTokenOf(TokenKind kind) => kind switch
    {
        TokenKind.Comment => "Muted",
        TokenKind.String => "G1",
        TokenKind.Number => "G2",
        TokenKind.Keyword or TokenKind.Builtin => "G4",
        TokenKind.Decorator => "G5",
        _ => "Ink",
    };

    private double XOf(TextPos p)
    {
        if (p.Column <= 0) return TextLeft;
        var layout = LayoutOf(p.Line);
        if (p.Column >= Document[p.Line].Length) return TextLeft + layout.WidthIncludingTrailingWhitespace;
        return TextLeft + layout.HitTestTextPosition(p.Column).X;
    }

    private int ColumnAt(int line, double x)
    {
        var text = Document[line];
        if (text.Length == 0 || x <= TextLeft) return 0;
        var layout = LayoutOf(line);
        var local = new Point(x - TextLeft, layout.Height / 2);
        var hit = layout.HitTestPoint(local);
        return Math.Clamp(hit.TextPosition, 0, text.Length);
    }

    private void ScrollToCaret(bool center = false)
    {
        var viewport = _scroll.Viewport;
        if (viewport.Width <= 0 || viewport.Height <= 0) return;
        var offset = _scroll.Offset;
        double top = PadTop + _caret.Line * LineHeight, bottom = top + LineHeight;
        double y = offset.Y;
        if (center && (top < y || bottom > y + viewport.Height)) y = top - viewport.Height / 2 + LineHeight / 2;
        else if (top < y) y = top - 4;
        else if (bottom > y + viewport.Height) y = bottom - viewport.Height + 4;
        double cx = XOf(_caret);
        double x = offset.X;
        double left = x + GutterWidth + 8, right = x + viewport.Width - 16;
        if (cx < left) x = Math.Max(0, cx - GutterWidth - 40);
        else if (cx > right) x = cx - viewport.Width + 60;
        var target = new Vector(Math.Max(0, x), Math.Max(0, y));
        if (target != offset) _scroll.Offset = target;
    }

    private sealed class ActionObserver<T>(Action<T> next) : IObserver<T>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(T value) => next(value);
    }
}

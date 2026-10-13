using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using Calcy.Core;
using Calcy.UI.Math.Layout;
using Calcy.UI.Math.Model;
using Calcy.UI.Math.Rendering;

namespace Calcy.UI.Math;

/// <summary>Data of <see cref="MathEditor.Submitted"/>.</summary>
public sealed class MathSubmittedEventArgs(string text) : EventArgs
{
    /// <summary>The submitted linear text.</summary>
    public string Text { get; } = text;
}

/// <summary>Data of <see cref="MathEditor.TextChanged"/>.</summary>
public sealed class MathTextChangedEventArgs(string oldText, string newText) : EventArgs
{
    /// <summary>The previous text.</summary>
    public string OldText { get; } = oldText;

    /// <summary>The new text.</summary>
    public string NewText { get; } = newText;
}

/// <summary>Data of <see cref="MathEditor.CaretSlotChanged"/>.</summary>
public sealed class MathCaretSlotChangedEventArgs(MathTemplateKind? template, int slotIndex, string? hint) : EventArgs
{
    /// <summary>The template whose slot now holds the caret, or null at the top level.</summary>
    public MathTemplateKind? Template { get; } = template;

    /// <summary>The slot index in Tab order, or -1 at the top level.</summary>
    public int SlotIndex { get; } = slotIndex;

    /// <summary>A hint such as "Typing the upper bound. Tab: next slot.", or null at the top level.</summary>
    public string? Hint { get; } = hint;
}

/// <summary>
/// A structured maths input in the style of TI MathPrint and Casio Natural Display: templates with slots,
/// "/" makes a fraction, "^" a power, "sqrt" a root, "pi" gives π, Tab moves between slots. <see cref="Text"/>
/// is two-way linear Calcy syntax.
/// </summary>
public class MathEditor : Control
{
    /// <summary>
    /// Turns the blinking caret on or off for every editor (off for tests and reduced motion). When off the caret
    /// is always shown.
    /// </summary>
    public static bool AnimationsEnabled { get; set; } = true;

    /// <summary>The content as linear Calcy syntax (two-way).</summary>
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<MathEditor, string>(nameof(Text), "", defaultBindingMode: BindingMode.TwoWay, coerce: (_, v) => v ?? "");

    /// <summary>Faint text shown while the editor is empty.</summary>
    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<MathEditor, string?>(nameof(PlaceholderText));

    /// <summary>Font size of the top-level text.</summary>
    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<MathEditor, double>(nameof(FontSize), 22.0, validate: v => v > 0 && double.IsFinite(v));

    /// <summary>Optional session: its functions and variables guide parsing and serialization.</summary>
    public static readonly StyledProperty<CalcSession?> SessionProperty =
        AvaloniaProperty.Register<MathEditor, CalcSession?>(nameof(Session));

    /// <summary>Space between the border and the maths.</summary>
    public static readonly StyledProperty<Thickness> PaddingProperty =
        AvaloniaProperty.Register<MathEditor, Thickness>(nameof(Padding), new Thickness(10));

    /// <summary>Border thickness in pixels.</summary>
    public static readonly StyledProperty<double> BorderThicknessProperty =
        AvaloniaProperty.Register<MathEditor, double>(nameof(BorderThickness), 3.0);

    /// <summary>Background override; null uses "Calcy.Card".</summary>
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<MathEditor, IBrush?>(nameof(Background));

    /// <summary>Border override; null uses "Calcy.Line".</summary>
    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        AvaloniaProperty.Register<MathEditor, IBrush?>(nameof(BorderBrush));

    /// <summary>Text colour override; null uses "Calcy.Ink".</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<MathEditor, IBrush?>(nameof(Foreground));

    /// <summary>Draw single-letter variables in italics.</summary>
    public static readonly StyledProperty<bool> ItalicVariablesProperty =
        AvaloniaProperty.Register<MathEditor, bool>(nameof(ItalicVariables));

    /// <summary>Command executed with the text as parameter when Enter is pressed.</summary>
    public static readonly StyledProperty<ICommand?> SubmitCommandProperty =
        AvaloniaProperty.Register<MathEditor, ICommand?>(nameof(SubmitCommand));

    /// <summary>The hint for the slot that holds the caret (null at the top level).</summary>
    public static readonly DirectProperty<MathEditor, string?> HintTextProperty =
        AvaloniaProperty.RegisterDirect<MathEditor, string?>(nameof(HintText), o => o.HintText);

    private readonly MathEditorState _state;
    private MathLayout? _layout;
    private MathPalette? _palette;
    private AvaloniaMathFont? _font;
    private DispatcherTimer? _blink;
    private bool _caretOn = true;
    private bool _syncingText;
    private bool _dragging;
    private double _scrollX;
    private string? _hint;
    private MathRow? _lastSlot;

    static MathEditor()
    {
        FocusableProperty.OverrideDefaultValue<MathEditor>(true);
        AffectsMeasure<MathEditor>(FontSizeProperty, PaddingProperty, BorderThicknessProperty, ItalicVariablesProperty, PlaceholderTextProperty);
        AffectsRender<MathEditor>(BackgroundProperty, BorderBrushProperty, ForegroundProperty);
    }

    /// <summary>Creates an empty editor.</summary>
    public MathEditor()
    {
        _state = new MathEditorState();
        _state.ContentChanged += OnContentChanged;
        _state.CaretChanged += OnCaretChanged;
        Cursor = new Cursor(StandardCursorType.Ibeam);
        ClipToBounds = true;
        ResourcesChanged += (_, _) => OnThemeChanged();
        ActualThemeVariantChanged += (_, _) => OnThemeChanged();
    }

    /// <summary>Raised when Enter is pressed.</summary>
    public event EventHandler<MathSubmittedEventArgs>? Submitted;

    /// <summary>Raised when <see cref="Text"/> changes (typing or setting it).</summary>
    public event EventHandler<MathTextChangedEventArgs>? TextChanged;

    /// <summary>Raised when the caret moves into another slot (or back to the top level).</summary>
    public event EventHandler<MathCaretSlotChangedEventArgs>? CaretSlotChanged;

    /// <inheritdoc cref="TextProperty"/>
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    /// <inheritdoc cref="PlaceholderTextProperty"/>
    public string? PlaceholderText { get => GetValue(PlaceholderTextProperty); set => SetValue(PlaceholderTextProperty, value); }

    /// <inheritdoc cref="FontSizeProperty"/>
    public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

    /// <inheritdoc cref="SessionProperty"/>
    public CalcSession? Session { get => GetValue(SessionProperty); set => SetValue(SessionProperty, value); }

    /// <inheritdoc cref="PaddingProperty"/>
    public Thickness Padding { get => GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }

    /// <inheritdoc cref="BorderThicknessProperty"/>
    public double BorderThickness { get => GetValue(BorderThicknessProperty); set => SetValue(BorderThicknessProperty, value); }

    /// <inheritdoc cref="BackgroundProperty"/>
    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

    /// <inheritdoc cref="BorderBrushProperty"/>
    public IBrush? BorderBrush { get => GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }

    /// <inheritdoc cref="ForegroundProperty"/>
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    /// <inheritdoc cref="ItalicVariablesProperty"/>
    public bool ItalicVariables { get => GetValue(ItalicVariablesProperty); set => SetValue(ItalicVariablesProperty, value); }

    /// <inheritdoc cref="SubmitCommandProperty"/>
    public ICommand? SubmitCommand { get => GetValue(SubmitCommandProperty); set => SetValue(SubmitCommandProperty, value); }

    /// <inheritdoc cref="HintTextProperty"/>
    public string? HintText { get => _hint; private set => SetAndRaise(HintTextProperty, ref _hint, value); }

    /// <summary>The editing model (structure, caret, selection, undo).</summary>
    public MathEditorState EditorState => _state;

    /// <summary>The current layout (after measure), for tests and tools.</summary>
    public MathLayout? CurrentLayout => _layout;

    // ================================================================== public commands

    /// <summary>Inserts a template at the caret (the selection goes into its main slot) and focuses the editor.</summary>
    public void InsertTemplate(MathTemplateKind kind)
    {
        _state.InsertTemplate(kind);
        FocusSelf();
    }

    /// <summary>Inserts a matrix of the given size.</summary>
    public void InsertMatrix(int rows, int columns)
    {
        _state.InsertMatrix(rows, columns);
        FocusSelf();
    }

    /// <summary>Types text at the caret as if from the keyboard ("sin(" gives sin with brackets, "π" gives π).</summary>
    public void InsertText(string text)
    {
        _state.TypeText(text);
        FocusSelf();
    }

    /// <summary>Inserts a symbol-map entry (a template or text).</summary>
    public void InsertSymbol(MathSymbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        if (symbol.Template is { } kind) InsertTemplate(kind);
        else InsertText(symbol.Insert);
    }

    /// <summary>Inserts linear text keeping its structure, as a paste does.</summary>
    public void PasteText(string text)
    {
        _state.Paste(text);
        FocusSelf();
    }

    /// <summary>Removes all content.</summary>
    public void Clear() => _state.Clear();

    /// <summary>Puts the caret in the first empty slot. Returns false when there is none.</summary>
    public bool FocusFirstEmptySlot()
    {
        bool found = _state.FocusFirstEmptySlot();
        FocusSelf();
        return found;
    }

    /// <summary>Undoes the last edit.</summary>
    public bool Undo() => _state.Undo();

    /// <summary>Redoes the last undone edit.</summary>
    public bool Redo() => _state.Redo();

    /// <summary>Selects everything.</summary>
    public void SelectAll() => _state.SelectAll();

    /// <summary>Adds a row to the matrix or piecewise definition around the caret.</summary>
    public bool AddMatrixRow() => _state.AddGridRow();

    /// <summary>Adds a column to the matrix around the caret.</summary>
    public bool AddMatrixColumn() => _state.AddGridColumn();

    /// <summary>Removes the matrix (or piecewise) row that holds the caret.</summary>
    public bool RemoveMatrixRow() => _state.RemoveGridRow();

    /// <summary>Removes the matrix column that holds the caret.</summary>
    public bool RemoveMatrixColumn() => _state.RemoveGridColumn();

    /// <summary>Raises <see cref="Submitted"/> and runs <see cref="SubmitCommand"/>.</summary>
    public void Submit()
    {
        string text = _state.Text;
        Submitted?.Invoke(this, new MathSubmittedEventArgs(text));
        if (SubmitCommand is { } cmd && cmd.CanExecute(text)) cmd.Execute(text);
    }

    /// <summary>Copies the selection (or everything) to the clipboard as linear text.</summary>
    public async Task CopyAsync()
    {
        var text = _state.GetSelectedText() ?? _state.Text;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb && text.Length > 0) await cb.SetTextAsync(text);
    }

    /// <summary>Copies the selection to the clipboard and deletes it.</summary>
    public async Task CutAsync()
    {
        var text = _state.GetSelectedText();
        if (text is null) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) await cb.SetTextAsync(text);
        _state.DeleteSelection();
    }

    /// <summary>Pastes linear text from the clipboard, keeping its structure.</summary>
    public async Task PasteAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } cb) return;
        var text = await cb.TryGetTextAsync();
        if (!string.IsNullOrEmpty(text)) _state.Paste(text);
    }

    private void FocusSelf()
    {
        if (TopLevel.GetTopLevel(this) is not null && !IsFocused) Focus();
    }

    // ================================================================== model events

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty && !_syncingText)
        {
            var newText = change.GetNewValue<string>() ?? "";
            var oldText = change.GetOldValue<string>() ?? "";
            if (newText != _state.Text)
            {
                _syncingText = true;
                try
                {
                    _state.SetText(newText);
                }
                finally
                {
                    _syncingText = false;
                }
            }
            TextChanged?.Invoke(this, new MathTextChangedEventArgs(oldText, newText));
            RaiseValueChanged(oldText, newText);
        }
        else if (change.Property == SessionProperty)
        {
            _state.ParseContext = change.GetNewValue<CalcSession?>();
            // Re-read the text: the session may know names that change its structure (f(x) as a call).
            if (!_state.IsEmpty)
            {
                _syncingText = true;
                try
                {
                    _state.SetText(Text, undoable: false);
                }
                finally
                {
                    _syncingText = false;
                }
            }
        }
    }

    private void OnContentChanged(object? sender, EventArgs e)
    {
        InvalidateMeasure();
        InvalidateVisual();
        if (_syncingText) return;
        string old = Text;
        string now = _state.Text;
        if (old == now) return;
        _syncingText = true;
        try
        {
            SetCurrentValue(TextProperty, now);
        }
        finally
        {
            _syncingText = false;
        }
        TextChanged?.Invoke(this, new MathTextChangedEventArgs(old, now));
        RaiseValueChanged(old, now);
    }

    private void OnCaretChanged(object? sender, EventArgs e)
    {
        ResetBlink();
        InvalidateVisual();
        var row = _state.Caret.Row;
        var slot = row.Owner is null ? null : row;
        if (!ReferenceEquals(slot, _lastSlot))
        {
            _lastSlot = slot;
            var hint = _state.CurrentHint;
            HintText = hint;
            CaretSlotChanged?.Invoke(this, new MathCaretSlotChangedEventArgs(row.Owner?.Kind, row.SlotIndex, hint));
        }
        else HintText = _state.CurrentHint;
    }

    private void RaiseValueChanged(string old, string now)
    {
        if (ControlAutomationPeer.FromElement(this) is { } peer)
            peer.RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, old, now);
    }

    private void OnThemeChanged()
    {
        _palette = null;
        var font = MathPalette.FindFont(this);
        if (!ReferenceEquals(font, _font))
        {
            _font = font;
            InvalidateMeasure();
        }
        InvalidateVisual();
    }

    // ================================================================== layout and rendering

    private double Chrome => BorderThickness;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        _font ??= MathPalette.FindFont(this);
        var engine = new MathLayoutEngine(_font, new MathLayoutOptions { FontSize = FontSize, ItalicVariables = ItalicVariables, ShowEmptySlots = true });
        _layout = engine.Layout(_state.Root);
        var m = _font.GetFontMetrics(FontSize);
        var pad = Padding;
        double contentW = _layout.Width + 0.6 * FontSize;
        if (_state.IsEmpty && !string.IsNullOrEmpty(PlaceholderText))
            contentW = System.Math.Max(contentW, _font.Measure(PlaceholderText, FontSize * 0.8, false).Width + 0.6 * FontSize);
        double contentH = System.Math.Max(_layout.Height, m.Ascent + m.Descent);
        double w = contentW + pad.Left + pad.Right + 2 * Chrome;
        double h = contentH + pad.Top + pad.Bottom + 2 * Chrome;
        if (double.IsFinite(availableSize.Width)) w = System.Math.Min(w, availableSize.Width);
        return new Size(System.Math.Max(w, 4 * FontSize), h);
    }

    /// <summary>Where the layout's (0, 0) is drawn, in control coordinates.</summary>
    private Point ContentOrigin()
    {
        if (_layout is null) return default;
        var pad = Padding;
        double innerLeft = Chrome + pad.Left;
        double innerW = Bounds.Width - innerLeft - Chrome - pad.Right;
        double innerTop = Chrome + pad.Top;
        double innerH = Bounds.Height - innerTop - Chrome - pad.Bottom;
        // Keep the caret visible when the content is wider than the editor.
        var caret = _layout.GetCaretRect(_state.Caret);
        if (_layout.Width + 0.6 * FontSize <= innerW) _scrollX = 0;
        else
        {
            if (caret.Right - _scrollX > innerW) _scrollX = caret.Right - innerW;
            if (caret.Left - _scrollX < 0) _scrollX = caret.Left;
            _scrollX = System.Math.Clamp(_scrollX, 0, System.Math.Max(0, _layout.Width + 0.6 * FontSize - innerW));
        }
        double y = innerTop + System.Math.Max(0, (innerH - _layout.Height) / 2);
        return new Point(innerLeft - _scrollX, y);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        _palette ??= MathPalette.Resolve(this);
        var palette = _palette;
        double bt = BorderThickness;
        var outer = new Rect(Bounds.Size);
        var background = Background ?? palette.Card;
        context.FillRectangle(background, outer, 0);
        if (bt > 0)
        {
            var pen = new Pen(BorderBrush ?? palette.Line, bt, lineJoin: PenLineJoin.Miter);
            context.DrawRectangle(null, pen, outer.Deflate(bt / 2), 0, 0);
        }
        if (_layout is null || _font is null) return;
        var inner = new Rect(bt, bt, System.Math.Max(0, Bounds.Width - 2 * bt), System.Math.Max(0, Bounds.Height - 2 * bt));
        var origin = ContentOrigin();
        var ink = Foreground ?? palette.Ink;
        IBrush Brush(MathColorRole r) => r == MathColorRole.Ink ? ink : palette.For(r);
        IBrush Inverse(MathColorRole r) => background;
        bool focused = IsKeyboardFocusWithin || IsFocused;

        using (context.PushClip(inner))
        using (context.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y)))
        {
            var font = _font;
            var content = _layout.Content;
            // Active slot: filled with the selection colour and a 2 px border.
            var caretRow = _state.Caret.Row;
            if (focused && caretRow.Owner is not null && _layout.Find(caretRow) is { } slotBox)
            {
                var r = slotBox.Bounds.Inflate(new Thickness(3, 2));
                context.FillRectangle(palette.Sel, r, 0);
                context.DrawRectangle(null, new Pen(palette.Line, 2), r.Deflate(1), 0, 0);
            }
            Rect? selRect = null;
            if (_state.Selection is { } sel)
            {
                selRect = _layout.GetRangeRect(sel.Row, sel.Start, sel.End);
                context.FillRectangle(ink, selRect.Value, 0);
            }
            MathRenderer.Draw(context, content, font, Brush);
            if (_state.IsEmpty && !string.IsNullOrEmpty(PlaceholderText)) DrawPlaceholder(context, palette.Muted);
            if (selRect is { } sr)
            {
                using (context.PushClip(sr))
                    MathRenderer.Draw(context, content, font, Inverse);
            }
            if (focused && (_caretOn || !AnimationsEnabled))
            {
                var caret = _layout.GetCaretRect(_state.Caret);
                context.FillRectangle(ink, caret, 0);
                using (context.PushClip(caret))
                {
                    MathRenderer.Draw(context, content, font, Inverse, showEmptySlots: false);
                    if (_state.IsEmpty && !string.IsNullOrEmpty(PlaceholderText)) DrawPlaceholder(context, background);
                }
            }
        }
    }

    private void DrawPlaceholder(DrawingContext context, IBrush brush)
    {
        if (_layout is null || _font is null) return;
        var ft = _font.GetFormattedText(PlaceholderText!, FontSize * 0.8, false);
        ft.SetForegroundBrush(brush);
        var rb = _layout.RootRow;
        context.DrawText(ft, new Point(rb.Origin.X + 0.1 * FontSize, rb.Origin.Y - ft.Baseline));
    }

    // ================================================================== caret blink

    private void ResetBlink()
    {
        _caretOn = true;
        if (_blink is { IsEnabled: true })
        {
            _blink.Stop();
            _blink.Start();
        }
    }

    /// <inheritdoc />
    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        _caretOn = true;
        if (AnimationsEnabled)
        {
            _blink ??= new DispatcherTimer(TimeSpan.FromMilliseconds(530), DispatcherPriority.Normal, (_, _) =>
            {
                _caretOn = !_caretOn;
                InvalidateVisual();
            });
            _blink.Start();
        }
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        _blink?.Stop();
        _dragging = false;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _blink?.Stop();
    }

    // ================================================================== input

    private MathCaret HitTest(Point p)
    {
        var o = ContentOrigin();
        return _layout!.HitTest(new Point(p.X - o.X, p.Y - o.Y));
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        if (_layout is null) return;
        var hit = HitTest(e.GetPosition(this));
        if (e.ClickCount >= 2)
        {
            _state.SetCaret(new MathCaret(hit.Row, 0));
            _state.SetCaret(new MathCaret(hit.Row, hit.Row.Count), extendSelection: true);
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _state.SetCaret(hit, extendSelection: true);
        else
        {
            _state.SetCaret(hit);
            _state.BeginSelection();
            _dragging = true;
            e.Pointer.Capture(this);
        }
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging || _layout is null) return;
        _state.SetCaret(HitTest(e.GetPosition(this)), extendSelection: true);
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging)
        {
            _dragging = false;
            e.Pointer.Capture(null);
            if (_state.Selection is null) _state.ClearSelection();
        }
    }

    /// <inheritdoc />
    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || string.IsNullOrEmpty(e.Text)) return;
        var text = new string(e.Text.Where(c => c >= ' ' && c != '\u007f').ToArray());
        if (text.Length == 0) return;
        _state.TypeText(text);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        e.Handled = HandleKey(e);
    }

    private bool HandleKey(KeyEventArgs e)
    {
        var hk = Application.Current?.PlatformSettings?.HotkeyConfiguration;
        bool Is(List<KeyGesture>? gestures) => gestures?.Any(g => g.Matches(e)) == true;
        bool command = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

        if (Is(hk?.Undo) || (command && !shift && e.Key == Key.Z)) return Undo() || true;
        if (Is(hk?.Redo) || (command && (e.Key == Key.Y || (shift && e.Key == Key.Z)))) return Redo() || true;
        if (Is(hk?.Copy) || (command && e.Key == Key.C)) { _ = CopyAsync(); return true; }
        if (Is(hk?.Cut) || (command && e.Key == Key.X)) { _ = CutAsync(); return true; }
        if (Is(hk?.Paste) || (command && e.Key == Key.V)) { _ = PasteAsync(); return true; }
        if (Is(hk?.SelectAll) || (command && e.Key == Key.A)) { SelectAll(); return true; }

        switch (e.Key)
        {
            case Key.Left:
                _state.MoveLeft(shift);
                return true;
            case Key.Right:
                _state.MoveRight(shift);
                return true;
            case Key.Up:
                return _layout is not null && _state.MoveUp(Geometry());
            case Key.Down:
                return _layout is not null && _state.MoveDown(Geometry());
            case Key.Home:
                _state.MoveHome(shift, command);
                return true;
            case Key.End:
                _state.MoveEnd(shift, command);
                return true;
            case Key.Tab:
                if (command) return false;
                return shift ? _state.PreviousSlot() : _state.NextSlot();
            case Key.Back:
                _state.Backspace();
                return true;
            case Key.Delete:
                _state.DeleteForward();
                return true;
            case Key.Enter:
                if (shift) _state.AddGridRow();
                else Submit();
                return true;
            case Key.Escape:
                if (_state.Selection is null) return false;
                _state.ClearSelection();
                return true;
        }
        return false;
    }

    private IMathCaretGeometry Geometry() => _layout!;

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new MathEditorAutomationPeer(this);
}

/// <summary>Exposes a <see cref="MathEditor"/> to screen readers as an edit box whose value is the linear text.</summary>
public sealed class MathEditorAutomationPeer(MathEditor owner) : ControlAutomationPeer(owner), IValueProvider
{
    private MathEditor Editor => (MathEditor)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;

    /// <inheritdoc />
    protected override string? GetNameCore()
    {
        var name = AutomationProperties.GetName(Owner);
        if (!string.IsNullOrEmpty(name)) return name;
        return string.IsNullOrEmpty(Editor.PlaceholderText) ? "Maths input" : Editor.PlaceholderText;
    }

    /// <inheritdoc />
    protected override string? GetHelpTextCore() => Editor.HintText ?? base.GetHelpTextCore();

    /// <inheritdoc />
    protected override string? GetPlaceholderTextCore() => Editor.PlaceholderText;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public string Value => Editor.Text;

    /// <inheritdoc />
    public void SetValue(string? value) => Editor.Text = value ?? "";
}

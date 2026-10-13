using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Calcy.Modes.Code.Editor;

public sealed partial class CodeEditor
{
    private ContextMenu BuildContextMenu()
    {
        MenuItem Item(string header, string gesture, Action action)
        {
            var item = new MenuItem { Header = header, InputGesture = KeyGesture.Parse(gesture) };
            item.Click += (_, _) => action();
            return item;
        }
        return new ContextMenu
        {
            Items =
            {
                Item("Undo", "Ctrl+Z", () => Undo()),
                Item("Redo", "Ctrl+Y", () => Redo()),
                new Separator(),
                Item("Cut", "Ctrl+X", Cut),
                Item("Copy", "Ctrl+C", Copy),
                Item("Paste", "Ctrl+V", Paste),
                Item("Select all", "Ctrl+A", SelectAll),
                new Separator(),
                Item("Toggle comment", "Ctrl+OemQuestion", ToggleComment),
                Item("Indent", "Tab", Indent),
                Item("Outdent", "Shift+Tab", Outdent),
                Item("Duplicate line", "Ctrl+D", DuplicateLines),
                Item("Find", "Ctrl+F", () => FindRequested?.Invoke(this, EventArgs.Empty)),
            },
        };
    }

    // ------------------------------------------------------------------ keyboard

    internal bool HandleKey(KeyEventArgs e)
    {
        var mods = e.KeyModifiers;
        bool ctrl = mods.HasFlag(KeyModifiers.Control) || mods.HasFlag(KeyModifiers.Meta);
        bool shift = mods.HasFlag(KeyModifiers.Shift);
        bool alt = mods.HasFlag(KeyModifiers.Alt);
        if (alt) return false;
        switch (e.Key)
        {
            case Key.Left:
                MoveHorizontal(-1, ctrl, shift);
                return true;
            case Key.Right:
                MoveHorizontal(1, ctrl, shift);
                return true;
            case Key.Up:
                if (ctrl) return false;
                MoveVertical(-1, shift);
                return true;
            case Key.Down:
                if (ctrl) return false;
                MoveVertical(1, shift);
                return true;
            case Key.PageUp:
                MoveVertical(-PageLines(), shift);
                return true;
            case Key.PageDown:
                MoveVertical(PageLines(), shift);
                return true;
            case Key.Home:
                if (ctrl) SetCaret(new TextPos(0, 0), shift);
                else
                {
                    int indent = Document.IndentOf(_caret.Line);
                    SetCaret(new TextPos(_caret.Line, _caret.Column == indent ? 0 : indent), shift);
                }
                return true;
            case Key.End:
                SetCaret(ctrl ? Document.End : new TextPos(_caret.Line, Document[_caret.Line].Length), shift);
                return true;
            case Key.Enter:
                if (ctrl || shift) return false;
                NewLine();
                return true;
            case Key.Back:
                if (ctrl && !HasSelection) MoveHorizontal(-1, true, true);
                Backspace();
                return true;
            case Key.Delete:
                if (ctrl && !HasSelection) MoveHorizontal(1, true, true);
                Delete();
                return true;
            case Key.Tab:
                if (ctrl) return false;
                if (shift) Outdent();
                else Indent();
                return true;
        }
        if (!ctrl) return false;
        switch (e.Key)
        {
            case Key.A:
                SelectAll();
                return true;
            case Key.C:
                Copy();
                return true;
            case Key.X:
                Cut();
                return true;
            case Key.V:
                Paste();
                return true;
            case Key.Z:
                if (shift) Redo();
                else Undo();
                return true;
            case Key.Y:
                Redo();
                return true;
            case Key.D:
                DuplicateLines();
                return true;
            case Key.F:
                FindRequested?.Invoke(this, EventArgs.Empty);
                return true;
            case Key.Oem2 or Key.Divide:
                ToggleComment();
                return true;
        }
        if (e.KeySymbol == "/")
        {
            ToggleComment();
            return true;
        }
        return false;
    }

    private int PageLines() => Math.Max(1, (int)(_scroll.Viewport.Height / LineHeight) - 1);

    private void MoveHorizontal(int dir, bool word, bool extend)
    {
        if (HasSelection && !extend)
        {
            SetCaret(dir < 0 ? TextPos.Min(_caret, _anchor) : TextPos.Max(_caret, _anchor));
            return;
        }
        var p = _caret;
        var line = Document[p.Line];
        if (dir < 0)
        {
            if (p.Column == 0)
            {
                if (p.Line > 0) p = new TextPos(p.Line - 1, Document[p.Line - 1].Length);
            }
            else if (word)
            {
                int c = p.Column;
                while (c > 0 && char.IsWhiteSpace(line[c - 1])) c--;
                if (c > 0 && IsWord(line[c - 1])) while (c > 0 && IsWord(line[c - 1])) c--;
                else if (c > 0) c--;
                p = new TextPos(p.Line, c);
            }
            else
            {
                int c = p.Column - 1;
                if (c > 0 && char.IsLowSurrogate(line[c]) && char.IsHighSurrogate(line[c - 1])) c--;
                p = new TextPos(p.Line, c);
            }
        }
        else
        {
            if (p.Column >= line.Length)
            {
                if (p.Line + 1 < Document.LineCount) p = new TextPos(p.Line + 1, 0);
            }
            else if (word)
            {
                int c = p.Column;
                if (IsWord(line[c])) while (c < line.Length && IsWord(line[c])) c++;
                else c++;
                while (c < line.Length && char.IsWhiteSpace(line[c])) c++;
                p = new TextPos(p.Line, c);
            }
            else
            {
                int c = p.Column + 1;
                if (c < line.Length && char.IsLowSurrogate(line[c])) c++;
                p = new TextPos(p.Line, c);
            }
        }
        SetCaret(p, extend);
    }

    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';

    private void MoveVertical(int lines, bool extend)
    {
        if (_desiredX < 0) _desiredX = XOf(_caret);
        int target = Math.Clamp(_caret.Line + lines, 0, Document.LineCount - 1);
        TextPos p;
        if (target == _caret.Line && lines != 0) p = lines < 0 ? new TextPos(0, 0) : new TextPos(target, Document[target].Length);
        else p = new TextPos(target, ColumnAt(target, _desiredX));
        var desired = _desiredX;
        SetCaret(p, extend, keepDesiredX: true);
        _desiredX = desired;
    }

    internal void HandleTextInput(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        // Control characters arrive here on some platforms; they are handled as keys.
        if (text.Any(c => char.IsControl(c) && c != '\n')) return;
        InsertText(text);
    }

    // ------------------------------------------------------------------ mouse

    private int _clickCount;
    private bool _dragging;
    private bool _dragLines;
    private int _dragLineAnchor;

    private TextPos HitTest(Point p)
    {
        int line = Math.Clamp((int)Math.Floor((p.Y - PadTop) / LineHeight), 0, Document.LineCount - 1);
        if (p.Y < PadTop) line = 0;
        return new TextPos(line, ColumnAt(line, p.X));
    }

    internal void PointerPressedOnSurface(PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_surface);
        if (!point.Properties.IsLeftButtonPressed)
        {
            if (point.Properties.IsRightButtonPressed)
            {
                var hit = HitTest(point.Position);
                var s = TextPos.Min(_caret, _anchor);
                var en = TextPos.Max(_caret, _anchor);
                if (!(HasSelection && hit >= s && hit <= en)) SetCaret(hit);
            }
            return;
        }
        _surface.Focus();
        _clickCount = e.ClickCount;
        var pos = HitTest(point.Position);
        bool inGutter = point.Position.X - _scroll.Offset.X < GutterWidth;
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (inGutter)
        {
            _dragLines = true;
            _dragLineAnchor = shift ? _anchor.Line : pos.Line;
            SelectLines(_dragLineAnchor, pos.Line);
        }
        else if (_clickCount >= 3)
        {
            _dragLines = true;
            _dragLineAnchor = pos.Line;
            SelectLines(pos.Line, pos.Line);
        }
        else if (_clickCount == 2)
        {
            SelectWordAt(pos);
            _dragLines = false;
        }
        else
        {
            _dragLines = false;
            SetCaret(pos, shift);
        }
        _dragging = true;
        e.Pointer.Capture(_surface);
        e.Handled = true;
    }

    internal void PointerMovedOnSurface(PointerEventArgs e)
    {
        if (!_dragging) return;
        var pos = HitTest(e.GetPosition(_surface));
        if (_dragLines) SelectLines(_dragLineAnchor, pos.Line);
        else if (_clickCount < 2) SetCaret(pos, extend: true);
    }

    internal void PointerReleasedOnSurface(PointerReleasedEventArgs e)
    {
        _dragging = false;
        e.Pointer.Capture(null);
    }

    private void SelectLines(int a, int b)
    {
        int first = Math.Min(a, b), last = Math.Max(a, b);
        var end = last + 1 < Document.LineCount ? new TextPos(last + 1, 0) : new TextPos(last, Document[last].Length);
        if (b < a) Select(end, new TextPos(first, 0));
        else Select(new TextPos(first, 0), end);
    }

    private void SelectWordAt(TextPos p)
    {
        var line = Document[p.Line];
        int s = Math.Min(p.Column, line.Length), e = s;
        if (s < line.Length && IsWord(line[s]) || s > 0 && IsWord(line[s - 1]))
        {
            while (s > 0 && IsWord(line[s - 1])) s--;
            while (e < line.Length && IsWord(line[e])) e++;
        }
        else if (s < line.Length) e = s + 1;
        Select(new TextPos(p.Line, s), new TextPos(p.Line, e));
    }

    // ------------------------------------------------------------------ measuring and drawing

    internal Size MeasureSurface()
    {
        int maxColumns = 0;
        for (int i = 0; i < Document.LineCount; i++) maxColumns = Math.Max(maxColumns, Document[i].Length);
        double width = TextLeft + (maxColumns + 4) * _charWidth + (_errorMessage is null ? 0 : 400);
        double height = PadTop * 2 + Document.LineCount * LineHeight;
        var viewport = _scroll.Viewport;
        return new Size(Math.Max(width, viewport.Width), Math.Max(height, viewport.Height));
    }

    internal void RenderSurface(DrawingContext ctx)
    {
        var offset = _scroll.Offset;
        var viewport = _scroll.Viewport;
        if (viewport.Width <= 0 || viewport.Height <= 0) viewport = _surface.Bounds.Size;
        var visible = new Rect(offset.X, offset.Y, viewport.Width, viewport.Height);
        ctx.FillRectangle(Brush("Card"), visible);

        int first = Math.Max(0, (int)Math.Floor((offset.Y - PadTop) / LineHeight));
        int last = Math.Min(Document.LineCount - 1, (int)Math.Ceiling((offset.Y + viewport.Height - PadTop) / LineHeight));
        var selStart = TextPos.Min(_caret, _anchor);
        var selEnd = TextPos.Max(_caret, _anchor);
        double textLeft = TextLeft;
        var bracket = _surface.IsFocused ? BracketPair() : null;
        var errBrush = Brush("Err");

        for (int line = first; line <= last; line++)
        {
            double y = PadTop + line * LineHeight;
            var text = Document[line];
            var layout = LayoutOf(line);

            if (line == _errorLine)
            {
                using (ctx.PushOpacity(0.16)) ctx.FillRectangle(errBrush, new Rect(offset.X, y, viewport.Width, LineHeight));
            }
            else if (!HasSelection && line == _caret.Line && _surface.IsFocused)
            {
                ctx.FillRectangle(Brush("Grid"), new Rect(offset.X, y, viewport.Width, LineHeight));
            }

            if (_findText.Length > 0)
            {
                int at = 0;
                while ((at = text.IndexOf(_findText, at, FindComparison)) >= 0)
                {
                    double x1 = XOf(new TextPos(line, at)), x2 = XOf(new TextPos(line, at + _findText.Length));
                    using (ctx.PushOpacity(0.45)) ctx.FillRectangle(Brush("Head"), new Rect(x1, y + 2, Math.Max(2, x2 - x1), LineHeight - 4));
                    at += _findText.Length;
                }
            }

            if (HasSelection && line >= selStart.Line && line <= selEnd.Line)
            {
                double x1 = line == selStart.Line ? XOf(selStart) : textLeft;
                double x2 = line == selEnd.Line ? XOf(selEnd) : textLeft + layout.WidthIncludingTrailingWhitespace + _charWidth * 0.6;
                if (x2 > x1) ctx.FillRectangle(Brush("Sel"), new Rect(x1, y, x2 - x1, LineHeight));
            }

            if (bracket is { } bp)
            {
                foreach (var b in new[] { bp.Bracket, bp.Match })
                {
                    if (b.Line != line) continue;
                    double x1 = XOf(b), x2 = XOf(new TextPos(b.Line, b.Column + 1));
                    ctx.DrawRectangle(null, new Pen(Brush("Ink"), 1), new Rect(x1 - 0.5, y + 2.5, x2 - x1 + 1, LineHeight - 5));
                }
            }

            layout.Draw(ctx, new Point(textLeft, y + (LineHeight - layout.Height) / 2));

            if (line == _errorLine)
            {
                double ux1 = _errorColumn is { } ec && ec < text.Length ? XOf(new TextPos(line, ec)) : textLeft + Document.IndentOf(line) * _charWidth;
                double ux2 = _errorColumn is { } ec2 && ec2 < text.Length ? XOf(new TextPos(line, ec2 + 1)) + _charWidth : textLeft + layout.WidthIncludingTrailingWhitespace;
                if (ux2 <= ux1) ux2 = ux1 + _charWidth;
                DrawZigzag(ctx, ux1, ux2, y + LineHeight - 3, errBrush);
                if (!string.IsNullOrEmpty(_errorMessage))
                {
                    var msg = new TextLayout("◀ " + _errorMessage, new Typeface(FontFamily, FontStyle.Italic, FontWeight.Bold), 13, errBrush);
                    double mx = textLeft + layout.WidthIncludingTrailingWhitespace + 24;
                    msg.Draw(ctx, new Point(mx, y + (LineHeight - msg.Height) / 2));
                    msg.Dispose();
                }
            }
        }

        if (_surface.IsFocused && _caretOn && _caret.Line >= first && _caret.Line <= last)
        {
            double cx = XOf(_caret);
            ctx.FillRectangle(Brush("Ink"), new Rect(Math.Round(cx) - 1, PadTop + _caret.Line * LineHeight + 2, 2, LineHeight - 4));
        }

        // The gutter stays put while the text scrolls sideways under it.
        double gw = GutterWidth;
        ctx.FillRectangle(Brush("Paper"), new Rect(offset.X, offset.Y, gw - GutterBorder, viewport.Height));
        ctx.FillRectangle(Brush("Line"), new Rect(offset.X + gw - GutterBorder, offset.Y, GutterBorder, viewport.Height));
        var numberFace = new Typeface(FontFamily);
        var boldFace = new Typeface(FontFamily, FontStyle.Normal, FontWeight.Bold);
        for (int line = first; line <= last; line++)
        {
            double y = PadTop + line * LineHeight;
            bool isError = line == _errorLine;
            bool isCurrent = line == _caret.Line;
            var brush = isError ? errBrush : isCurrent ? Brush("Ink") : Brush("Muted");
            using var num = new TextLayout((line + 1).ToString(CultureInfo.InvariantCulture), isError || isCurrent ? boldFace : numberFace, FontSizeValue, brush);
            double nx = offset.X + gw - GutterBorder - GutterPad - num.WidthIncludingTrailingWhitespace;
            if (isError) ctx.FillRectangle(errBrush, new Rect(offset.X + 3, y + 6, 4, LineHeight - 12));
            num.Draw(ctx, new Point(nx, y + (LineHeight - num.Height) / 2));
        }
    }

    private static void DrawZigzag(DrawingContext ctx, double x1, double x2, double y, IBrush brush)
    {
        // A stepped, pixel style squiggle under the error.
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            bool up = true;
            for (double x = Math.Floor(x1); x < x2; x += 2)
            {
                g.BeginFigure(new Point(x, up ? y - 2 : y), true);
                g.LineTo(new Point(x + 2, up ? y - 2 : y));
                g.LineTo(new Point(x + 2, up ? y : y + 2));
                g.LineTo(new Point(x, up ? y : y + 2));
                g.EndFigure(true);
                up = !up;
            }
        }
        ctx.DrawGeometry(brush, null, geometry);
    }

    /// <summary>The scrolling surface that draws the visible lines and receives keyboard and mouse input.</summary>
    private sealed class EditorSurface : Control
    {
        private readonly CodeEditor _owner;

        public EditorSurface(CodeEditor owner)
        {
            _owner = owner;
            Focusable = true;
            Cursor = new Cursor(StandardCursorType.Ibeam);
            ClipToBounds = false;
        }

        protected override Size MeasureOverride(Size availableSize) => _owner.MeasureSurface();

        public override void Render(DrawingContext context) => _owner.RenderSurface(context);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!e.Handled && _owner.HandleKey(e)) e.Handled = true;
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            base.OnTextInput(e);
            if (e.Handled || e.Text is null) return;
            _owner.HandleTextInput(e.Text);
            e.Handled = true;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            _owner.PointerPressedOnSurface(e);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            _owner.PointerMovedOnSurface(e);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _owner.PointerReleasedOnSurface(e);
        }

        protected override void OnGotFocus(FocusChangedEventArgs e)
        {
            base.OnGotFocus(e);
            InvalidateVisual();
        }

        protected override void OnLostFocus(FocusChangedEventArgs e)
        {
            base.OnLostFocus(e);
            InvalidateVisual();
        }
    }
}

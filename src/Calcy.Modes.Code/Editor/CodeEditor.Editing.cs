using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace Calcy.Modes.Code.Editor;

public sealed partial class CodeEditor
{
    private const int MaxUndo = 500;

    private sealed record Edit(TextPos Start, string Removed, string Inserted);

    /// <summary>One undoable user action: one or more edits plus the caret before and after.</summary>
    private sealed class UndoStep
    {
        public List<Edit> Edits { get; } = [];
        public TextPos CaretBefore { get; init; }
        public TextPos AnchorBefore { get; init; }
        public TextPos CaretAfter { get; set; }
        public TextPos AnchorAfter { get; set; }
        public bool Typing { get; init; }
        public DateTime Time { get; set; } = DateTime.UtcNow;
    }

    private UndoStep Begin(bool typing = false) => new() { CaretBefore = _caret, AnchorBefore = _anchor, Typing = typing };

    private TextPos ReplaceRange(UndoStep step, TextPos a, TextPos b, string text)
    {
        var s = Document.Clamp(TextPos.Min(a, b));
        var e = Document.Clamp(TextPos.Max(a, b));
        text = TextDocument.Normalize(text);
        var removed = Document.GetText(s, e);
        if (removed.Length == 0 && text.Length == 0) return s;
        var end = Document.Replace(s, e, text);
        step.Edits.Add(new Edit(s, removed, text));
        return end;
    }

    private void Commit(UndoStep step)
    {
        if (step.Edits.Count == 0)
        {
            CaretChanged();
            return;
        }
        step.CaretAfter = _caret;
        step.AnchorAfter = _anchor;
        var last = _undo.Count > 0 ? _undo[^1] : null;
        if (step.Typing && last is { Typing: true } && last.Edits.Count == 1 && step.Edits.Count == 1 &&
            (step.Time - last.Time) < TimeSpan.FromSeconds(1.5) && last.CaretAfter == step.CaretBefore && last.AnchorAfter == step.AnchorBefore)
        {
            var a = last.Edits[0];
            var b = step.Edits[0];
            if (b.Removed.Length == 0 && TextDocument.Advance(a.Start, a.Inserted) == b.Start && !b.Inserted.Contains('\n') &&
                !(b.Inserted == " " && !a.Inserted.EndsWith(' ')))
            {
                last.Edits[0] = a with { Inserted = a.Inserted + b.Inserted };
                last.CaretAfter = _caret;
                last.AnchorAfter = _anchor;
                last.Time = step.Time;
                AfterEdit();
                return;
            }
        }
        _undo.Add(step);
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        _redo.Clear();
        AfterEdit();
    }

    private void AfterEdit()
    {
        _desiredX = -1;
        ClearError();
        TextChanged?.Invoke(this, EventArgs.Empty);
        CaretChanged();
    }

    /// <summary>Undoes the last action.</summary>
    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        for (int i = step.Edits.Count - 1; i >= 0; i--)
        {
            var e = step.Edits[i];
            Document.Replace(e.Start, TextDocument.Advance(e.Start, e.Inserted), e.Removed);
        }
        _redo.Add(step);
        _caret = Document.Clamp(step.CaretBefore);
        _anchor = Document.Clamp(step.AnchorBefore);
        AfterEdit();
        return true;
    }

    /// <summary>Redoes the last undone action.</summary>
    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        var step = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        foreach (var e in step.Edits) Document.Replace(e.Start, TextDocument.Advance(e.Start, e.Removed), e.Inserted);
        _undo.Add(step);
        _caret = Document.Clamp(step.CaretAfter);
        _anchor = Document.Clamp(step.AnchorAfter);
        AfterEdit();
        return true;
    }

    // ------------------------------------------------------------------ typing

    /// <summary>Types or pastes text over the selection.</summary>
    public void InsertText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        text = TextDocument.Normalize(text);
        bool typing = text.Length == 1 && text != "\n";
        var step = Begin(typing);
        // Typing a closing bracket or colon on a "dedent" keyword line keeps the code readable.
        if (text == ":" && !HasSelection) DedentKeywordLine(step);
        var end = ReplaceRange(step, _anchor, _caret, text);
        _caret = _anchor = end;
        Commit(step);
    }

    private void DedentKeywordLine(UndoStep step)
    {
        // "else", "elif ...", "except ...", "finally" typed one level too deep move back to match their block.
        var line = Document[_caret.Line];
        var trimmed = line.TrimStart();
        if (_caret.Column != line.Length) return;
        bool keyword = trimmed is "else" or "finally" or "try" || trimmed.StartsWith("elif ", StringComparison.Ordinal) ||
                       trimmed == "except" || trimmed.StartsWith("except ", StringComparison.Ordinal);
        if (!keyword || _caret.Line == 0) return;
        int indent = Document.IndentOf(_caret.Line);
        string opener = trimmed.StartsWith("el", StringComparison.Ordinal) ? "if" : "try";
        for (int l = _caret.Line - 1; l >= 0; l--)
        {
            var t = Document[l].TrimStart();
            if (t.Length == 0 || t.StartsWith('#')) continue;
            int li = Document.IndentOf(l);
            if (li < indent && (t.StartsWith(opener, StringComparison.Ordinal) || t.StartsWith("for", StringComparison.Ordinal) ||
                                t.StartsWith("while", StringComparison.Ordinal) || t.StartsWith("elif", StringComparison.Ordinal) ||
                                t.StartsWith("except", StringComparison.Ordinal)))
            {
                ReplaceRange(step, new TextPos(_caret.Line, 0), new TextPos(_caret.Line, indent), new string(' ', li));
                _caret = _anchor = new TextPos(_caret.Line, Document[_caret.Line].Length);
                return;
            }
            if (li < indent) return;
        }
    }

    /// <summary>Enter: a new line indented like the current one, one level deeper after a colon.</summary>
    public void NewLine()
    {
        var step = Begin();
        var start = TextPos.Min(_caret, _anchor);
        var line = Document[start.Line];
        var before = line[..Math.Min(start.Column, line.Length)];
        int indent = 0;
        while (indent < before.Length && before[indent] == ' ') indent++;
        var code = StripComment(before).TrimEnd();
        if (code.EndsWith(':')) indent += TextDocument.IndentSize;
        else if (IsBlockEnd(code.TrimStart())) indent = Math.Max(0, indent - TextDocument.IndentSize);
        else if (OpenBracketsOn(code) > 0) indent += TextDocument.IndentSize;
        var end = ReplaceRange(step, _anchor, _caret, "\n" + new string(' ', indent));
        // Do not leave trailing spaces on the line that was split.
        var prev = Document[end.Line - 1];
        if (prev.Length > 0 && prev.TrimEnd().Length == 0)
        {
            ReplaceRange(step, new TextPos(end.Line - 1, 0), new TextPos(end.Line - 1, prev.Length), "");
        }
        _caret = _anchor = new TextPos(end.Line, indent);
        Commit(step);
    }

    private static bool IsBlockEnd(string code) =>
        code is "pass" or "break" or "continue" or "return" or "raise" ||
        code.StartsWith("return ", StringComparison.Ordinal) || code.StartsWith("raise ", StringComparison.Ordinal);

    private static int OpenBracketsOn(string code)
    {
        int depth = 0;
        var tokens = PythonHighlighter.Tokenize(code, LexState.Start).Tokens;
        foreach (var t in tokens)
        {
            if (t.Kind != TokenKind.Bracket) continue;
            depth += code[t.Start] is '(' or '[' or '{' ? 1 : -1;
        }
        return depth;
    }

    private static string StripComment(string code)
    {
        var tokens = PythonHighlighter.Tokenize(code, LexState.Start).Tokens;
        var comment = tokens.FirstOrDefault(t => t.Kind == TokenKind.Comment);
        return comment.Length > 0 ? code[..comment.Start] : code;
    }

    /// <summary>Backspace: deletes the selection, a level of indentation, or the character before the caret.</summary>
    public void Backspace()
    {
        var step = Begin();
        if (HasSelection)
        {
            _caret = _anchor = ReplaceRange(step, _anchor, _caret, "");
        }
        else if (_caret.Column > 0)
        {
            var line = Document[_caret.Line];
            var before = line[.._caret.Column];
            int from;
            if (before.Trim().Length == 0)
            {
                // Soft tabs: go back to the previous indentation stop.
                from = (_caret.Column - 1) / TextDocument.IndentSize * TextDocument.IndentSize;
            }
            else
            {
                from = _caret.Column - 1;
                if (from > 0 && char.IsLowSurrogate(line[from]) && char.IsHighSurrogate(line[from - 1])) from--;
            }
            _caret = _anchor = ReplaceRange(step, new TextPos(_caret.Line, from), _caret, "");
        }
        else if (_caret.Line > 0)
        {
            var prevEnd = new TextPos(_caret.Line - 1, Document[_caret.Line - 1].Length);
            _caret = _anchor = ReplaceRange(step, prevEnd, _caret, "");
        }
        Commit(step);
    }

    /// <summary>Delete: deletes the selection or the character after the caret.</summary>
    public void Delete()
    {
        var step = Begin();
        if (HasSelection)
        {
            _caret = _anchor = ReplaceRange(step, _anchor, _caret, "");
        }
        else
        {
            var line = Document[_caret.Line];
            TextPos to;
            if (_caret.Column < line.Length)
            {
                int n = char.IsHighSurrogate(line[_caret.Column]) && _caret.Column + 1 < line.Length ? 2 : 1;
                to = new TextPos(_caret.Line, _caret.Column + n);
            }
            else if (_caret.Line + 1 < Document.LineCount) to = new TextPos(_caret.Line + 1, 0);
            else to = _caret;
            _caret = _anchor = ReplaceRange(step, _caret, to, "");
        }
        Commit(step);
    }

    private (int First, int Last) SelectedLines()
    {
        var s = TextPos.Min(_caret, _anchor);
        var e = TextPos.Max(_caret, _anchor);
        int last = e.Line;
        if (e.Column == 0 && e.Line > s.Line) last--;
        return (s.Line, last);
    }

    /// <summary>Tab: indents the selected lines, or inserts spaces up to the next indentation stop.</summary>
    public void Indent()
    {
        var (first, last) = SelectedLines();
        if (!HasSelection || (first == last && !(TextPos.Min(_caret, _anchor).Column == 0 && TextPos.Max(_caret, _anchor).Column == Document[first].Length && Document[first].Length > 0)))
        {
            int spaces = TextDocument.IndentSize - _caret.Column % TextDocument.IndentSize;
            InsertText(new string(' ', spaces));
            return;
        }
        var step = Begin();
        for (int l = first; l <= last; l++)
            if (Document[l].Length > 0) ReplaceRange(step, new TextPos(l, 0), new TextPos(l, 0), new string(' ', TextDocument.IndentSize));
        ShiftSelection(first, last, TextDocument.IndentSize);
        Commit(step);
    }

    /// <summary>Shift+Tab: removes one level of indentation from the selected lines.</summary>
    public void Outdent()
    {
        var (first, last) = SelectedLines();
        var step = Begin();
        var removedPerLine = new Dictionary<int, int>();
        for (int l = first; l <= last; l++)
        {
            int n = Math.Min(TextDocument.IndentSize, Document.IndentOf(l));
            if (n == 0) continue;
            removedPerLine[l] = n;
            ReplaceRange(step, new TextPos(l, 0), new TextPos(l, n), "");
        }
        TextPos Fix(TextPos p) => removedPerLine.TryGetValue(p.Line, out var n) ? new TextPos(p.Line, Math.Max(0, p.Column - n)) : p;
        _caret = Fix(_caret);
        _anchor = Fix(_anchor);
        Commit(step);
    }

    private void ShiftSelection(int first, int last, int by)
    {
        TextPos Fix(TextPos p) => p.Line >= first && p.Line <= last && (p.Column > 0 || !HasSelection) && Document[p.Line].Length > 0
            ? new TextPos(p.Line, p.Column + by)
            : p;
        bool caretFirst = _caret < _anchor;
        var s = TextPos.Min(_caret, _anchor);
        var e = TextPos.Max(_caret, _anchor);
        s = s.Column == 0 ? s : Fix(s);
        e = e.Column == 0 && e.Line > last ? e : Fix(e);
        if (caretFirst) { _caret = s; _anchor = e; }
        else { _caret = e; _anchor = s; }
    }

    /// <summary>Ctrl+/: comments the selected lines out with "# ", or removes the comment marks when all have them.</summary>
    public void ToggleComment()
    {
        var (first, last) = SelectedLines();
        var lines = Enumerable.Range(first, last - first + 1).Where(l => Document[l].Trim().Length > 0).ToList();
        if (lines.Count == 0) return;
        bool allCommented = lines.All(l => Document[l].TrimStart().StartsWith('#'));
        int column = lines.Min(l => Document.IndentOf(l));
        var step = Begin();
        var delta = new Dictionary<int, (int At, int By)>();
        foreach (var l in lines)
        {
            int indent = Document.IndentOf(l);
            if (allCommented)
            {
                int n = Document[l].Length > indent + 1 && Document[l][indent + 1] == ' ' ? 2 : 1;
                ReplaceRange(step, new TextPos(l, indent), new TextPos(l, indent + n), "");
                delta[l] = (indent, -n);
            }
            else
            {
                ReplaceRange(step, new TextPos(l, column), new TextPos(l, column), "# ");
                delta[l] = (column, 2);
            }
        }
        TextPos Fix(TextPos p) => delta.TryGetValue(p.Line, out var d) && p.Column >= d.At ? new TextPos(p.Line, Math.Max(d.At, p.Column + d.By)) : p;
        _caret = Document.Clamp(Fix(_caret));
        _anchor = Document.Clamp(Fix(_anchor));
        Commit(step);
    }

    /// <summary>Ctrl+D: duplicates the current line (or the selected lines) below.</summary>
    public void DuplicateLines()
    {
        var (first, last) = SelectedLines();
        var block = Document.GetText(new TextPos(first, 0), new TextPos(last, Document[last].Length));
        var step = Begin();
        ReplaceRange(step, new TextPos(last, Document[last].Length), new TextPos(last, Document[last].Length), "\n" + block);
        int shift = last - first + 1;
        _caret = new TextPos(_caret.Line + shift, _caret.Column);
        _anchor = new TextPos(_anchor.Line + shift, _anchor.Column);
        Commit(step);
    }

    // ------------------------------------------------------------------ clipboard

    /// <summary>Copies the selection (or the current line) to the clipboard.</summary>
    public async void Copy()
    {
        var text = HasSelection ? SelectedText : Document[_caret.Line] + "\n";
        await SetClipboard(text);
    }

    /// <summary>Cuts the selection (or the current line) to the clipboard.</summary>
    public async void Cut()
    {
        string text;
        var step = Begin();
        if (HasSelection)
        {
            text = SelectedText;
            _caret = _anchor = ReplaceRange(step, _anchor, _caret, "");
        }
        else
        {
            int l = _caret.Line;
            text = Document[l] + "\n";
            var to = l + 1 < Document.LineCount ? new TextPos(l + 1, 0) : new TextPos(l, Document[l].Length);
            var from = l + 1 < Document.LineCount || l == 0 ? new TextPos(l, 0) : new TextPos(l - 1, Document[l - 1].Length);
            _caret = _anchor = ReplaceRange(step, from, to, "");
        }
        Commit(step);
        await SetClipboard(text);
    }

    /// <summary>Pastes the clipboard text.</summary>
    public async void Paste()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        string? text;
        try
        {
            text = await clipboard.TryGetTextAsync();
        }
        catch (Exception e) when (e is InvalidOperationException or TimeoutException or System.Runtime.InteropServices.COMException)
        {
            return;
        }
        if (!string.IsNullOrEmpty(text)) InsertText(text);
    }

    private async Task SetClipboard(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        try
        {
            await ClipboardExtensions.SetTextAsync(clipboard, text);
        }
        catch (Exception e) when (e is InvalidOperationException or TimeoutException or System.Runtime.InteropServices.COMException)
        {
            // The clipboard can be busy; copying is best effort.
        }
    }
}

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Calcy.Modes.Code.Editor;
using Calcy.UI.TestKit;

namespace Calcy.Modes.Code.Tests;

public class EditorTests
{
    private static (Window Window, CodeEditor Editor) NewEditor(string text = "")
    {
        var editor = new CodeEditor { Text = text };
        var w = Headless.Show(editor, 900, 600);
        editor.FocusEditor();
        Headless.Pump();
        return (w, editor);
    }

    private static void Type(Window w, string text)
    {
        foreach (var ch in text)
        {
            if (ch == '\n') w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            else w.KeyTextInput(ch.ToString());
        }
        Headless.Pump(1);
    }

    [Fact]
    public void TypingAutoIndentsAfterColonsAndDedentsAfterReturn()
    {
        Headless.Run(() =>
        {
            var (w, e) = NewEditor();
            Type(w, "def f(x):\nif x > 0:\nreturn x\nreturn -x\n");
            Assert.Equal("def f(x):\n    if x > 0:\n        return x\n    return -x\n", e.Text);
            Assert.Equal(new TextPos(4, 0), e.Caret);
        });
    }

    [Fact]
    public void ElseMovesBackToItsIf()
    {
        Headless.Run(() =>
        {
            var (w, e) = NewEditor();
            Type(w, "if a:\nb = 1\nelse:\nb = 2");
            Assert.Equal("if a:\n    b = 1\nelse:\n    b = 2", e.Text);
        });
    }

    [Fact]
    public void BackspaceRemovesOneIndentLevel()
    {
        Headless.Run(() =>
        {
            var (w, e) = NewEditor("for i in x:\n        ");
            e.SetCaret(new TextPos(1, 8));
            w.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            Assert.Equal("for i in x:\n    ", e.Text);
            w.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            w.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            Assert.Equal("for i in x:", e.Text);
        });
    }

    [Fact]
    public void TabAndShiftTabIndentLinesAndUndoRestores()
    {
        Headless.Run(() =>
        {
            var (w, e) = NewEditor("a = 1\nb = 2\nc = 3");
            w.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.Equal("    a = 1\nb = 2\nc = 3", e.Text);
            e.Select(new TextPos(0, 0), new TextPos(2, 5));
            w.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.Equal("        a = 1\n    b = 2\n    c = 3", e.Text);
            w.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
            w.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
            Assert.Equal("a = 1\nb = 2\nc = 3", e.Text);
            Assert.True(e.HasSelection);
            w.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
            Assert.Equal("    a = 1\nb = 2\nc = 3", e.Text);
            w.KeyPress(Key.Y, RawInputModifiers.Control, PhysicalKey.Y, "y");
            Assert.Equal("a = 1\nb = 2\nc = 3", e.Text);
        });
    }

    [Fact]
    public void CommentToggleAddsAndRemovesMarks()
    {
        Headless.Run(() =>
        {
            var (w, e) = NewEditor("def f():\n    x = 1\n\n    return x");
            e.Select(new TextPos(1, 0), new TextPos(3, 3));
            w.KeyPress(Key.Oem2, RawInputModifiers.Control, PhysicalKey.Slash, "/");
            Assert.Equal("def f():\n    # x = 1\n\n    # return x", e.Text);
            w.KeyPress(Key.Oem2, RawInputModifiers.Control, PhysicalKey.Slash, "/");
            Assert.Equal("def f():\n    x = 1\n\n    return x", e.Text);
        });
    }

    [Fact]
    public void TypingIsUndoneAsOneWordAtATime()
    {
        Headless.Run(() =>
        {
            var (w, e) = NewEditor();
            Type(w, "print");
            Type(w, "(1)");
            Assert.Equal("print(1)", e.Text);
            Assert.True(e.Undo());
            Assert.Equal("", e.Text);
            Assert.True(e.Redo());
            Assert.Equal("print(1)", e.Text);
        });
    }

    [Fact]
    public void HighlightingSpans()
    {
        var spans = PythonHighlighter.Tokenize("def area(r):  # circle", LexState.Start).Tokens;
        Assert.Equal([
            new Token(0, 3, TokenKind.Keyword),
            new Token(4, 4, TokenKind.Definition),
            new Token(8, 1, TokenKind.Bracket),
            new Token(9, 1, TokenKind.Text),
            new Token(10, 1, TokenKind.Bracket),
            new Token(11, 1, TokenKind.Text),
            new Token(14, 8, TokenKind.Comment),
        ], spans);
        var line = "x = 3.5e2 + len('a#b') + f\"{y}\" + 0x1F";
        var kinds = PythonHighlighter.Tokenize(line, LexState.Start).Tokens.Select(t => (line.Substring(t.Start, t.Length), t.Kind)).ToList();
        Assert.Contains(("3.5e2", TokenKind.Number), kinds);
        Assert.Contains(("len", TokenKind.Builtin), kinds);
        Assert.Contains(("'a#b'", TokenKind.String), kinds);
        Assert.Contains(("f\"{y}\"", TokenKind.String), kinds);
        Assert.Contains(("0x1F", TokenKind.Number), kinds);
        Assert.DoesNotContain(kinds, k => k.Kind == TokenKind.Comment);
        var (open, state) = PythonHighlighter.Tokenize("s = \"\"\"start", LexState.Start);
        Assert.Equal("\"\"\"", state.OpenQuote);
        Assert.Equal(TokenKind.String, open[^1].Kind);
        var (inside, inState) = PythonHighlighter.Tokenize("still # not a comment", state);
        Assert.Equal([new Token(0, 21, TokenKind.String)], inside);
        Assert.Equal(state, inState);
        var (end, endState) = PythonHighlighter.Tokenize("end\"\"\" + 1", state);
        Assert.Null(endState.OpenQuote);
        Assert.Equal(TokenKind.Number, end[^1].Kind);
        Assert.Equal(TokenKind.Decorator, PythonHighlighter.Tokenize("@calc.function", LexState.Start).Tokens[0].Kind);
        Assert.Equal("G4", CodeEditor.ColorTokenOf(TokenKind.Keyword));
        Assert.Equal("Muted", CodeEditor.ColorTokenOf(TokenKind.Comment));
    }

    [Fact]
    public void EditorTokensFollowMultiLineStrings()
    {
        Headless.Run(() =>
        {
            var (_, e) = NewEditor("x = '''\nfor\n'''\nfor i in y: pass");
            Assert.Equal(TokenKind.String, e.TokensOf(1)[0].Kind);
            Assert.Equal(TokenKind.Keyword, e.TokensOf(3)[0].Kind);
        });
    }

    [Fact]
    public void BracketsAreMatchedAcrossLinesButNotInStrings()
    {
        Headless.Run(() =>
        {
            var (_, e) = NewEditor("f(a, [1, ')'],\n  (b))");
            e.SetCaret(new TextPos(0, 2));
            var pair = e.BracketPair();
            Assert.NotNull(pair);
            Assert.Equal(new TextPos(0, 1), pair!.Value.Bracket);
            Assert.Equal(new TextPos(1, 5), pair.Value.Match);
            e.SetCaret(new TextPos(0, 5));
            Assert.Equal(new TextPos(0, 12), e.BracketPair()!.Value.Match);
        });
    }

    [Fact]
    public void FindAndReplace()
    {
        Headless.Run(() =>
        {
            var (_, e) = NewEditor("total = 0\nfor n in range(5):\n    total += n\nprint(Total)");
            e.FindText = "total";
            Assert.Equal(3, e.FindMatchCount);
            e.FindMatchCase = true;
            Assert.Equal(2, e.FindMatchCount);
            Assert.True(e.FindNext());
            Assert.Equal("total", e.SelectedText);
            Assert.Equal(0, e.Caret.Line);
            Assert.True(e.FindNext());
            Assert.Equal(2, e.Caret.Line);
            Assert.True(e.FindPrevious());
            Assert.Equal(0, e.Caret.Line);
            Assert.Equal(2, e.ReplaceAll("sum_"));
            Assert.Equal("sum_ = 0\nfor n in range(5):\n    sum_ += n\nprint(Total)", e.Text);
        });
    }

    [Fact]
    public void ErrorMarkerIsSetAndClearedByEditing()
    {
        Headless.Run(() =>
        {
            var (w, e) = NewEditor("a = 1\nb = 1 / 0");
            e.SetError(2, null, "ZeroDivisionError: division by zero");
            Assert.Equal(2, e.ErrorLine);
            Assert.Equal("ZeroDivisionError: division by zero", e.ErrorMessage);
            Headless.Pump();
            Type(w, "x");
            Assert.Null(e.ErrorLine);
        });
    }

    [Fact]
    public void MouseClickPlacesTheCaretAndDoubleClickSelectsAWord()
    {
        Headless.Run(() =>
        {
            var (w, e) = NewEditor("alpha beta gamma\nsecond line");
            Headless.Pump();
            var origin = e.Surface.TranslatePoint(new Point(0, 0), w)!.Value;
            // Line 2 (index 1) is at y = 10 + 24 + 12; columns start after the gutter.
            var p = new Point(origin.X + 400, origin.Y + 10 + 24 + 12);
            w.MouseDown(p, MouseButton.Left, RawInputModifiers.None);
            w.MouseUp(p, MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(1, e.Caret.Line);
            Assert.Equal("second line".Length, e.Caret.Column);
            var gutter = new Point(origin.X + 10, origin.Y + 10 + 12);
            w.MouseDown(gutter, MouseButton.Left, RawInputModifiers.None);
            w.MouseUp(gutter, MouseButton.Left, RawInputModifiers.None);
            Assert.Equal("alpha beta gamma\n", e.SelectedText);
        });
    }

    [Fact]
    public void LargeFilesStayResponsive()
    {
        Headless.Run(() =>
        {
            var lines = Enumerable.Range(0, 5000).Select(i => i % 7 == 0 ? $"def f{i}(x):  # function {i}" : $"    y = x * {i} + len('text {i}')");
            var (w, e) = NewEditor(string.Join("\n", lines));
            Headless.Pump();
            var sw = Stopwatch.StartNew();
            e.SetCaret(new TextPos(4990, 0));
            Headless.Pump();
            for (int i = 0; i < 20; i++) w.KeyTextInput("z");
            Headless.Pump();
            sw.Stop();
            Assert.Equal(5000, e.Document.LineCount);
            Assert.StartsWith("zzzz", e.Document[4990]);
            Assert.True(sw.ElapsedMilliseconds < 3000, $"took {sw.ElapsedMilliseconds} ms");
        });
    }

    [Fact]
    public void DocumentNormalisesTabsAndLineBreaks()
    {
        var d = new TextDocument { Text = "a\r\n\tb\rc" };
        Assert.Equal("a\n    b\nc", d.Text);
        Assert.Equal(3, d.LineCount);
        var end = d.Replace(new TextPos(1, 4), new TextPos(2, 0), "X\nY");
        Assert.Equal("a\n    X\nYc", d.Text);
        Assert.Equal(new TextPos(2, 1), end);
        Assert.Equal(8, d.OffsetOf(new TextPos(2, 0)));
        Assert.Equal(new TextPos(2, 1), d.PositionAt(d.OffsetOf(new TextPos(2, 1))));
    }
}

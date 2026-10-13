using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Calcy.Core;
using Calcy.UI.Math.Model;

namespace Calcy.UI.Math.Tests;

/// <summary>Behaviour of the controls: properties, events, automation and the clipboard.</summary>
public class ControlTests
{
    [Fact]
    public void TextIsTwoWay()
    {
        UI.Run(() =>
        {
            var editor = new MathEditor();
            var changes = new List<string>();
            editor.TextChanged += (_, e) => changes.Add(e.NewText);
            editor.Text = "(a+b)/2";
            Assert.IsType<MathTemplate>(editor.EditorState.Root[0]);
            editor.InsertText("+1");
            Assert.Equal("(a+b)/2+1", editor.Text);
            Assert.Equal(["(a+b)/2", "(a+b)/2+1"], changes);
            editor.Clear();
            Assert.Equal("", editor.Text);
        });
    }

    [Fact]
    public void CaretSlotChangedCarriesTheHint()
    {
        UI.Run(() =>
        {
            var editor = new MathEditor();
            var hints = new List<string?>();
            editor.CaretSlotChanged += (_, e) => hints.Add(e.Hint);
            editor.InsertTemplate(MathTemplateKind.Fraction);
            editor.EditorState.NextSlot();
            editor.EditorState.NextSlot();
            Assert.Equal(["Typing the numerator. Tab: next slot.", "Typing the denominator. Tab: leave the fraction.", null], hints);
            Assert.Null(editor.HintText);
        });
    }

    [Fact]
    public void SymbolCatalogEntriesInsertSomethingThatParses()
    {
        UI.Run(() =>
        {
            foreach (var group in SymbolCatalog.Groups)
            {
                Assert.NotEmpty(group.Symbols);
                foreach (var symbol in group.Symbols)
                {
                    var editor = new MathEditor();
                    editor.InsertSymbol(symbol);
                    Assert.True(editor.Text.Length > 0 || symbol.Template is not null, $"{group.Name} {symbol.Name}");
                }
            }
            Assert.Equal(["Basic", "Greek", "Calculus", "Logic", "Sets", "Units", "Constants"], SymbolCatalog.Groups.Select(g => g.Name));
            var pi = new MathEditor();
            pi.InsertSymbol(SymbolCatalog.Constants.Symbols.First(s => s.Name == "pi"));
            pi.InsertText("r");
            Assert.Equal("pi*r", pi.Text);
            var sin = new MathEditor();
            sin.InsertSymbol(SymbolCatalog.Basic.Symbols.First(s => s.Glyph == "sin"));
            sin.InsertText("x");
            Assert.Equal("sin(x)", sin.Text);
        });
    }

    [Fact]
    public void TemplateCatalogCoversEveryKind()
    {
        foreach (var kind in Enum.GetValues<MathTemplateKind>())
        {
            var info = MathTemplateCatalog.Get(kind);
            Assert.False(string.IsNullOrEmpty(info.Glyph));
            Assert.False(string.IsNullOrEmpty(info.ToolTip));
            Assert.NotEmpty(info.SlotNames);
            var s = new MathEditorState();
            s.InsertTemplate(kind);
            Assert.False(s.IsEmpty);
        }
    }

    [Fact]
    public void AutomationExposesTheLinearText()
    {
        UI.Run(() =>
        {
            var editor = new MathEditor { Text = "x^2", PlaceholderText = "Input" };
            var window = new Window { Content = editor };
            window.Show();
            var peer = ControlAutomationPeer.CreatePeerForElement(editor);
            Assert.Equal(AutomationControlType.Edit, peer.GetAutomationControlType());
            var value = peer.GetProvider<IValueProvider>()!;
            Assert.Equal("x^2", value.Value);
            value.SetValue("1/2");
            Assert.Equal("1/2", editor.Text);
            Assert.Equal("Input", peer.GetName());

            var view = new MathView { Text = "x^2 + 1" };
            var viewPeer = ControlAutomationPeer.CreatePeerForElement(view);
            Assert.Equal("x^2 + 1", viewPeer.GetName());
            window.Close();
        });
    }

    [Fact]
    public void KeyboardShortcutsUndoAndSelectAll()
    {
        UI.Run(() =>
        {
            var editor = new MathEditor { Width = 300 };
            var window = new Window { SizeToContent = SizeToContent.Height, Width = 320, Content = editor };
            window.Show();
            editor.Focus();
            window.KeyTextInput("x+1");
            window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
            Assert.Equal("", editor.Text);
            window.KeyPress(Key.Y, RawInputModifiers.Control, PhysicalKey.Y, "y");
            Assert.Equal("x+1", editor.Text);
            window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
            window.KeyTextInput("/");
            window.KeyTextInput("2");
            Assert.Equal("(x+1)/2", editor.Text);
            window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            Assert.Equal("x+1", editor.Text);
            window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
            window.KeyPress(Key.Right, RawInputModifiers.Shift, PhysicalKey.ArrowRight, null);
            Assert.Equal("x", editor.EditorState.GetSelectedText());
            window.Close();
        });
    }

    [Fact]
    public void CopyAndPasteThroughTheClipboard()
    {
        UI.Run(() =>
        {
            var a = new MathEditor { Text = "sqrt(x)/2", Width = 300 };
            var b = new MathEditor { Width = 300 };
            var window = new Window { SizeToContent = SizeToContent.Height, Width = 320, Content = new StackPanel { Children = { a, b } } };
            window.Show();
            Assert.NotNull(TopLevel.GetTopLevel(a)?.Clipboard);
            a.SelectAll();
            var copy = a.CopyAsync();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            copy.GetAwaiter().GetResult();
            b.Focus();
            var paste = b.PasteAsync();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            paste.GetAwaiter().GetResult();
            Assert.Equal("sqrt(x)/2", b.Text);
            Assert.IsType<MathTemplate>(b.EditorState.Root[0]);
            window.Close();
        });
    }

    [Fact]
    public void MouseDragSelectsAndArrowKeysMoveVertically()
    {
        UI.Run(() =>
        {
            var editor = new MathEditor { Text = "a+12/34+c", FontSize = 30, Width = 400 };
            var window = new Window { SizeToContent = SizeToContent.Height, Width = 420, Content = editor };
            window.Show();
            window.CaptureRenderedFrame();
            var origin = editor.TranslatePoint(new Point(0, 0), window)!.Value;
            var m = typeof(MathEditor).GetMethod("ContentOrigin", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var content = (Point)m.Invoke(editor, null)!;
            var root = editor.CurrentLayout!.RootRow;
            Point At(double x) => new(origin.X + content.X + x, origin.Y + content.Y + root.Origin.Y - 5);
            window.MouseDown(At(root.CaretX[0] + 1), MouseButton.Left, RawInputModifiers.None);
            window.MouseMove(At(root.CaretX[3] + 1), RawInputModifiers.LeftMouseButton);
            window.MouseUp(At(root.CaretX[3] + 1), MouseButton.Left, RawInputModifiers.None);
            Assert.Equal("a+12/34", editor.EditorState.GetSelectedText());

            var frac = (Calcy.UI.Math.Model.MathTemplate)editor.EditorState.Root[2];
            editor.EditorState.SetCaret(new MathCaret(frac[0], 1));
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.Same(frac[1], editor.EditorState.Caret.Row);
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.Same(frac[0], editor.EditorState.Caret.Row);
            window.Close();
        });
    }

    [Fact]
    public void LongContentScrollsToKeepTheCaretVisible()
    {
        UI.Run(() =>
        {
            var editor = new MathEditor { Width = 200, FontSize = 24 };
            var window = new Window { SizeToContent = SizeToContent.Height, Width = 220, Content = editor };
            window.Show();
            editor.Focus();
            window.KeyTextInput("1+2+3+4+5+6+7+8+9+10+11+12+13+14");
            var frame = window.CaptureRenderedFrame()!;
            frame.Save(Path.Combine(UI.OutputFolder, "editor_scrolled.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            var m = typeof(MathEditor).GetMethod("ContentOrigin", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var origin = (Point)m.Invoke(editor, null)!;
            Assert.True(origin.X < 0, "content scrolled left");
            Assert.True(editor.Bounds.Width <= 200.5);
            window.Close();
        });
    }

    [Fact]
    public void MathViewUsesTheSessionAndCanPreserveStructure()
    {
        UI.Run(() =>
        {
            var session = new CalcSession();
            session.Evaluate("f(x) = x^2");
            var view = new MathView { Session = session, Text = "f(2) + x + x^2", PreserveStructure = true };
            UI.Capture(view, "view_preserved");
            Assert.Null(view.ErrorMessage);
            var root = view.CurrentLayout!.RootRow.Row;
            Assert.Equal("f(2)+x+x^2", MathSerializer.Serialize(root, session));

            var sorted = new MathView { Text = "x + x^2" };
            UI.Capture(sorted, "view_sorted");
            Assert.Equal("x^2+x", MathSerializer.Serialize(sorted.CurrentLayout!.RootRow.Row));

            var bad = new MathView { Text = "2 + * 3" };
            UI.Capture(bad, "view_error");
            Assert.NotNull(bad.ErrorMessage);
        });
    }

    [Fact]
    public void AlignmentMovesTheContent()
    {
        UI.Run(() =>
        {
            var left = new MathView { Text = "x", FontSize = 30, Width = 300, TextAlignment = Avalonia.Media.TextAlignment.Left };
            var right = new MathView { Text = "x", FontSize = 30, Width = 300, TextAlignment = Avalonia.Media.TextAlignment.Right };
            var l = UI.Capture(left);
            var r = UI.Capture(right);
            Assert.True(FirstInkColumn(l) < 100);
            Assert.True(FirstInkColumn(r) > 200);
        });
    }

    private static unsafe int FirstInkColumn(Avalonia.Media.Imaging.WriteableBitmap bmp)
    {
        using var fb = bmp.Lock();
        for (int x = 0; x < fb.Size.Width; x++)
            for (int y = 0; y < fb.Size.Height; y++)
            {
                var p = (byte*)fb.Address + y * fb.RowBytes + x * 4;
                if (p[0] < 128 && p[1] < 128 && p[2] < 128) return x;
            }
        return -1;
    }
}

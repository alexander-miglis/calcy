using System.Text;
using Avalonia;
using Calcy.Core.Parsing;

namespace Calcy.UI.Math.Model;

/// <summary>A caret position: before item <see cref="Index"/> of <see cref="Row"/>.</summary>
public readonly record struct MathCaret(MathRow Row, int Index);

/// <summary>A selected range of items in one row.</summary>
public readonly record struct MathSelection(MathRow Row, int Start, int End)
{
    /// <summary>Number of selected items.</summary>
    public int Length => End - Start;
}

/// <summary>Layout information the editor needs for vertical caret movement.</summary>
public interface IMathCaretGeometry
{
    /// <summary>Bounds of a laid-out row in a common coordinate space.</summary>
    bool TryGetRowBounds(MathRow row, out Rect bounds);

    /// <summary>Horizontal caret position before item <paramref name="index"/> of <paramref name="row"/>.</summary>
    double GetCaretX(MathRow row, int index);

    /// <summary>The caret index in <paramref name="row"/> nearest to <paramref name="x"/>.</summary>
    int NearestIndex(MathRow row, double x);
}

/// <summary>
/// The editing model behind <c>MathEditor</c>: a tree of rows and templates, a caret, a selection, typing rules
/// (/ makes a fraction, ^ a power, sqrt a root...), navigation, and undo/redo. It has no UI dependencies.
/// </summary>
public sealed class MathEditorState
{
    private readonly List<Snapshot> _undo = [];
    private readonly List<Snapshot> _redo = [];
    private string? _lastEditKind;
    private (MathTemplate Template, string Word, MathRow Slot)? _freshAuto;
    private MathCaret? _anchor;
    private const int MaxUndo = 200;

    /// <summary>Creates an empty editor model.</summary>
    public MathEditorState(IParseContext? parseContext = null)
    {
        ParseContext = parseContext;
        Root = new MathRow();
        Caret = new MathCaret(Root, 0);
    }

    /// <summary>Raised when the content changes.</summary>
    public event EventHandler? ContentChanged;

    /// <summary>Raised when the caret or the selection moves.</summary>
    public event EventHandler? CaretChanged;

    /// <summary>Decides which names are functions when parsing and serializing. Null uses the default list.</summary>
    public IParseContext? ParseContext { get; set; }

    /// <summary>The root row.</summary>
    public MathRow Root { get; private set; }

    /// <summary>The caret.</summary>
    public MathCaret Caret { get; private set; }

    /// <summary>The selection anchor, or null when nothing is being selected.</summary>
    public MathCaret? SelectionAnchor => _anchor;

    /// <summary>True when the model has no content.</summary>
    public bool IsEmpty => Root.IsEmpty;

    /// <summary>The content as linear Calcy syntax.</summary>
    public string Text => MathSerializer.Serialize(Root, ParseContext);

    /// <summary>True when there is something to undo.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>True when there is something to redo.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>The selected range (lifted to the row that contains both ends), or null.</summary>
    public MathSelection? Selection
    {
        get
        {
            if (_anchor is not { } a) return null;
            var c = Caret;
            var row = CommonRow(a.Row, c.Row);
            var (alo, ahi) = Lift(a, row);
            var (clo, chi) = Lift(c, row);
            int s = System.Math.Min(alo, clo), e = System.Math.Max(ahi, chi);
            return s == e ? null : new MathSelection(row, s, e);
        }
    }

    /// <summary>True when a non-empty range is selected.</summary>
    public bool HasSelection => Selection is not null;

    /// <summary>The template whose slot holds the caret, or null at the top level.</summary>
    public MathTemplate? CurrentTemplate => Caret.Row.Owner;

    /// <summary>A short hint about the slot that holds the caret, such as "Typing the upper bound. Tab: next slot."</summary>
    public string? CurrentHint => HintFor(Caret.Row);

    // ================================================================== public editing API

    /// <summary>Replaces the content by parsing linear text; text that does not parse is kept as plain characters.</summary>
    /// <returns>True when the text parsed (or was empty).</returns>
    public bool SetText(string? text, bool undoable = true)
    {
        bool ok = true;
        Mutate("set", () =>
        {
            ok = MathInputBuilder.TryBuild(text ?? "", ParseContext, out var row);
            Root = row;
            Caret = new MathCaret(Root, Root.Count);
            _anchor = null;
        }, undoable && !Root.IsEmpty);
        return ok;
    }

    /// <summary>Removes all content.</summary>
    public void Clear() => Mutate("clear", () =>
    {
        Root.Clear();
        Caret = new MathCaret(Root, 0);
        _anchor = null;
    });

    /// <summary>Types text as if it came from the keyboard (used by symbol buttons: "sin(" gives sin with brackets).</summary>
    public void TypeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        bool simple = text.Length == 1 && (char.IsLetterOrDigit(text[0]) || text[0] == '.');
        Mutate(simple ? "type" : "insert", () =>
        {
            var e = System.Globalization.StringInfo.GetTextElementEnumerator(text);
            while (e.MoveNext()) TypeCore((string)e.Current);
        });
    }

    /// <summary>Inserts linear text with its structure (paste): "(a+b)/2" becomes a fraction. Falls back to typing.</summary>
    public void Paste(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        text = text.Replace("\r", "").Replace("\n", " ").Trim();
        Mutate("paste", () =>
        {
            ResolvePendingWord();
            DeleteSelectionCore();
            var st = MathParsing.TryParseStatement(text, ParseContext, out _);
            if (st is null)
            {
                var e = System.Globalization.StringInfo.GetTextElementEnumerator(text);
                while (e.MoveNext()) TypeCore((string)e.Current);
                return;
            }
            var row = MathInputBuilder.FromStatement(st, ParseContext);
            var items = row.RemoveRange(0, row.Count);
            int at = Caret.Index;
            Caret.Row.InsertRange(at, items);
            Caret = new MathCaret(Caret.Row, at + items.Count);
        });
    }

    /// <summary>Inserts a template at the caret (the selection, if any, goes into its main slot).</summary>
    public void InsertTemplate(MathTemplateKind kind) => Mutate("template", () => InsertTemplateCore(kind, fromTyping: false));

    /// <summary>Inserts a matrix of the given size.</summary>
    public void InsertMatrix(int rows, int columns) => Mutate("template", () =>
    {
        ResolvePendingWord();
        DeleteSelectionCore();
        var t = new MathTemplate(MathTemplateKind.Matrix, System.Math.Clamp(rows, 1, 20), System.Math.Clamp(columns, 1, 20));
        InsertAndEnter(t);
    });

    /// <summary>Deletes backwards (Backspace).</summary>
    public void Backspace() => Mutate("delete", BackspaceCore);

    /// <summary>Deletes forwards (Delete).</summary>
    public void DeleteForward() => Mutate("delete", DeleteCore);

    /// <summary>Deletes the selection.</summary>
    public void DeleteSelection() => Mutate("delete", () => DeleteSelectionCore());

    /// <summary>The selection as linear text, or null when nothing is selected.</summary>
    public string? GetSelectedText()
    {
        if (Selection is not { } s) return null;
        return MathSerializer.Serialize(s.Row.Items.Skip(s.Start).Take(s.Length).ToList(), ParseContext);
    }

    /// <summary>Selects everything.</summary>
    public void SelectAll() => MoveCaret(() =>
    {
        _anchor = new MathCaret(Root, 0);
        Caret = new MathCaret(Root, Root.Count);
    }, keepSelection: true);

    /// <summary>Clears the selection without moving the caret.</summary>
    public void ClearSelection() => MoveCaret(() => _anchor = null, keepSelection: true);

    /// <summary>Moves the caret right (into templates and through their slots); extends the selection when asked.</summary>
    public void MoveRight(bool extendSelection = false) => MoveCaret(() =>
    {
        if (extendSelection) { ExtendHorizontally(+1); return; }
        if (Selection is { } sel) { Caret = new MathCaret(sel.Row, sel.End); return; }
        var (row, idx) = Caret;
        if (idx < row.Count)
        {
            if (row[idx] is MathTemplate t) Caret = new MathCaret(t[0], 0);
            else Caret = new MathCaret(row, idx + 1);
        }
        else if (row.Owner is { } owner)
        {
            int k = row.SlotIndex;
            Caret = k + 1 < owner.Slots.Count ? new MathCaret(owner[k + 1], 0) : After(owner);
        }
    }, extendSelection);

    /// <summary>Moves the caret left; extends the selection when asked.</summary>
    public void MoveLeft(bool extendSelection = false) => MoveCaret(() =>
    {
        if (extendSelection) { ExtendHorizontally(-1); return; }
        if (Selection is { } sel) { Caret = new MathCaret(sel.Row, sel.Start); return; }
        var (row, idx) = Caret;
        if (idx > 0)
        {
            if (row[idx - 1] is MathTemplate t)
            {
                var last = t[t.Slots.Count - 1];
                Caret = new MathCaret(last, last.Count);
            }
            else Caret = new MathCaret(row, idx - 1);
        }
        else if (row.Owner is { } owner)
        {
            int k = row.SlotIndex;
            Caret = k > 0 ? new MathCaret(owner[k - 1], owner[k - 1].Count) : Before(owner);
        }
    }, extendSelection);

    /// <summary>Moves to the start of the current row (or of everything).</summary>
    public void MoveHome(bool extendSelection = false, bool wholeDocument = false) => MoveCaret(() =>
    {
        if (extendSelection) _anchor ??= Caret;
        Caret = wholeDocument ? new MathCaret(Root, 0) : new MathCaret(Caret.Row, 0);
    }, extendSelection);

    /// <summary>Moves to the end of the current row (or of everything).</summary>
    public void MoveEnd(bool extendSelection = false, bool wholeDocument = false) => MoveCaret(() =>
    {
        if (extendSelection) _anchor ??= Caret;
        Caret = wholeDocument ? new MathCaret(Root, Root.Count) : new MathCaret(Caret.Row, Caret.Row.Count);
    }, extendSelection);

    /// <summary>Moves the caret up (numerator, upper bound, matrix row above). Returns false when it cannot move.</summary>
    public bool MoveUp(IMathCaretGeometry geometry) => MoveVertical(geometry, up: true);

    /// <summary>Moves the caret down (denominator, lower bound, matrix row below). Returns false when it cannot move.</summary>
    public bool MoveDown(IMathCaretGeometry geometry) => MoveVertical(geometry, up: false);

    /// <summary>Jumps to the next slot (Tab). Returns false when there is none (focus may move on).</summary>
    public bool NextSlot()
    {
        bool moved = false;
        MoveCaret(() => moved = NextSlotCore(), keepSelection: false);
        return moved;
    }

    /// <summary>Jumps to the previous slot (Shift+Tab). Returns false when there is none.</summary>
    public bool PreviousSlot()
    {
        bool moved = false;
        MoveCaret(() => moved = PreviousSlotCore(), keepSelection: false);
        return moved;
    }

    /// <summary>Places the caret in the first empty slot. Returns false (caret at the end) when there is none.</summary>
    public bool FocusFirstEmptySlot()
    {
        bool found = false;
        MoveCaret(() =>
        {
            var slot = Root.DescendantSlots().FirstOrDefault(s => s.IsEmpty);
            if (slot is null) Caret = new MathCaret(Root, Root.Count);
            else
            {
                Caret = new MathCaret(slot, 0);
                found = true;
            }
        }, keepSelection: false);
        return found;
    }

    /// <summary>Places the caret (for example after a mouse click); extends the selection when asked.</summary>
    public void SetCaret(MathCaret caret, bool extendSelection = false) => MoveCaret(() =>
    {
        if (extendSelection) _anchor ??= Caret;
        Caret = new MathCaret(caret.Row, System.Math.Clamp(caret.Index, 0, caret.Row.Count));
    }, extendSelection);

    /// <summary>Starts a selection at the caret (used when a mouse drag begins).</summary>
    public void BeginSelection() => _anchor = Caret;

    /// <summary>Adds a row to the matrix or piecewise definition around the caret.</summary>
    public bool AddGridRow() => GridEdit(t =>
    {
        int r = CellOf(t).Row;
        t.InsertGridRow(r + 1);
        Caret = new MathCaret(t.Cell(r + 1, 0), 0);
        return true;
    });

    /// <summary>Adds a column to the matrix around the caret.</summary>
    public bool AddGridColumn() => GridEdit(t =>
    {
        if (t.Kind != MathTemplateKind.Matrix) return false;
        var (r, c) = CellOf(t);
        t.InsertGridColumn(c + 1);
        Caret = new MathCaret(t.Cell(r, c + 1), 0);
        return true;
    });

    /// <summary>Removes the row of the matrix or piecewise definition that holds the caret.</summary>
    public bool RemoveGridRow() => GridEdit(t =>
    {
        var (r, c) = CellOf(t);
        if (!t.RemoveGridRow(r)) return false;
        r = System.Math.Min(r, t.Rows - 1);
        var cell = t.Cell(r, System.Math.Min(c, t.Columns - 1));
        Caret = new MathCaret(cell, cell.Count);
        return true;
    });

    /// <summary>Removes the matrix column that holds the caret.</summary>
    public bool RemoveGridColumn() => GridEdit(t =>
    {
        if (t.Kind != MathTemplateKind.Matrix) return false;
        var (r, c) = CellOf(t);
        if (!t.RemoveGridColumn(c)) return false;
        var cell = t.Cell(r, System.Math.Min(c, t.Columns - 1));
        Caret = new MathCaret(cell, cell.Count);
        return true;
    });

    /// <summary>Undoes the last edit.</summary>
    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        _redo.Add(TakeSnapshot("redo"));
        Restore(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        _lastEditKind = null;
        ContentChanged?.Invoke(this, EventArgs.Empty);
        CaretChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Redoes the last undone edit.</summary>
    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        _undo.Add(TakeSnapshot("undo"));
        Restore(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        _lastEditKind = null;
        ContentChanged?.Invoke(this, EventArgs.Empty);
        CaretChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    // ================================================================== infrastructure

    private sealed record Snapshot(MathRow Root, List<int> Caret, string Kind);

    private Snapshot TakeSnapshot(string kind) => new(Root.Clone(), PathOf(Caret), kind);

    private void Restore(Snapshot s)
    {
        Root = s.Root.Clone();
        Caret = Resolve(Root, s.Caret);
        _anchor = null;
        _freshAuto = null;
    }

    private void Mutate(string kind, Action action, bool undoable = true)
    {
        var fresh = _freshAuto;
        _freshAuto = kind is "type" or "insert" ? fresh : null;
        var before = TakeSnapshot(kind);
        string sigBefore = Signature(Root);
        var caretBefore = PathOf(Caret);
        action();
        bool changed = Signature(Root) != sigBefore;
        if (changed)
        {
            bool coalesce = kind == "type" && _lastEditKind == "type" && _undo.Count > 0;
            if (undoable && !coalesce)
            {
                _undo.Add(before);
                if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
            }
            if (undoable) _redo.Clear();
            _lastEditKind = kind;
            ContentChanged?.Invoke(this, EventArgs.Empty);
        }
        if (changed || !PathOf(Caret).SequenceEqual(caretBefore)) CaretChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MoveCaret(Action action, bool keepSelection)
    {
        var before = PathOf(Caret);
        var anchorBefore = _anchor;
        if (!keepSelection) _anchor = null;
        _freshAuto = null;
        _lastEditKind = null;
        action();
        if (!PathOf(Caret).SequenceEqual(before) || !Equals(anchorBefore, _anchor)) CaretChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool GridEdit(Func<MathTemplate, bool> edit)
    {
        var grid = Caret.Row.SelfAndAncestors().Select(r => r.Owner).FirstOrDefault(t => t is { IsGrid: true });
        if (grid is null) return false;
        bool ok = false;
        Mutate("grid", () =>
        {
            // Find the grid again: the caret row is a cell of it or nested inside one.
            ok = edit(grid);
        });
        return ok;
    }

    private (int Row, int Column) CellOf(MathTemplate grid)
    {
        var cell = Caret.Row.SelfAndAncestors().First(r => ReferenceEquals(r.Owner, grid));
        int k = cell.SlotIndex;
        return (k / grid.Columns, k % grid.Columns);
    }

    /// <summary>A compact structural signature used to detect content changes.</summary>
    internal static string Signature(MathRow row)
    {
        var sb = new StringBuilder();
        Write(row);
        return sb.ToString();

        void Write(MathRow r)
        {
            foreach (var n in r.Items)
            {
                if (n is MathChar c) sb.Append(c.Text).Append('\u0001');
                else if (n is MathTemplate t)
                {
                    sb.Append('\u0002').Append((int)t.Kind).Append(':').Append(t.Columns).Append(t.IsGhostClosed ? 'g' : 'c').Append(t.FunctionName);
                    foreach (var s in t.Slots)
                    {
                        sb.Append('\u0003');
                        Write(s);
                    }
                    sb.Append('\u0004');
                }
            }
        }
    }

    /// <summary>Path of a caret from the root: (item index, slot index) pairs and then the index.</summary>
    internal static List<int> PathOf(MathCaret c)
    {
        var path = new List<int>();
        for (var row = c.Row; row.Owner is { } owner; row = owner.Parent!)
        {
            path.Insert(0, row.SlotIndex);
            path.Insert(0, owner.IndexInParent);
            if (owner.Parent is null) break;
        }
        path.Add(c.Index);
        return path;
    }

    internal static MathCaret Resolve(MathRow root, List<int> path)
    {
        var row = root;
        for (int k = 0; k + 1 < path.Count; k += 2)
        {
            if (path[k] < 0 || path[k] >= row.Count || row[path[k]] is not MathTemplate t || path[k + 1] >= t.Slots.Count)
                return new MathCaret(row, row.Count);
            row = t[path[k + 1]];
        }
        return new MathCaret(row, System.Math.Clamp(path[^1], 0, row.Count));
    }

    /// <summary>Document-order key of a caret: item and slot indices with -1 marking the caret itself.</summary>
    private static List<int> OrderKey(MathCaret c)
    {
        var key = PathOf(c);
        key.Add(-1);
        return key;
    }

    private static int Compare(List<int> a, List<int> b)
    {
        for (int i = 0; i < System.Math.Min(a.Count, b.Count); i++)
            if (a[i] != b[i]) return a[i].CompareTo(b[i]);
        return a.Count.CompareTo(b.Count);
    }

    private static MathCaret After(MathTemplate t) => new(t.Parent!, t.IndexInParent + 1);

    private static MathCaret Before(MathTemplate t) => new(t.Parent!, t.IndexInParent);

    private static MathRow CommonRow(MathRow a, MathRow b)
    {
        var set = a.SelfAndAncestors().ToHashSet();
        foreach (var r in b.SelfAndAncestors())
            if (set.Contains(r)) return r;
        return a.Root;
    }

    private static (int Lo, int Hi) Lift(MathCaret p, MathRow row)
    {
        if (ReferenceEquals(p.Row, row)) return (p.Index, p.Index);
        var r = p.Row;
        while (!ReferenceEquals(r.ParentRow, row)) r = r.ParentRow!;
        int j = r.Owner!.IndexInParent;
        return (j, j + 1);
    }

    private void ExtendHorizontally(int dir)
    {
        _anchor ??= Caret;
        var (row, idx) = Caret;
        if (dir > 0)
        {
            if (idx < row.Count) Caret = new MathCaret(row, idx + 1);
            else if (row.Owner is { } o) Caret = After(o);
        }
        else
        {
            if (idx > 0) Caret = new MathCaret(row, idx - 1);
            else if (row.Owner is { } o) Caret = Before(o);
        }
    }

    // ================================================================== hints

    /// <summary>The hint for a slot row ("Typing the numerator. Tab: next slot."), or null for the top level.</summary>
    public static string? HintFor(MathRow row)
    {
        if (row.Owner is not { } t) return null;
        var info = MathTemplateCatalog.Get(t.Kind);
        int k = row.SlotIndex;
        string slot;
        string extra = "";
        if (t.Kind == MathTemplateKind.Matrix)
        {
            slot = $"entry in row {k / t.Columns + 1}, column {k % t.Columns + 1}";
            extra = " Comma: next column. Semicolon: next row.";
        }
        else if (t.Kind == MathTemplateKind.Piecewise)
        {
            int r = k / 2;
            slot = k % 2 == 0 ? $"value of case {r + 1}" :
                r == t.Rows - 1 && r > 0 ? $"condition of case {r + 1} (leave it empty for otherwise)" : $"condition of case {r + 1}";
        }
        else slot = k >= 0 && k < info.SlotNames.Count ? info.SlotNames[k] : "slot";
        string next = k + 1 < t.Slots.Count ? " Tab: next slot." : $" Tab: leave the {info.Name}.";
        return $"Typing the {slot}.{next}{extra}";
    }

    // ================================================================== typing

    private static bool IsAsciiLetter(MathNode? n) => n is MathChar { Text.Length: 1 } c && char.IsAsciiLetter(c.Text[0]);

    private static bool IsOperand(MathNode n) => n is MathTemplate || (n is MathChar c && MathCharInfo.IsOperandPart(c.Text));

    private void TypeCore(string c)
    {
        var fresh = _freshAuto;
        _freshAuto = null;

        if (Selection is not null)
        {
            switch (c)
            {
                case "/": WrapSelection(MathTemplateKind.Fraction); return;
                case "(": WrapSelection(MathTemplateKind.Parens); return;
                case "[": WrapSelection(MathTemplateKind.Brackets); return;
                case "{": WrapSelection(MathTemplateKind.Braces); return;
                case "|": WrapSelection(MathTemplateKind.Abs); return;
                case "√": WrapSelection(MathTemplateKind.Sqrt); return;
                case "^": WrapSelection(MathTemplateKind.Power); return;
            }
            DeleteSelectionCore();
        }

        // Subscripts only hold name characters; anything else continues after the subscript.
        if (Caret.Row.Owner is { Kind: MathTemplateKind.Subscript } sub && !(c.Length == 1 && char.IsLetterOrDigit(c[0])))
        {
            Caret = After(sub);
            if (c == " ") return;
        }

        if (c.Length == 1 && char.IsAsciiLetter(c[0]))
        {
            TypeLetter(c);
            return;
        }

        // "(" right after a word turned into a template: sqrt( keeps the root (the "(" is not needed), while
        // sum( and int( go back to letters so call syntax such as sum(list) keeps working.
        if (c == "(" && fresh is { } f && ReferenceEquals(Caret.Row, f.Slot) && f.Slot.IsEmpty && f.Template.Parent is not null)
        {
            if (f.Template.Slots.Count == 1 || f.Template.Kind == MathTemplateKind.NthRoot) return;
            var parent = f.Template.Parent;
            int at = f.Template.IndexInParent;
            parent.RemoveAt(at);
            var letters = f.Word.Select(ch => (MathNode)new MathChar(ch.ToString())).ToList();
            parent.InsertRange(at, letters);
            Caret = new MathCaret(parent, at + letters.Count);
            OpenBracket(MathTemplateKind.Parens);
            return;
        }
        if (c == "_")
        {
            if (TryLogBase()) return;
            ResolvePendingWord(c);
        }
        else if (ResolvePendingWord(c)) return; // a non-letter ends the word being typed: pi then + gives π+

        // Linear typing: x^2+1 means x² + 1 and x/2+1 means x/2 + 1, so an operator typed at the end of a filled
        // exponent or denominator continues after it (2x and other juxtapositions stay inside, as 1/2x is 1/(2x)).
        if (c is "+" or "-" or "−" or "*" or "·" or "×" or "÷" or "/" or "=" or "<" or ">" or "≤" or "≥" or "≠" or "," or ";")
        {
            while (Caret.Row.Owner is { } o && Caret.Index == Caret.Row.Count && !Caret.Row.IsEmpty
                   && (o.Kind == MathTemplateKind.Power || (o.Kind == MathTemplateKind.Fraction && Caret.Row.SlotIndex == 1)))
            {
                Caret = After(o);
            }
        }

        var (row, idx) = Caret;
        MathNode? prev = idx > 0 ? row[idx - 1] : null;
        switch (c)
        {
            case " ":
                if (row.Owner is { Kind: MathTemplateKind.Power or MathTemplateKind.Subscript } script && idx == row.Count)
                {
                    Caret = After(script);
                    return;
                }
                InsertChar(" ");
                return;
            case "/":
                TypeSlash();
                return;
            case "^":
                TypePower();
                return;
            case "_":
                TypeSubscript();
                return;
            case "(":
                OpenBracket(MathTemplateKind.Parens);
                return;
            case "[":
                OpenBracket(MathTemplateKind.Brackets);
                return;
            case "{":
                OpenBracket(MathTemplateKind.Braces);
                return;
            case ")":
                CloseBracket(MathTemplateKind.Parens);
                return;
            case "]":
                CloseBracket(MathTemplateKind.Brackets);
                return;
            case "}":
                CloseBracket(MathTemplateKind.Braces);
                return;
            case "|":
                if (row.Owner is { Kind: MathTemplateKind.Abs } abs && idx == row.Count) Caret = After(abs);
                else InsertAndEnter(new MathTemplate(MathTemplateKind.Abs));
                return;
            case "*":
                if (prev is MathChar { Text: "·" } && _lastTypedStar)
                {
                    row.RemoveAt(idx - 1);
                    Caret = new MathCaret(row, idx - 1);
                    _lastTypedStar = false;
                    TypePower();
                    return;
                }
                InsertChar("·");
                _lastTypedStar = true;
                return;
            case "√":
                InsertTemplateCore(MathTemplateKind.Sqrt, fromTyping: true);
                return;
            case "∛" or "∜":
            {
                var t = new MathTemplate(MathTemplateKind.NthRoot);
                t[0].Add(new MathChar(c == "∛" ? "3" : "4"));
                if (c == "∛") t.FunctionName = "cbrt";
                InsertAndEnter(t);
                return;
            }
            case "∫":
                InsertTemplateCore(MathTemplateKind.DefiniteIntegral, fromTyping: false);
                return;
            case "∑":
                InsertTemplateCore(MathTemplateKind.Sum, fromTyping: false);
                return;
            case "∏":
                InsertTemplateCore(MathTemplateKind.Product, fromTyping: false);
                return;
            case "−":
                InsertChar("-");
                return;
            case ",":
                if (TryGridMove(nextRow: false)) return;
                InsertChar(",");
                return;
            case ";":
                if (TryGridMove(nextRow: true)) return;
                InsertChar(";");
                return;
            case "=":
                if (prev is MathChar pe && pe.Text is "<" or ">" or "!" or ":" or "=")
                {
                    ReplacePrev(pe.Text switch { "<" => "≤", ">" => "≥", "!" => "≠", "=" => "==", _ => "≔" });
                    return;
                }
                InsertChar("=");
                return;
            case ">":
                if (prev is MathChar pg && pg.Text is "-" or "=" or "<")
                {
                    ReplacePrev(pg.Text switch { "-" => "→", "=" => "⇒", _ => "≠" });
                    return;
                }
                InsertChar(">");
                return;
        }
        if (c.Length == 1 && "⁰¹²³⁴⁵⁶⁷⁸⁹⁻".Contains(c[0]))
        {
            TypeSuperscriptChar(c[0]);
            return;
        }
        _lastTypedStar = false;
        InsertChar(c);
    }

    private bool _lastTypedStar;

    private void InsertChar(string text)
    {
        if (text != "·") _lastTypedStar = false;
        var (row, idx) = Caret;
        row.Insert(idx, new MathChar(text));
        Caret = new MathCaret(row, idx + 1);
    }

    private void ReplacePrev(string text)
    {
        var (row, idx) = Caret;
        row.RemoveAt(idx - 1);
        row.Insert(idx - 1, new MathChar(text));
    }

    private void TypeLetter(string letter)
    {
        _lastTypedStar = false;
        var (row, idx) = Caret;
        row.Insert(idx, new MathChar(letter));
        Caret = new MathCaret(row, idx + 1);

        var run = LetterRunBeforeCaret(out int runStart);
        // Longest suffix (two letters or more) that could still become a known word.
        for (int len = run.Length; len >= 2; len--)
        {
            string s = run[^len..];
            if (!IsWordPrefix(s)) continue;
            if (MathAutoCommands.ByName.TryGetValue(s, out var cmd) && CanConvert(cmd, len == run.Length) && !IsProperPrefixOfWord(s))
                Convert(cmd, len);
            return;
        }
        if (run.Length < 2) return;
        // The new letter ended every word that was being typed: settle the earlier letters first.
        row.RemoveAt(Caret.Index - 1);
        Caret = new MathCaret(row, Caret.Index - 1);
        bool function = ResolveRun(run[..^1], out bool converted);
        if (function && !converted) InsertChar(" ");
        var (r2, i2) = Caret;
        r2.Insert(i2, new MathChar(letter));
        Caret = new MathCaret(r2, i2 + 1);
    }

    private string LetterRunBeforeCaret(out int start)
    {
        var (row, idx) = Caret;
        int j = idx;
        while (j > 0 && IsAsciiLetter(row[j - 1])) j--;
        start = j;
        var sb = new StringBuilder();
        for (int k = j; k < idx; k++) sb.Append(((MathChar)row[k]).Text);
        return sb.ToString();
    }

    private IEnumerable<string> Dictionary()
    {
        foreach (var c in MathAutoCommands.All) yield return c.Name;
        foreach (var f in MathParsing.FunctionNamesOf(ParseContext)) yield return f;
    }

    private bool IsWordPrefix(string s) => Dictionary().Any(w => w.StartsWith(s, StringComparison.Ordinal));

    private bool IsProperPrefixOfWord(string s) => Dictionary().Any(w => w.Length > s.Length && w.StartsWith(s, StringComparison.Ordinal));

    private bool IsFunctionWord(string s) => MathParsing.FunctionNamesOf(ParseContext).Contains(s);

    private static bool CanConvert(MathAutoCommand cmd, bool atRunStart) => !cmd.RequiresWordStart || atRunStart;

    /// <summary>
    /// Settles the word before the caret when a non-letter is typed (pi then + gives π+). Returns true when the key
    /// was used up: a space that committed the word, or "(" after sqrt or abs (the template already brackets).
    /// Before "(" the letters of sum, int, lim and other multi-slot templates stay, so sum(list) keeps working.
    /// </summary>
    private bool ResolvePendingWord(string? trigger = null)
    {
        var run = LetterRunBeforeCaret(out _);
        if (run.Length < 2) return false;
        var (cmd, len, _) = FindResolution(run);
        if (cmd is null) return false;
        if (trigger == "(" && cmd.Kind == MathAutoCommandKind.Template)
        {
            bool singleSlot = cmd.Template is MathTemplateKind.Sqrt or MathTemplateKind.Abs || cmd.Name == "cbrt";
            if (!singleSlot) return false;
            Convert(cmd, len);
            return true;
        }
        Convert(cmd, len);
        return trigger == " ";
    }

    /// <summary>Settles the letters before the caret. Returns true when they end with a function name.</summary>
    private bool ResolveRun(string run, out bool converted)
    {
        var (cmd, len, function) = FindResolution(run);
        converted = cmd is not null;
        if (cmd is not null) Convert(cmd, len);
        return function;
    }

    /// <summary>The command a letter run ends with (longest live suffix first), or whether it ends with a function name.</summary>
    private (MathAutoCommand? Command, int Length, bool Function) FindResolution(string run)
    {
        for (int len = run.Length; len >= 2; len--)
        {
            string s = run[^len..];
            if (!IsWordPrefix(s)) continue;
            if (MathAutoCommands.ByName.TryGetValue(s, out var cmd) && CanConvert(cmd, len == run.Length)) return (cmd, len, false);
            if (IsFunctionWord(s)) return (null, len, true);
        }
        return (null, 0, false);
    }

    private void Convert(MathAutoCommand cmd, int letters)
    {
        var (row, idx) = Caret;
        int start = idx - letters;
        row.RemoveRange(start, letters);
        Caret = new MathCaret(row, start);
        switch (cmd.Kind)
        {
            case MathAutoCommandKind.Symbol or MathAutoCommandKind.Word:
                InsertChar(cmd.Symbol);
                break;
            default:
            {
                MathTemplate t;
                if (cmd.Name == "cbrt")
                {
                    t = new MathTemplate(MathTemplateKind.NthRoot) { FunctionName = "cbrt" };
                    t[0].Add(new MathChar("3"));
                    InsertAndEnter(t);
                }
                else t = InsertTemplateCore(cmd.Template, fromTyping: true)!;
                if (t is not null) _freshAuto = (t, cmd.Name, Caret.Row);
                break;
            }
        }
    }

    private bool TryLogBase()
    {
        var (row, idx) = Caret;
        if (idx < 3) return false;
        if (row[idx - 3] is not MathChar { Text: "l" } || row[idx - 2] is not MathChar { Text: "o" } || row[idx - 1] is not MathChar { Text: "g" }) return false;
        if (idx > 3 && IsAsciiLetter(row[idx - 4])) return false;
        row.RemoveRange(idx - 3, 3);
        Caret = new MathCaret(row, idx - 3);
        InsertAndEnter(new MathTemplate(MathTemplateKind.LogBase));
        return true;
    }

    /// <summary>Inserts a template; the selection (if any) goes into its main slot. Returns the new template.</summary>
    private MathTemplate? InsertTemplateCore(MathTemplateKind kind, bool fromTyping)
    {
        ResolvePendingWord();
        if (kind == MathTemplateKind.Power)
        {
            TypePower();
            return null;
        }
        if (kind == MathTemplateKind.Subscript)
        {
            TypeSubscript();
            return null;
        }
        if (Selection is not null)
        {
            return WrapSelection(kind);
        }
        var t = new MathTemplate(kind);
        var info = MathTemplateCatalog.Get(t.Kind);
        foreach (var (slot, text) in info.SlotDefaults)
        {
            if (fromTyping && kind == MathTemplateKind.Limit) continue;
            if (slot < t.Slots.Count) MathInputBuilder.Chars(t[slot], text);
        }
        InsertAndEnter(t);
        return t;
    }

    /// <summary>Inserts a template at the caret and moves into its first empty slot.</summary>
    private void InsertAndEnter(MathTemplate t)
    {
        var (row, idx) = Caret;
        row.Insert(idx, t);
        EnterFirstEmpty(t);
    }

    private void EnterFirstEmpty(MathTemplate t)
    {
        var target = t.Slots.FirstOrDefault(s => s.IsEmpty) ?? t.Slots[MathTemplateCatalog.Get(t.Kind).PrimarySlot is int p and >= 0 && p < t.Slots.Count ? p : 0];
        Caret = new MathCaret(target, target.Count);
    }

    private MathTemplate WrapSelection(MathTemplateKind kind)
    {
        var sel = Selection!.Value;
        _anchor = null;
        var items = sel.Row.RemoveRange(sel.Start, sel.Length);
        var row = sel.Row;
        int at = sel.Start;
        switch (kind)
        {
            case MathTemplateKind.Power:
            {
                MathNode baseNode;
                if (items.Count == 1) baseNode = items[0];
                else
                {
                    var p = new MathTemplate(MathTemplateKind.Parens);
                    p[0].InsertRange(0, items);
                    baseNode = p;
                }
                row.Insert(at, baseNode);
                var pow = new MathTemplate(MathTemplateKind.Power);
                row.Insert(at + 1, pow);
                Caret = new MathCaret(pow[0], 0);
                return pow;
            }
            case MathTemplateKind.Fraction:
            {
                var f = new MathTemplate(MathTemplateKind.Fraction);
                f[0].InsertRange(0, items);
                row.Insert(at, f);
                Caret = new MathCaret(f[1], 0);
                return f;
            }
            default:
            {
                var t = new MathTemplate(kind == MathTemplateKind.Vector ? MathTemplateKind.Matrix : kind, kind == MathTemplateKind.Vector ? 2 : 0, kind == MathTemplateKind.Vector ? 1 : 0);
                var info = MathTemplateCatalog.Get(t.Kind);
                int slot = info.PrimarySlot >= 0 ? info.PrimarySlot : 0;
                t[slot].InsertRange(0, items);
                foreach (var (s, text) in info.SlotDefaults)
                    if (s != slot && s < t.Slots.Count) MathInputBuilder.Chars(t[s], text);
                row.Insert(at, t);
                var empty = t.Slots.FirstOrDefault(s => s.IsEmpty);
                Caret = empty is not null ? new MathCaret(empty, 0) : new MathCaret(t[slot], t[slot].Count);
                return t;
            }
        }
    }

    private void TypeSlash()
    {
        var (row, idx) = Caret;
        int start = idx;
        while (start > 0 && IsOperand(row[start - 1])) start--;
        var f = new MathTemplate(MathTemplateKind.Fraction);
        var items = row.RemoveRange(start, idx - start);
        f[0].InsertRange(0, items);
        row.Insert(start, f);
        Caret = items.Count > 0 ? new MathCaret(f[1], 0) : new MathCaret(f[0], 0);
    }

    private void TypePower()
    {
        var (row, idx) = Caret;
        if (idx > 0 && row[idx - 1] is MathTemplate { Kind: MathTemplateKind.Power } existing)
        {
            Caret = new MathCaret(existing[0], existing[0].Count);
            return;
        }
        var pow = new MathTemplate(MathTemplateKind.Power);
        if (idx == 0 || !IsOperand(row[idx - 1]))
        {
            var p = new MathTemplate(MathTemplateKind.Parens);
            row.Insert(idx, p);
            row.Insert(idx + 1, pow);
            Caret = new MathCaret(p[0], 0);
            return;
        }
        row.Insert(idx, pow);
        Caret = new MathCaret(pow[0], 0);
    }

    private void TypeSubscript()
    {
        var (row, idx) = Caret;
        if (idx > 0 && row[idx - 1] is MathTemplate { Kind: MathTemplateKind.Subscript } existing)
        {
            Caret = new MathCaret(existing[0], existing[0].Count);
            return;
        }
        var sub = new MathTemplate(MathTemplateKind.Subscript);
        row.Insert(idx, sub);
        Caret = new MathCaret(sub[0], 0);
    }

    private void TypeSuperscriptChar(char c)
    {
        string text = c == '⁻' ? "-" : ((char)('0' + "⁰¹²³⁴⁵⁶⁷⁸⁹".IndexOf(c))).ToString();
        var (row, idx) = Caret;
        if (idx > 0 && row[idx - 1] is MathTemplate { Kind: MathTemplateKind.Power } p && p[0].Items.All(n => n is MathChar ch && (MathCharInfo.IsDigit(ch.Text) || ch.Text == "-")))
        {
            p[0].Add(new MathChar(text));
            return;
        }
        var pow = new MathTemplate(MathTemplateKind.Power);
        pow[0].Add(new MathChar(text));
        row.Insert(idx, pow);
        Caret = new MathCaret(row, idx + 1);
    }

    /// <summary>"(" opens brackets that take in everything after the caret (shown with a faint closing bracket until ")" is typed).</summary>
    private void OpenBracket(MathTemplateKind kind)
    {
        var (row, idx) = Caret;
        var t = new MathTemplate(kind) { IsGhostClosed = true };
        var trailing = row.RemoveRange(idx, row.Count - idx);
        t[0].InsertRange(0, trailing);
        row.Insert(idx, t);
        Caret = new MathCaret(t[0], 0);
    }

    private void CloseBracket(MathTemplateKind kind)
    {
        // Walk outwards: close the nearest matching bracket, or leave a call-like template (sqrt typed as "sqrt(").
        for (var r = Caret.Row; r.Owner is { } owner; r = owner.Parent!)
        {
            if (owner.Kind == kind)
            {
                if (ReferenceEquals(r, Caret.Row) && Caret.Index < r.Count && owner.IsGhostClosed)
                {
                    var trailing = r.RemoveRange(Caret.Index, r.Count - Caret.Index);
                    owner.Parent!.InsertRange(owner.IndexInParent + 1, trailing);
                }
                owner.IsGhostClosed = false;
                Caret = After(owner);
                return;
            }
            if (kind == MathTemplateKind.Parens && owner.Kind is MathTemplateKind.Sqrt or MathTemplateKind.Abs or MathTemplateKind.LogBase or MathTemplateKind.NthRoot
                && ReferenceEquals(r, Caret.Row) && Caret.Index == r.Count)
            {
                Caret = After(owner);
                return;
            }
            if (owner.Parent is null) break;
        }
        // No open bracket: wrap what is before the caret in this row.
        var (row, idx) = Caret;
        int start = 0;
        var t = new MathTemplate(kind);
        var items = row.RemoveRange(start, idx - start);
        t[0].InsertRange(0, items);
        row.Insert(start, t);
        Caret = After(t);
    }

    private bool TryGridMove(bool nextRow)
    {
        var row = Caret.Row;
        if (row.Owner is not { IsGrid: true } grid) return false;
        int k = row.SlotIndex;
        int r = k / grid.Columns, c = k % grid.Columns;
        if (grid.Kind == MathTemplateKind.Piecewise)
        {
            if (nextRow) return false;
            if (c == 0)
            {
                var cond = grid.Cell(r, 1);
                Caret = new MathCaret(cond, cond.Count);
                return true;
            }
            if (r + 1 >= grid.Rows) grid.InsertGridRow(r + 1);
            var v = grid.Cell(r + 1, 0);
            Caret = new MathCaret(v, v.Count);
            return true;
        }
        if (nextRow)
        {
            if (r + 1 >= grid.Rows) grid.InsertGridRow(r + 1);
            var cell = grid.Cell(r + 1, 0);
            Caret = new MathCaret(cell, cell.Count);
        }
        else
        {
            if (c + 1 >= grid.Columns) grid.InsertGridColumn(c + 1);
            var cell = grid.Cell(r, c + 1);
            Caret = new MathCaret(cell, cell.Count);
        }
        return true;
    }

    // ================================================================== deleting

    private bool DeleteSelectionCore()
    {
        if (Selection is not { } s)
        {
            _anchor = null;
            return false;
        }
        s.Row.RemoveRange(s.Start, s.Length);
        Caret = new MathCaret(s.Row, s.Start);
        _anchor = null;
        return true;
    }

    private static bool IsEffectivelyEmpty(MathTemplate t)
    {
        var info = MathTemplateCatalog.Get(t.Kind);
        for (int i = 0; i < t.Slots.Count; i++)
            if (!IsSlotDefault(t, info, i)) return false;
        return true;
    }

    private static bool IsSlotDefault(MathTemplate t, MathTemplateInfo info, int i)
    {
        var slot = t[i];
        if (slot.IsEmpty) return true;
        if (info.SlotDefaults.TryGetValue(i, out var d)) return MathSerializer.Serialize(slot) == d;
        if (t.Kind == MathTemplateKind.NthRoot && i == 0 && t.FunctionName == "cbrt") return MathSerializer.Serialize(slot) == "3";
        return false;
    }

    private static bool CanUnwrap(MathTemplate t, int k)
    {
        if (t.IsGrid) return false;
        var info = MathTemplateCatalog.Get(t.Kind);
        int p = info.PrimarySlot;
        if (p < 0 || p >= t.Slots.Count) return false;
        for (int i = 0; i < t.Slots.Count; i++)
            if (i != p && !IsSlotDefault(t, info, i)) return false;
        return k == p || t[k].IsEmpty || IsSlotDefault(t, info, k);
    }

    private void Unwrap(MathTemplate t, bool caretAtStart)
    {
        var info = MathTemplateCatalog.Get(t.Kind);
        var parent = t.Parent!;
        int at = t.IndexInParent;
        var content = t[info.PrimarySlot].RemoveRange(0, t[info.PrimarySlot].Count);
        parent.RemoveAt(at);
        parent.InsertRange(at, content);
        Caret = new MathCaret(parent, caretAtStart ? at : at + content.Count);
    }

    private void RemoveTemplate(MathTemplate t)
    {
        var parent = t.Parent!;
        int at = t.IndexInParent;
        parent.RemoveAt(at);
        Caret = new MathCaret(parent, at);
    }

    private void BackspaceCore()
    {
        if (DeleteSelectionCore()) return;
        var (row, idx) = Caret;
        if (idx > 0)
        {
            var prev = row[idx - 1];
            if (prev is MathChar)
            {
                row.RemoveAt(idx - 1);
                Caret = new MathCaret(row, idx - 1);
            }
            else if (prev is MathTemplate t)
            {
                if (IsEffectivelyEmpty(t)) RemoveTemplate(t);
                else
                {
                    var last = t[t.Slots.Count - 1];
                    Caret = new MathCaret(last, last.Count);
                }
            }
            return;
        }
        if (row.Owner is not { } owner) return;
        int k = row.SlotIndex;
        if (IsEffectivelyEmpty(owner)) RemoveTemplate(owner);
        else if (CanUnwrap(owner, k)) Unwrap(owner, caretAtStart: k == MathTemplateCatalog.Get(owner.Kind).PrimarySlot);
        else Caret = k > 0 ? new MathCaret(owner[k - 1], owner[k - 1].Count) : Before(owner);
    }

    private void DeleteCore()
    {
        if (DeleteSelectionCore()) return;
        var (row, idx) = Caret;
        if (idx < row.Count)
        {
            var next = row[idx];
            if (next is MathChar) row.RemoveAt(idx);
            else if (next is MathTemplate t)
            {
                if (IsEffectivelyEmpty(t)) RemoveTemplate(t);
                else Caret = new MathCaret(t[0], 0);
            }
            return;
        }
        if (row.Owner is not { } owner) return;
        int k = row.SlotIndex;
        if (IsEffectivelyEmpty(owner)) RemoveTemplate(owner);
        else if (CanUnwrap(owner, k)) Unwrap(owner, caretAtStart: false);
        else Caret = k + 1 < owner.Slots.Count ? new MathCaret(owner[k + 1], 0) : After(owner);
    }

    // ================================================================== slots and vertical moves

    private bool NextSlotCore()
    {
        var key = OrderKey(Caret);
        var inside = Caret.Row.Owner;
        foreach (var slot in Root.DescendantSlots())
        {
            var start = OrderKey(new MathCaret(slot, 0));
            if (Compare(start, key) <= 0) continue;
            if (inside is not null && !IsInside(slot, inside)) break;
            Caret = new MathCaret(slot, slot.Count);
            return true;
        }
        if (inside is not null)
        {
            Caret = After(inside);
            return true;
        }
        return false;
    }

    private bool PreviousSlotCore()
    {
        var key = OrderKey(Caret);
        var inside = Caret.Row.Owner;
        MathRow? best = null;
        foreach (var slot in Root.DescendantSlots())
        {
            if (Caret.Row.IsWithin(slot)) continue;
            var end = OrderKey(new MathCaret(slot, slot.Count));
            if (Compare(end, key) >= 0) continue;
            if (inside is not null && !IsInside(slot, inside)) continue;
            best = slot;
        }
        if (best is not null)
        {
            Caret = new MathCaret(best, best.Count);
            return true;
        }
        if (inside is not null)
        {
            Caret = Before(inside);
            return true;
        }
        return false;
    }

    private static bool IsInside(MathRow slot, MathTemplate t) => slot.SelfAndAncestors().Any(r => ReferenceEquals(r.Owner, t));

    private bool MoveVertical(IMathCaretGeometry g, bool up)
    {
        bool moved = false;
        MoveCaret(() =>
        {
            var (row, idx) = Caret;
            // From a base into its exponent or subscript.
            if (idx > 0 && row[idx - 1] is MathTemplate { Kind: MathTemplateKind.Power or MathTemplateKind.Subscript } script
                && (script.Kind == MathTemplateKind.Power) == up)
            {
                Caret = new MathCaret(script[0], script[0].Count);
                moved = true;
                return;
            }
            if (!g.TryGetRowBounds(row, out _)) return;
            double x = g.GetCaretX(row, idx);
            for (var r = row; r.Owner is { } t; r = t.Parent!)
            {
                if (!g.TryGetRowBounds(r, out var cur)) break;
                MathRow? best = null;
                double bestScore = double.MaxValue;
                foreach (var s in t.Slots)
                {
                    if (ReferenceEquals(s, r) || !g.TryGetRowBounds(s, out var b)) continue;
                    bool ok = up ? b.Bottom <= cur.Top + 1 : b.Top >= cur.Bottom - 1;
                    if (!ok) continue;
                    double dy = up ? cur.Top - b.Bottom : b.Top - cur.Bottom;
                    double dx = x < b.Left ? b.Left - x : x > b.Right ? x - b.Right : 0;
                    double score = System.Math.Max(0, dy) * 4 + dx;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = s;
                    }
                }
                if (best is not null)
                {
                    Caret = new MathCaret(best, g.NearestIndex(best, x));
                    moved = true;
                    return;
                }
                if ((t.Kind == MathTemplateKind.Power && !up) || (t.Kind == MathTemplateKind.Subscript && up))
                {
                    Caret = After(t);
                    moved = true;
                    return;
                }
                if (t.Parent is null) break;
            }
        }, keepSelection: false);
        return moved;
    }
}

namespace Calcy.UI.Math.Model;

/// <summary>Colour role of a piece of maths; the renderer maps it to a theme brush.</summary>
public enum MathColorRole
{
    /// <summary>Main text colour ("Calcy.Ink").</summary>
    Ink,

    /// <summary>Secondary text ("Calcy.Muted"), for placeholders and hints.</summary>
    Muted,

    /// <summary>Accent colour ("Calcy.Head").</summary>
    Head,

    /// <summary>Error colour ("Calcy.Err").</summary>
    Error,
}

/// <summary>A node of the structured maths model: a character or a template with slots.</summary>
public abstract class MathNode
{
    /// <summary>The row that contains this node, or null when detached.</summary>
    public MathRow? Parent { get; internal set; }

    /// <summary>Position of this node in its parent row, or -1 when detached.</summary>
    public int IndexInParent => Parent?.IndexOf(this) ?? -1;

    /// <summary>Deep copy without a parent.</summary>
    public abstract MathNode Clone();
}

/// <summary>
/// An atom of text: a digit, a letter, an operator, a Greek letter or a word such as "and".
/// Multi-character text is one caret stop.
/// </summary>
public sealed class MathChar : MathNode
{
    /// <summary>Creates a character node.</summary>
    public MathChar(string text, MathColorRole color = MathColorRole.Ink)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        Text = text;
        Color = color;
    }

    /// <summary>The stored text (for example "x", "·", "≤", "π", "and").</summary>
    public string Text { get; }

    /// <summary>Colour role used when rendering.</summary>
    public MathColorRole Color { get; }

    /// <summary>True for a single character equal to <paramref name="c"/>.</summary>
    public bool Is(char c) => Text.Length == 1 && Text[0] == c;

    /// <summary>True when the text equals <paramref name="s"/>.</summary>
    public bool Is(string s) => Text == s;

    /// <inheritdoc />
    public override MathNode Clone() => new MathChar(Text, Color);

    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>A horizontal sequence of nodes. The root of a model and every template slot is a row.</summary>
public sealed class MathRow
{
    private readonly List<MathNode> _items = [];

    /// <summary>Creates an empty row.</summary>
    public MathRow()
    {
    }

    /// <summary>Creates a row holding the given nodes.</summary>
    public MathRow(IEnumerable<MathNode> items)
    {
        foreach (var n in items) Add(n);
    }

    /// <summary>The nodes of this row.</summary>
    public IReadOnlyList<MathNode> Items => _items;

    /// <summary>Number of nodes.</summary>
    public int Count => _items.Count;

    /// <summary>True when the row has no nodes.</summary>
    public bool IsEmpty => _items.Count == 0;

    /// <summary>The node at <paramref name="index"/>.</summary>
    public MathNode this[int index] => _items[index];

    /// <summary>The template this row is a slot of, or null for a root row.</summary>
    public MathTemplate? Owner { get; internal set; }

    /// <summary>Index of this row in <see cref="Owner"/>'s slots, or -1 for a root row.</summary>
    public int SlotIndex => Owner?.IndexOfSlot(this) ?? -1;

    /// <summary>The row that contains the owner template, or null for a root row.</summary>
    public MathRow? ParentRow => Owner?.Parent;

    /// <summary>Nesting depth: 0 for the root row.</summary>
    public int Depth
    {
        get
        {
            int d = 0;
            for (var r = ParentRow; r is not null; r = r.ParentRow) d++;
            return d;
        }
    }

    /// <summary>The outermost row.</summary>
    public MathRow Root
    {
        get
        {
            var r = this;
            while (r.ParentRow is { } p) r = p;
            return r;
        }
    }

    /// <summary>Position of <paramref name="node"/> in this row, or -1.</summary>
    public int IndexOf(MathNode node) => _items.IndexOf(node);

    /// <summary>Appends a node (detaching it from its previous row).</summary>
    public void Add(MathNode node) => Insert(_items.Count, node);

    /// <summary>Inserts a node (detaching it from its previous row).</summary>
    public void Insert(int index, MathNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.Parent?.Remove(node);
        node.Parent = this;
        _items.Insert(index, node);
    }

    /// <summary>Inserts nodes in order starting at <paramref name="index"/>.</summary>
    public void InsertRange(int index, IEnumerable<MathNode> nodes)
    {
        foreach (var n in nodes.ToList()) Insert(index++, n);
    }

    /// <summary>Removes a node from this row.</summary>
    public bool Remove(MathNode node)
    {
        int i = _items.IndexOf(node);
        if (i < 0) return false;
        RemoveAt(i);
        return true;
    }

    /// <summary>Removes the node at <paramref name="index"/>.</summary>
    public void RemoveAt(int index)
    {
        _items[index].Parent = null;
        _items.RemoveAt(index);
    }

    /// <summary>Removes and returns <paramref name="count"/> nodes starting at <paramref name="start"/>.</summary>
    public List<MathNode> RemoveRange(int start, int count)
    {
        var removed = _items.GetRange(start, count);
        _items.RemoveRange(start, count);
        foreach (var n in removed) n.Parent = null;
        return removed;
    }

    /// <summary>Removes every node.</summary>
    public void Clear() => RemoveRange(0, _items.Count);

    /// <summary>Deep copy without an owner.</summary>
    public MathRow Clone() => new(_items.Select(n => n.Clone()));

    /// <summary>True when this row is <paramref name="other"/> or nested inside it.</summary>
    public bool IsWithin(MathRow other)
    {
        for (var r = this; r is not null; r = r.ParentRow)
            if (ReferenceEquals(r, other)) return true;
        return false;
    }

    /// <summary>The rows from this one up to the root (this row first).</summary>
    public IEnumerable<MathRow> SelfAndAncestors()
    {
        for (var r = this; r is not null; r = r.ParentRow) yield return r;
    }

    /// <summary>Every template slot nested in this row, in document (Tab) order.</summary>
    public IEnumerable<MathRow> DescendantSlots()
    {
        foreach (var n in _items)
        {
            if (n is not MathTemplate t) continue;
            foreach (var s in t.Slots)
            {
                yield return s;
                foreach (var d in s.DescendantSlots()) yield return d;
            }
        }
    }

    /// <inheritdoc />
    public override string ToString() => MathSerializer.Serialize(this);
}

/// <summary>A structural template such as a fraction, a root or an integral. Its slots are rows.</summary>
public sealed class MathTemplate : MathNode
{
    private readonly List<MathRow> _slots = [];

    /// <summary>Creates a template with empty slots (or a grid for matrices and piecewise definitions).</summary>
    public MathTemplate(MathTemplateKind kind, int rows = 0, int columns = 0)
    {
        if (kind == MathTemplateKind.Vector)
        {
            kind = MathTemplateKind.Matrix;
            if (rows <= 0) rows = 2;
            columns = 1;
        }
        Kind = kind;
        var info = MathTemplateCatalog.Get(kind);
        if (kind == MathTemplateKind.Matrix)
        {
            Columns = columns > 0 ? columns : info.DefaultColumns;
            int r = rows > 0 ? rows : info.DefaultRows;
            for (int i = 0; i < r * Columns; i++) AddSlot(new MathRow());
        }
        else if (kind == MathTemplateKind.Piecewise)
        {
            Columns = 2;
            int r = rows > 0 ? rows : info.DefaultRows;
            for (int i = 0; i < r * 2; i++) AddSlot(new MathRow());
        }
        else
        {
            Columns = info.SlotNames.Count;
            foreach (var _ in info.SlotNames) AddSlot(new MathRow());
        }
    }

    private MathTemplate(MathTemplateKind kind, int columns, IEnumerable<MathRow> slots, string? functionName, bool ghost)
    {
        Kind = kind;
        Columns = columns;
        FunctionName = functionName;
        IsGhostClosed = ghost;
        foreach (var s in slots) AddSlot(s);
    }

    /// <summary>The kind of template.</summary>
    public MathTemplateKind Kind { get; }

    /// <summary>The slots, in Tab order (row-major for grids).</summary>
    public IReadOnlyList<MathRow> Slots => _slots;

    /// <summary>Number of grid columns (matrices: columns; piecewise: 2; other templates: the slot count).</summary>
    public int Columns { get; private set; }

    /// <summary>Number of grid rows (matrices and piecewise definitions; 1 for other templates).</summary>
    public int Rows => Columns == 0 ? 0 : _slots.Count / Columns;

    /// <summary>True for templates laid out as a grid (matrix, piecewise).</summary>
    public bool IsGrid => Kind is MathTemplateKind.Matrix or MathTemplateKind.Piecewise;

    /// <summary>
    /// Optional function name used when serializing, so a template built from text keeps its spelling
    /// (for example "cbrt" for a cube root or "prod" for a product).
    /// </summary>
    public string? FunctionName { get; set; }

    /// <summary>
    /// For brackets opened by typing "(": the closing bracket was added automatically and is shown faintly
    /// until ")" is typed.
    /// </summary>
    public bool IsGhostClosed { get; set; }

    /// <summary>Slot by index.</summary>
    public MathRow this[int slot] => _slots[slot];

    /// <summary>Slot of a grid cell.</summary>
    public MathRow Cell(int row, int column) => _slots[row * Columns + column];

    /// <summary>Index of <paramref name="slot"/>, or -1.</summary>
    public int IndexOfSlot(MathRow slot)
    {
        for (int i = 0; i < _slots.Count; i++)
            if (ReferenceEquals(_slots[i], slot)) return i;
        return -1;
    }

    /// <summary>True when every slot is empty.</summary>
    public bool IsEmpty => _slots.All(s => s.IsEmpty);

    /// <summary>Inserts a grid row before <paramref name="row"/> (matrices and piecewise definitions).</summary>
    public void InsertGridRow(int row)
    {
        if (!IsGrid) throw new InvalidOperationException("Only matrices and piecewise definitions have rows");
        row = System.Math.Clamp(row, 0, Rows);
        for (int c = Columns - 1; c >= 0; c--) InsertSlot(row * Columns, new MathRow());
    }

    /// <summary>Removes a grid row (at least one row is kept).</summary>
    public bool RemoveGridRow(int row)
    {
        if (!IsGrid || Rows <= 1 || row < 0 || row >= Rows) return false;
        for (int c = 0; c < Columns; c++) RemoveSlotAt(row * Columns);
        return true;
    }

    /// <summary>Inserts a matrix column before <paramref name="column"/>.</summary>
    public void InsertGridColumn(int column)
    {
        if (Kind != MathTemplateKind.Matrix) throw new InvalidOperationException("Only matrices have columns");
        column = System.Math.Clamp(column, 0, Columns);
        int rows = Rows;
        Columns++;
        for (int r = 0; r < rows; r++) InsertSlot(r * Columns + column, new MathRow());
    }

    /// <summary>Removes a matrix column (at least one column is kept).</summary>
    public bool RemoveGridColumn(int column)
    {
        if (Kind != MathTemplateKind.Matrix || Columns <= 1 || column < 0 || column >= Columns) return false;
        int rows = Rows;
        for (int r = rows - 1; r >= 0; r--) RemoveSlotAt(r * Columns + column);
        Columns--;
        return true;
    }

    private void AddSlot(MathRow row) => InsertSlot(_slots.Count, row);

    private void InsertSlot(int index, MathRow row)
    {
        row.Owner = this;
        _slots.Insert(index, row);
    }

    private void RemoveSlotAt(int index)
    {
        _slots[index].Owner = null;
        _slots.RemoveAt(index);
    }

    /// <inheritdoc />
    public override MathNode Clone() => new MathTemplate(Kind, Columns, _slots.Select(s => s.Clone()), FunctionName, IsGhostClosed);

    /// <inheritdoc />
    public override string ToString() => $"{Kind}[{string.Join("; ", _slots)}]";
}

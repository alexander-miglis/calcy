namespace Calcy.Modes.Notebook.Model;

/// <summary>
/// A text cell: Markdown (headings, bold, italic, lists, quotes, links, inline code) with inline maths written between
/// dollar signs ($x^2 + 1$, typeset) and live values ($=T$, the current value of an expression).
/// </summary>
public sealed class TextCell : NotebookCell
{
    private string _text;

    /// <summary>Creates a text cell.</summary>
    public TextCell(string text = "", string? id = null) : base(id)
    {
        _text = text ?? "";
    }

    /// <inheritdoc />
    public override CellKind Kind => CellKind.Text;

    /// <summary>The Markdown source.</summary>
    public string Text
    {
        get => _text;
        set
        {
            value ??= "";
            if (value == _text) return;
            _text = value;
            _parsed = null;
            OnInputChanged();
        }
    }

    private MarkdownDocument? _parsed;

    /// <summary>The parsed Markdown.</summary>
    public MarkdownDocument Markdown => _parsed ??= MarkdownDocument.Parse(_text);

    /// <summary>The values of the live boxes ($=expr$) in order, from the last calculation.</summary>
    public IReadOnlyList<LiveValue> LiveValues { get; internal set; } = [];

    /// <inheritdoc />
    public override NotebookCell Clone() => CopyBase(new TextCell(_text));

    internal override CellData ToData()
    {
        var d = BaseData();
        d.Text = _text;
        return d;
    }

    internal static TextCell Restore(CellData d)
    {
        var c = new TextCell(d.Text ?? "", d.Id);
        c.ReadBase(d);
        return c;
    }
}

using System.Text.Json.Serialization;

namespace Calcy.Modes.Matrix.Logic;

/// <summary>What a card block shows.</summary>
public enum CardBlockKind
{
    /// <summary>A line of maths text (a value, an equation), shown large.</summary>
    Text,

    /// <summary>A secondary maths line (a polynomial, a vector form), shown in normal weight.</summary>
    Formula,

    /// <summary>A bracketed matrix, optionally with a label such as "A⁻¹ =".</summary>
    Matrix,

    /// <summary>A short explanation in the body font.</summary>
    Note,

    /// <summary>A heading inside the steps list (for example "Forward elimination").</summary>
    Heading,

    /// <summary>One elimination step: the row operation and the matrix after it.</summary>
    Step,
}

/// <summary>
/// One piece of a result card. Exact and decimal forms are both kept so the card can switch between them
/// (design decision D6). Everything is plain text so cards can be saved and restored.
/// </summary>
public sealed class CardBlock
{
    /// <summary>The kind of block.</summary>
    public CardBlockKind Kind { get; set; }

    /// <summary>Text before a matrix (for example "L =") or the operation of a step.</summary>
    public string? Label { get; set; }

    /// <summary>The exact text (Text, Note, Heading blocks) or the text after a matrix.</summary>
    public string? Text { get; set; }

    /// <summary>The decimal form of <see cref="Text"/>, when it differs.</summary>
    public string? ApproxText { get; set; }

    /// <summary>Exact matrix cells, row by row.</summary>
    public string[][]? Cells { get; set; }

    /// <summary>Decimal matrix cells, when they differ from the exact ones.</summary>
    public string[][]? ApproxCells { get; set; }

    /// <summary>Draws a vertical bar before this column (augmented matrices); -1 for none.</summary>
    public int AugmentAt { get; set; } = -1;

    /// <summary>Text block shown in the error colour.</summary>
    public bool Emphasis { get; set; }

    /// <summary>A text block.</summary>
    public static CardBlock Line(string text, string? approx = null, bool emphasis = false) =>
        new() { Kind = CardBlockKind.Text, Text = text, ApproxText = approx == text ? null : approx, Emphasis = emphasis };

    /// <summary>A secondary maths line.</summary>
    public static CardBlock FormulaText(string text, string? approx = null) =>
        new() { Kind = CardBlockKind.Formula, Text = text, ApproxText = approx == text ? null : approx };

    /// <summary>An explanation.</summary>
    public static CardBlock NoteText(string text) => new() { Kind = CardBlockKind.Note, Text = text };

    /// <summary>A heading in the steps list.</summary>
    public static CardBlock HeadingText(string text) => new() { Kind = CardBlockKind.Heading, Text = text };

    /// <summary>A matrix with an optional label and trailing text.</summary>
    public static CardBlock Grid(string? label, string[][] cells, string[][]? approx = null, int augmentAt = -1, string? after = null) =>
        new()
        {
            Kind = CardBlockKind.Matrix,
            Label = label,
            Cells = cells,
            ApproxCells = approx is not null && !SameCells(cells, approx) ? approx : null,
            AugmentAt = augmentAt,
            Text = after,
        };

    /// <summary>An elimination step.</summary>
    public static CardBlock StepBlock(string operation, string[][] cells, string[][]? approx, int augmentAt) =>
        new()
        {
            Kind = CardBlockKind.Step,
            Label = operation,
            Cells = cells,
            ApproxCells = approx is not null && !SameCells(cells, approx) ? approx : null,
            AugmentAt = augmentAt,
        };

    /// <summary>True when the block has a decimal alternative.</summary>
    [JsonIgnore]
    public bool HasApprox => ApproxText is not null || ApproxCells is not null;

    private static bool SameCells(string[][] a, string[][] b) =>
        a.Length == b.Length && a.Zip(b).All(p => p.First.SequenceEqual(p.Second));
}

/// <summary>A result card (design decision D5): what was computed, the result and how to reuse it.</summary>
public sealed class MatrixCard
{
    /// <summary>Running number shown as #n.</summary>
    public int Number { get; set; }

    /// <summary>Header text in the pixel font (upper case ASCII, for example "INVERSE").</summary>
    public string Title { get; set; } = "";

    /// <summary>Time the card was made (HH:mm).</summary>
    public string Time { get; set; } = "";

    /// <summary>What was computed, in maths notation (for example "A⁻¹" or "det(A)").</summary>
    public string Input { get; set; } = "";

    /// <summary>The card reports a problem instead of a result.</summary>
    public bool IsError { get; set; }

    /// <summary>The result.</summary>
    public List<CardBlock> Blocks { get; set; } = [];

    /// <summary>Elimination steps (shown on request).</summary>
    public List<CardBlock> Steps { get; set; } = [];

    /// <summary>True while the steps are shown.</summary>
    public bool StepsOpen { get; set; }

    /// <summary>True when the exact form is too unwieldy to read first (huge fractions), so decimals are shown first.</summary>
    public bool PreferDecimals { get; set; }

    /// <summary>Calcy input for COPY (types back into Calcy as the same value).</summary>
    public string? CopyText { get; set; }

    /// <summary>LaTeX for COPY LATEX.</summary>
    public string? Latex { get; set; }

    /// <summary>Values STORE AS can save (one per matrix of the result, for example L, U and P).</summary>
    public List<StorePart> StoreParts { get; set; } = [];

    /// <summary>Text sent to Advanced mode by USE.</summary>
    public string? UseText { get; set; }

    /// <summary>The expression that produced the card, so EDIT can put it back in the expression line.</summary>
    public string? Source { get; set; }

    /// <summary>True when some block has a decimal form.</summary>
    [JsonIgnore]
    public bool HasApprox => Blocks.Any(b => b.HasApprox) || Steps.Any(b => b.HasApprox);
}

/// <summary>A value that STORE AS can save: a label such as "L" and its Calcy input.</summary>
public sealed class StorePart
{
    /// <summary>Which part of the result this is (empty for single results).</summary>
    public string Label { get; set; } = "";

    /// <summary>Calcy input of the value.</summary>
    public string Text { get; set; } = "";

    /// <summary>True when the value is a matrix (it then also joins the matrix library).</summary>
    public bool IsMatrix { get; set; }
}

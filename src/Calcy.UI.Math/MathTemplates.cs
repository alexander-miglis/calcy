namespace Calcy.UI.Math;

/// <summary>The structural templates of the maths editor and renderer.</summary>
public enum MathTemplateKind
{
    /// <summary>Stacked fraction: numerator over denominator.</summary>
    Fraction,

    /// <summary>Square root.</summary>
    Sqrt,

    /// <summary>n-th root with an index.</summary>
    NthRoot,

    /// <summary>Superscript (power) attached to the operand before it.</summary>
    Power,

    /// <summary>Subscript attached to the name before it (v_0).</summary>
    Subscript,

    /// <summary>Absolute value bars.</summary>
    Abs,

    /// <summary>Round brackets that grow with their content.</summary>
    Parens,

    /// <summary>Square brackets (lists, indices).</summary>
    Brackets,

    /// <summary>Curly braces: a list {a, b}.</summary>
    Braces,

    /// <summary>Logarithm with a base: log_b(x).</summary>
    LogBase,

    /// <summary>Indefinite integral: integrand and variable.</summary>
    Integral,

    /// <summary>Definite integral: lower bound, upper bound, integrand and variable.</summary>
    DefiniteIntegral,

    /// <summary>Derivative d/dx: variable and expression.</summary>
    Derivative,

    /// <summary>Sum Σ with index variable and bounds.</summary>
    Sum,

    /// <summary>Product Π with index variable and bounds.</summary>
    Product,

    /// <summary>Limit: variable, the value it approaches, expression.</summary>
    Limit,

    /// <summary>Matrix with brackets (rows by columns).</summary>
    Matrix,

    /// <summary>Insertion shortcut for a column vector (a matrix with one column).</summary>
    Vector,

    /// <summary>Piecewise definition with a left brace: value and condition per case.</summary>
    Piecewise,
}

/// <summary>Describes a template: how its helper button looks and what its slots are called.</summary>
public sealed record MathTemplateInfo
{
    /// <summary>The template kind.</summary>
    public required MathTemplateKind Kind { get; init; }

    /// <summary>Short name, for example "fraction".</summary>
    public required string Name { get; init; }

    /// <summary>Text for a helper button, for example "□/□".</summary>
    public required string Glyph { get; init; }

    /// <summary>Tooltip for the helper button.</summary>
    public required string ToolTip { get; init; }

    /// <summary>What the user can type to get this template without the mouse.</summary>
    public string Shortcut { get; init; } = "";

    /// <summary>Helper bar group: "Basic", "Calculus" or "Structures".</summary>
    public string Group { get; init; } = "Basic";

    /// <summary>Human names of the slots in Tab order (for hints such as "Typing the upper bound").</summary>
    public required IReadOnlyList<string> SlotNames { get; init; }

    /// <summary>Text placed in a slot when the template is inserted (for example "x" for the variable of an integral).</summary>
    public IReadOnlyDictionary<int, string> SlotDefaults { get; init; } = new Dictionary<int, string>();

    /// <summary>
    /// The slot whose content is kept when the template is removed with Backspace or Delete, or -1 when
    /// the template is only removed once it is empty.
    /// </summary>
    public int PrimarySlot { get; init; } = -1;

    /// <summary>Default grid rows (matrix, piecewise).</summary>
    public int DefaultRows { get; init; } = 1;

    /// <summary>Default grid columns (matrix).</summary>
    public int DefaultColumns { get; init; } = 1;
}

/// <summary>The catalogue of templates, used by the editor and by the app's helper bar.</summary>
public static class MathTemplateCatalog
{
    private static readonly Dictionary<MathTemplateKind, MathTemplateInfo> ByKind;

    static MathTemplateCatalog()
    {
        All =
        [
            new() { Kind = MathTemplateKind.Fraction, Name = "fraction", Glyph = "□/□", ToolTip = "Fraction (type /)", Shortcut = "/", SlotNames = ["numerator", "denominator"], PrimarySlot = 0 },
            new() { Kind = MathTemplateKind.Sqrt, Name = "square root", Glyph = "√□", ToolTip = "Square root (type sqrt)", Shortcut = "sqrt", SlotNames = ["expression under the root"], PrimarySlot = 0 },
            new() { Kind = MathTemplateKind.NthRoot, Name = "root", Glyph = "ⁿ√□", ToolTip = "n-th root (type nthroot)", Shortcut = "nthroot", SlotNames = ["root index", "expression under the root"], PrimarySlot = 1 },
            new() { Kind = MathTemplateKind.Power, Name = "power", Glyph = "□ⁿ", ToolTip = "Power (type ^)", Shortcut = "^", SlotNames = ["exponent"] },
            new() { Kind = MathTemplateKind.Subscript, Name = "subscript", Glyph = "□ₙ", ToolTip = "Subscript for a name such as v₀ (type _)", Shortcut = "_", SlotNames = ["subscript"] },
            new() { Kind = MathTemplateKind.Abs, Name = "absolute value", Glyph = "|□|", ToolTip = "Absolute value (type |)", Shortcut = "|", SlotNames = ["expression inside the bars"], PrimarySlot = 0 },
            new() { Kind = MathTemplateKind.Parens, Name = "brackets", Glyph = "(□)", ToolTip = "Brackets (type ( )", Shortcut = "(", SlotNames = ["expression in brackets"], PrimarySlot = 0 },
            new() { Kind = MathTemplateKind.Brackets, Name = "square brackets", Glyph = "[□]", ToolTip = "Square brackets (type [ )", Shortcut = "[", Group = "Structures", SlotNames = ["expression in square brackets"], PrimarySlot = 0 },
            new() { Kind = MathTemplateKind.Braces, Name = "list", Glyph = "{□}", ToolTip = "List {a, b, c} (type { )", Shortcut = "{", Group = "Structures", SlotNames = ["list items"], PrimarySlot = 0 },
            new() { Kind = MathTemplateKind.LogBase, Name = "logarithm", Glyph = "log□", ToolTip = "Logarithm with a base (type log_)", Shortcut = "log_", SlotNames = ["base", "argument"], PrimarySlot = 1 },
            new() { Kind = MathTemplateKind.Integral, Name = "integral", Glyph = "∫□dx", ToolTip = "Indefinite integral", Group = "Calculus", SlotNames = ["integrand", "variable"], SlotDefaults = new Dictionary<int, string> { [1] = "x" }, PrimarySlot = 0 },
            new() { Kind = MathTemplateKind.DefiniteIntegral, Name = "integral", Glyph = "∫□", ToolTip = "Definite integral from a to b (type int)", Shortcut = "int", Group = "Calculus", SlotNames = ["lower bound", "upper bound", "integrand", "variable"], SlotDefaults = new Dictionary<int, string> { [3] = "x" }, PrimarySlot = 2 },
            new() { Kind = MathTemplateKind.Derivative, Name = "derivative", Glyph = "d/dx", ToolTip = "Derivative d/dx (type diff)", Shortcut = "diff", Group = "Calculus", SlotNames = ["variable", "expression to differentiate"], SlotDefaults = new Dictionary<int, string> { [0] = "x" }, PrimarySlot = 1 },
            new() { Kind = MathTemplateKind.Sum, Name = "sum", Glyph = "Σ", ToolTip = "Sum with bounds (type sum)", Shortcut = "sum", Group = "Calculus", SlotNames = ["index variable", "start value", "end value", "expression to add up"], SlotDefaults = new Dictionary<int, string> { [0] = "k" }, PrimarySlot = 3 },
            new() { Kind = MathTemplateKind.Product, Name = "product", Glyph = "Π", ToolTip = "Product with bounds (type prod)", Shortcut = "prod", Group = "Calculus", SlotNames = ["index variable", "start value", "end value", "expression to multiply"], SlotDefaults = new Dictionary<int, string> { [0] = "k" }, PrimarySlot = 3 },
            new() { Kind = MathTemplateKind.Limit, Name = "limit", Glyph = "lim", ToolTip = "Limit (type lim)", Shortcut = "lim", Group = "Calculus", SlotNames = ["variable", "value it approaches", "expression"], SlotDefaults = new Dictionary<int, string> { [0] = "x" }, PrimarySlot = 2 },
            new() { Kind = MathTemplateKind.Matrix, Name = "matrix", Glyph = "[□□]", ToolTip = "Matrix (comma: next column, semicolon: next row)", Group = "Structures", SlotNames = ["matrix entry"], DefaultRows = 2, DefaultColumns = 2 },
            new() { Kind = MathTemplateKind.Vector, Name = "vector", Glyph = "[□;□]", ToolTip = "Column vector", Group = "Structures", SlotNames = ["vector entry"], DefaultRows = 2, DefaultColumns = 1 },
            new() { Kind = MathTemplateKind.Piecewise, Name = "piecewise definition", Glyph = "{:}", ToolTip = "Piecewise definition (leave the last condition empty for otherwise)", Group = "Structures", SlotNames = ["value", "condition"], DefaultRows = 2 },
        ];
        ByKind = All.ToDictionary(i => i.Kind);
    }

    /// <summary>Every template, in helper bar order.</summary>
    public static IReadOnlyList<MathTemplateInfo> All { get; }

    /// <summary>The description of a template kind.</summary>
    public static MathTemplateInfo Get(MathTemplateKind kind) => ByKind[kind];
}

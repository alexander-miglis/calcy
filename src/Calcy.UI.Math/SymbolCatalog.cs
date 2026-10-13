namespace Calcy.UI.Math;

/// <summary>An entry of the symbol map.</summary>
/// <param name="Glyph">What the button shows.</param>
/// <param name="Name">Tooltip / accessible name.</param>
/// <param name="Insert">Text typed into the editor (for example "π" or "sin(").</param>
/// <param name="Template">When set, the button inserts this template instead of text.</param>
public sealed record MathSymbol(string Glyph, string Name, string Insert, MathTemplateKind? Template = null);

/// <summary>A named group of the symbol map.</summary>
/// <param name="Name">Group name, such as "Greek".</param>
/// <param name="Symbols">The entries.</param>
public sealed record MathSymbolGroup(string Name, IReadOnlyList<MathSymbol> Symbols);

/// <summary>
/// The data behind the symbol map: groups Basic, Greek, Calculus, Logic, Sets, Units and Constants. Every entry
/// inserts something Calcy's parser understands; pass it to <see cref="MathEditor.InsertSymbol"/>.
/// </summary>
public static class SymbolCatalog
{
    private static MathSymbol S(string glyph, string name, string insert) => new(glyph, name, insert);

    private static MathSymbol T(string glyph, string name, MathTemplateKind kind) => new(glyph, name, "", kind);

    /// <summary>Operators, brackets and common functions.</summary>
    public static MathSymbolGroup Basic { get; } = new("Basic",
    [
        S("+", "plus", "+"), S("−", "minus", "-"), S("×", "times", "×"), S("÷", "divided by", "÷"), S("·", "multiplied by", "*"),
        T("□/□", "fraction", MathTemplateKind.Fraction), T("□ⁿ", "power", MathTemplateKind.Power), S("²", "squared", "²"),
        T("√□", "square root", MathTemplateKind.Sqrt), T("ⁿ√□", "n-th root", MathTemplateKind.NthRoot),
        T("( )", "brackets", MathTemplateKind.Parens), T("|□|", "absolute value", MathTemplateKind.Abs),
        S("=", "equals", "="), S("≠", "not equal", "≠"), S("<", "less than", "<"), S(">", "greater than", ">"),
        S("≤", "less than or equal", "≤"), S("≥", "greater than or equal", "≥"), S("!", "factorial", "!"), S("%", "percent", "%"),
        S(",", "comma", ","), S("sin", "sine", "sin("), S("cos", "cosine", "cos("), S("tan", "tangent", "tan("),
        S("sin⁻¹", "inverse sine", "asin("), S("cos⁻¹", "inverse cosine", "acos("), S("tan⁻¹", "inverse tangent", "atan("),
        S("ln", "natural logarithm", "ln("), S("log", "logarithm base 10", "log("), T("logₙ", "logarithm with a base", MathTemplateKind.LogBase),
        S("eˣ", "exponential", "e^"), S("10ˣ", "power of ten", "10^"), S("mod", "remainder", " mod "),
    ]);

    /// <summary>Greek letters (lower and upper case).</summary>
    public static MathSymbolGroup Greek { get; } = new("Greek",
    [
        S("α", "alpha", "α"), S("β", "beta", "β"), S("γ", "gamma", "γ"), S("δ", "delta", "δ"), S("ε", "epsilon", "ε"),
        S("ζ", "zeta", "ζ"), S("η", "eta", "η"), S("θ", "theta", "θ"), S("ι", "iota", "ι"), S("κ", "kappa", "κ"),
        S("λ", "lambda", "λ"), S("μ", "mu", "μ"), S("ν", "nu", "ν"), S("ξ", "xi", "ξ"), S("π", "pi", "π"),
        S("ρ", "rho", "ρ"), S("σ", "sigma", "σ"), S("τ", "tau", "τ"), S("υ", "upsilon", "υ"), S("φ", "phi", "φ"),
        S("χ", "chi", "χ"), S("ψ", "psi", "ψ"), S("ω", "omega", "ω"), S("Γ", "capital gamma", "Γ"), S("Δ", "capital delta", "Δ"),
        S("Θ", "capital theta", "Θ"), S("Λ", "capital lambda", "Λ"), S("Ξ", "capital xi", "Ξ"), S("Σ", "capital sigma", "Σ"),
        S("Φ", "capital phi", "Φ"), S("Ψ", "capital psi", "Ψ"), S("Ω", "capital omega", "Ω"),
    ]);

    /// <summary>Integrals, derivatives, sums, products and limits.</summary>
    public static MathSymbolGroup Calculus { get; } = new("Calculus",
    [
        T("∫ₐᵇ", "definite integral", MathTemplateKind.DefiniteIntegral), T("∫□dx", "indefinite integral", MathTemplateKind.Integral),
        T("d/dx", "derivative", MathTemplateKind.Derivative), S("′", "prime (derivative of a function)", "'"),
        T("Σ", "sum", MathTemplateKind.Sum), T("Π", "product", MathTemplateKind.Product), T("lim", "limit", MathTemplateKind.Limit),
        S("∞", "infinity", "∞"), S("→", "store into a variable", "→"),
    ]);

    /// <summary>Comparisons, logic words, true and false.</summary>
    public static MathSymbolGroup Logic { get; } = new("Logic",
    [
        S("and", "and", " and "), S("or", "or", " or "), S("not", "not", "not "), S("xor", "exclusive or", " xor "),
        S("true", "true", "true"), S("false", "false", "false"), S("==", "is equal to (comparison)", "=="),
        S("≠", "not equal", "≠"), S("≤", "less than or equal", "≤"), S("≥", "greater than or equal", "≥"),
        S("≔", "define", ":="), S("⇒", "function arrow", "=>"), T("{:}", "piecewise definition", MathTemplateKind.Piecewise),
    ]);

    /// <summary>Lists, ranges, vectors and matrices.</summary>
    public static MathSymbolGroup Sets { get; } = new("Sets",
    [
        T("{□}", "list", MathTemplateKind.Braces), T("[□]", "square brackets", MathTemplateKind.Brackets),
        S("..", "range a..b", ".."), T("[□□]", "matrix", MathTemplateKind.Matrix), T("[□;□]", "column vector", MathTemplateKind.Vector),
        S("len", "number of items", "len("), S("sum", "sum of a list", "sum("), S("sort", "sort a list", "sort("),
        S("min", "smallest item", "min("), S("max", "largest item", "max("), S("mean", "mean", "mean("),
        S("seq", "sequence", "seq("), S("join", "join lists", "join("),
    ]);

    /// <summary>Angle units, percent and scientific notation.</summary>
    public static MathSymbolGroup Units { get; } = new("Units",
    [
        S("°", "degrees", "°"), S("rad", "convert to radians", "rad("), S("deg", "convert to degrees", "deg("),
        S("%", "percent", "%"), S("×10ⁿ", "times ten to the power", "×10^"), S("ᴇ", "exponent marker (3ᴇ5)", "ᴇ"),
    ]);

    /// <summary>Mathematical constants and the previous answer.</summary>
    public static MathSymbolGroup Constants { get; } = new("Constants",
    [
        S("π", "pi", "π"), S("e", "Euler's number", "e"), S("i", "imaginary unit", "i"), S("∞", "infinity", "∞"),
        S("ans", "previous answer", "ans"),
    ]);

    /// <summary>Every group in symbol map order.</summary>
    public static IReadOnlyList<MathSymbolGroup> Groups { get; } = [Basic, Greek, Calculus, Logic, Sets, Units, Constants];
}

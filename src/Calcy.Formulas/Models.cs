using System.Text.Json.Serialization;

namespace Calcy.Formulas;

/// <summary>The educational level a formula is aimed at.</summary>
public enum FormulaLevel
{
    /// <summary>Secondary school (GCSE, A-level, IB, high school and similar).</summary>
    School,

    /// <summary>First years of university or college science and engineering.</summary>
    University,
}

/// <summary>A browsable group of formulas, for example "Mechanics".</summary>
public sealed record FormulaCategory
{
    /// <summary>Stable kebab-case identifier, for example <c>modern-physics</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>One or two sentences describing what the category covers.</summary>
    public string Description { get; init; } = "";

    /// <summary>A single word hinting which icon the UI should show (for example <c>atom</c>).</summary>
    public string Icon { get; init; } = "";

    /// <summary>Sort order in the category list (ascending).</summary>
    public int Order { get; init; }
}

/// <summary>A documented physical or mathematical constant.</summary>
public sealed record FormulaConstant
{
    /// <summary>Stable kebab-case identifier, for example <c>speed-of-light</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The identifier used for the constant inside formula equations, for example <c>c</c> or <c>k_B</c>.</summary>
    public required string Symbol { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Numerical value in the unit given by <see cref="Unit"/>.</summary>
    public required double Value { get; init; }

    /// <summary>SI unit symbol, or an empty string for dimensionless constants.</summary>
    public string Unit { get; init; } = "";

    /// <summary>Standard uncertainty in the same unit, or null when the value is exact or conventional.</summary>
    public double? Uncertainty { get; init; }

    /// <summary>True when the value is exact by definition (for example the 2019 SI defining constants).</summary>
    public bool Exact { get; init; }

    /// <summary>Short explanation for students.</summary>
    public string Description { get; init; } = "";

    /// <summary>Where the value comes from, for example "CODATA 2018".</summary>
    public string? Source { get; init; }

    /// <summary>Search keywords and alternative names.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>One quantity in a formula, with its meaning and unit.</summary>
public sealed record FormulaVariable
{
    /// <summary>The identifier used in the equation, for example <c>v_0</c>, <c>E_k</c> or <c>λ</c>.</summary>
    public required string Symbol { get; init; }

    /// <summary>Short display name, for example "initial velocity".</summary>
    public required string Name { get; init; }

    /// <summary>SI unit symbol, or an empty string for dimensionless quantities.</summary>
    public string Unit { get; init; } = "";

    /// <summary>One sentence explaining the quantity.</summary>
    public string Description { get; init; } = "";

    /// <summary>A sensible starting value the UI may prefill (for example g = 9.81).</summary>
    public double? DefaultValue { get; init; }

    /// <summary>Smallest meaningful value, if the quantity is bounded below.</summary>
    public double? Min { get; init; }

    /// <summary>Largest meaningful value, if the quantity is bounded above.</summary>
    public double? Max { get; init; }

    /// <summary>True when only strictly positive values make sense (lengths, masses, absolute temperatures).</summary>
    public bool Positive { get; init; }

    /// <summary>True when only whole numbers make sense (counts, term numbers, quantum numbers).</summary>
    public bool Integer { get; init; }

    /// <summary>True when the quantity has no unit.</summary>
    [JsonIgnore]
    public bool IsDimensionless => Unit.Length == 0;

    /// <summary>Checks a value against <see cref="Positive"/>, <see cref="Integer"/>, <see cref="Min"/> and <see cref="Max"/>.</summary>
    public bool Accepts(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return false;
        if (Positive && value <= 0) return false;
        if (Integer && Math.Abs(value - Math.Round(value)) > 1e-9) return false;
        if (Min is { } min && value < min) return false;
        if (Max is { } max && value > max) return false;
        return true;
    }
}

/// <summary>A worked example: a short story, the known values and the answer.</summary>
public sealed record WorkedExample
{
    /// <summary>A one or two sentence situation that motivates the calculation.</summary>
    public required string Story { get; init; }

    /// <summary>Known values keyed by variable symbol. Constants are not listed; they come from the constant table.</summary>
    public IReadOnlyDictionary<string, double> Given { get; init; } = new Dictionary<string, double>();

    /// <summary>
    /// The variable that is solved for. Null for identities, where <see cref="Result"/> is the common value of both sides.
    /// </summary>
    public string? SolveFor { get; init; }

    /// <summary>The expected answer, in <see cref="Unit"/>.</summary>
    public required double Result { get; init; }

    /// <summary>Unit of the answer (the unit of the solved variable).</summary>
    public string Unit { get; init; } = "";

    /// <summary>Absolute tolerance for comparing a computed answer with <see cref="Result"/>.</summary>
    public required double Tolerance { get; init; }

    /// <summary>Two to four short steps showing how the answer is reached.</summary>
    public IReadOnlyList<string> Steps { get; init; } = [];
}

/// <summary>A documented formula from the collection.</summary>
public sealed record Formula
{
    /// <summary>Unique kebab-case identifier, for example <c>ohms-law</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Display name, for example "Ohm's law".</summary>
    public required string Name { get; init; }

    /// <summary>Id of the <see cref="FormulaCategory"/> this formula belongs to.</summary>
    public string Category { get; init; } = "";

    /// <summary>Topic inside the category, for example "Kinematics".</summary>
    public string Subcategory { get; init; } = "";

    /// <summary>Educational level.</summary>
    public FormulaLevel Level { get; init; } = FormulaLevel.School;

    /// <summary>
    /// The formula in Calcy syntax, always an equation with exactly one <c>=</c>, for example <c>V = I*R</c>.
    /// Uses explicit <c>*</c>, the functions sqrt, root, abs, exp, ln, log, the trigonometric and hyperbolic
    /// functions, the constants pi and e, postfix <c>!</c> for factorials, and the symbols listed in
    /// <see cref="Variables"/> and <see cref="Constants"/>. Angles are in radians unless a variable's unit says otherwise.
    /// </summary>
    public required string Equation { get; init; }

    /// <summary>LaTeX for pretty display.</summary>
    public string DisplayLatex { get; init; } = "";

    /// <summary>Every variable that appears in <see cref="Equation"/>, in a natural reading order.</summary>
    public IReadOnlyList<FormulaVariable> Variables { get; init; } = [];

    /// <summary>Ids of the constants (see <see cref="FormulaLibrary.Constants"/>) that appear in the equation.</summary>
    public IReadOnlyList<string> Constants { get; init; } = [];

    /// <summary>True when the equation holds for every value of its variables (an identity such as a log rule).</summary>
    public bool IsIdentity { get; init; }

    /// <summary>Two to six sentences: what the formula says and why it is true.</summary>
    public string Explanation { get; init; } = "";

    /// <summary>One to three sentences on when the formula is the right tool.</summary>
    public string WhenToUse { get; init; } = "";

    /// <summary>Assumptions and limits of validity, for example "small angles only".</summary>
    public IReadOnlyList<string> Assumptions { get; init; } = [];

    /// <summary>A worked example that the test suite checks numerically.</summary>
    public WorkedExample? Example { get; init; }

    /// <summary>Ids of related formulas.</summary>
    public IReadOnlyList<string> Related { get; init; } = [];

    /// <summary>Search keywords, including common alternative names.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>The sign or unit convention used, where conventions differ between textbooks.</summary>
    public string? Source { get; init; }

    /// <summary>The text left of the <c>=</c> in <see cref="Equation"/>, trimmed.</summary>
    [JsonIgnore]
    public string LeftSide => SplitEquation().Left;

    /// <summary>The text right of the <c>=</c> in <see cref="Equation"/>, trimmed.</summary>
    [JsonIgnore]
    public string RightSide => SplitEquation().Right;

    /// <summary>Finds a variable by its exact symbol, or returns null.</summary>
    public FormulaVariable? GetVariable(string symbol)
    {
        foreach (var v in Variables)
            if (string.Equals(v.Symbol, symbol, StringComparison.Ordinal)) return v;
        return null;
    }

    private (string Left, string Right) SplitEquation()
    {
        int i = Equation.IndexOf('=');
        return i < 0 ? (Equation.Trim(), "") : (Equation[..i].Trim(), Equation[(i + 1)..].Trim());
    }
}

/// <summary>A search hit with its relevance score (higher is better).</summary>
public sealed record FormulaSearchHit(Formula Formula, double Score);

using System.Text;
using Calcy.Core.Numbers;
using Calcy.Core.Printing;

namespace Calcy.Units;

/// <summary>A value with a unit, for example 5 km.</summary>
public readonly record struct Quantity(BigRational Value, Unit Unit)
{
    /// <summary>"5 km", "45°".</summary>
    public string Format(NumberFormatOptions? options = null) => Unit.Format(Value, options);

    /// <inheritdoc />
    public override string ToString() => Format();
}

/// <summary>Raised for input that cannot be read or units that cannot be converted. The message is meant for the user.</summary>
public sealed class ConversionException(string message) : Exception(message);

/// <summary>
/// A parsed conversion request such as "5 km to mi", "3 ft 4 in to cm" or "170 cm to ft in".
/// The terms are added together; the targets are the requested output units (none, one, or several for a mixed result).
/// </summary>
public sealed class ConversionQuery
{
    internal ConversionQuery(IReadOnlyList<Quantity> terms, IReadOnlyList<string> numberTexts, IReadOnlyList<Unit> targets, string inputText)
    {
        Terms = terms;
        NumberTexts = numberTexts;
        Targets = targets;
        InputText = inputText;
    }

    /// <summary>The quantities to add (one for "5 km", two for "3 ft 4 in").</summary>
    public IReadOnlyList<Quantity> Terms { get; }

    /// <summary>The numbers exactly as typed, one per term ("3 1/2", "1.5e3").</summary>
    public IReadOnlyList<string> NumberTexts { get; }

    /// <summary>The requested units, largest first. Empty when the text had no "to" part.</summary>
    public IReadOnlyList<Unit> Targets { get; }

    /// <summary>The input side as typed, without the target part ("3 ft 4 in").</summary>
    public string InputText { get; }

    /// <summary>The unit of the first term.</summary>
    public Unit Unit => Terms[0].Unit;

    /// <summary>The dimension shared by every term and target.</summary>
    public Dimension Dimension => Terms[0].Unit.Dimension;

    /// <summary>True for a single value with a single unit ("5 km").</summary>
    public bool IsSimple => Terms.Count == 1;
}

/// <summary>The outcome of a conversion: the value in the target unit, the parts of a mixed result, and whether it is exact.</summary>
public sealed class ConversionResult
{
    internal ConversionResult(ConversionQuery? query, Quantity input, IReadOnlyList<Quantity> parts, BigRational value, bool isExact, UnitCategory? category)
    {
        Query = query;
        Input = input;
        Parts = parts;
        Value = value;
        IsExact = isExact;
        Category = category;
    }

    /// <summary>The parsed request, when the conversion came from text.</summary>
    public ConversionQuery? Query { get; }

    /// <summary>The input as one quantity (compound input is summed into the unit of its first term).</summary>
    public Quantity Input { get; }

    /// <summary>The first (or only) target unit.</summary>
    public Unit Target => Parts[0].Unit;

    /// <summary>The whole result expressed in <see cref="Target"/>.</summary>
    public BigRational Value { get; }

    /// <summary>The result as shown: one part normally, several for a mixed result such as 5 ft 6.929133858 in.</summary>
    public IReadOnlyList<Quantity> Parts { get; }

    /// <summary>False when the conversion needed an approximation of π (degree to radian, parsec to metre).</summary>
    public bool IsExact { get; }

    /// <summary>The category of the result's dimension.</summary>
    public UnitCategory? Category { get; }

    /// <summary>The result with its unit(s), for example "3.106855961 mi" or "5 ft 6.929133858 in".</summary>
    public string Format(NumberFormatOptions? options = null)
    {
        var sb = new StringBuilder();
        foreach (var p in Parts)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(p.Format(options));
        }
        return sb.ToString();
    }

    /// <summary>Input and result, for example "5 km = 3.106855961 mi" (with ≈ when the result is approximate).</summary>
    public string Describe(NumberFormatOptions? options = null)
    {
        string input = Query is { IsSimple: false } q ? q.InputText : Input.Format(options);
        return $"{input} {(IsExact ? "=" : "≈")} {Format(options)}";
    }

    /// <inheritdoc />
    public override string ToString() => Describe();
}

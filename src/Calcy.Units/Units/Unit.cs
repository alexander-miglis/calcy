using Calcy.Core.Numbers;
using Calcy.Core.Printing;

namespace Calcy.Units;

/// <summary>How a unit's values map to the SI base unit of its dimension.</summary>
public enum UnitKind
{
    /// <summary><c>base = value × scale</c> (metre, mile, byte, degree of angle).</summary>
    Linear,

    /// <summary><c>base = value × scale + offset</c> (degree Celsius, degree Fahrenheit).</summary>
    Affine,

    /// <summary><c>base = scale / value</c> (litres per 100 km, where more fuel means less distance).</summary>
    Reciprocal,
}

/// <summary>Which prefixes a unit symbol accepts (km, mL, kB, KiB, ...).</summary>
[Flags]
public enum UnitPrefixes
{
    /// <summary>No prefixes.</summary>
    None = 0,

    /// <summary>d, c, m, µ, n, p, f, a, z, y, r, q.</summary>
    SiSmall = 1,

    /// <summary>da and h.</summary>
    SiDecaHecto = 2,

    /// <summary>k, M, G, T, P, E, Z, Y, R, Q.</summary>
    SiThousands = 4,

    /// <summary>Every SI prefix.</summary>
    Si = SiSmall | SiDecaHecto | SiThousands,

    /// <summary>Ki, Mi, Gi, Ti, Pi, Ei, Zi, Yi (IEC binary prefixes, for bits and bytes).</summary>
    Binary = 8,
}

/// <summary>
/// A unit of measurement: symbol, name, dimension and an exact relation to the SI base unit of that dimension.
/// Instances are immutable and shared; two units are equal when they have the same id and definition.
/// </summary>
public sealed class Unit : IEquatable<Unit>
{
    internal Unit(
        string id, string symbol, string name, Dimension dimension, UnitScale scale,
        UnitKind kind = UnitKind.Linear, BigRational offset = default, UnitPrefixes prefixes = UnitPrefixes.None,
        IReadOnlyList<string>? aliases = null, bool spaceBeforeSymbol = true, bool isDerived = false, string? pluralName = null)
    {
        if (kind != UnitKind.Linear && !scale.IsRational) throw new ArgumentException("Only linear units can have a scale with π", nameof(scale));
        Id = id;
        Symbol = symbol;
        Name = name;
        PluralName = pluralName ?? UnitText.Pluralize(name);
        Dimension = dimension;
        Scale = scale;
        Kind = kind;
        Offset = offset;
        Prefixes = prefixes;
        Aliases = aliases ?? [];
        SpaceBeforeSymbol = spaceBeforeSymbol;
        IsDerived = isDerived;
    }

    /// <summary>Stable identifier, used for saving state (usually the symbol).</summary>
    public string Id { get; }

    /// <summary>Display symbol, for example "km", "°F" or "fl oz".</summary>
    public string Symbol { get; }

    /// <summary>Singular name in British spelling, for example "kilometre".</summary>
    public string Name { get; }

    /// <summary>Plural name, for example "kilometres" or "feet".</summary>
    public string PluralName { get; }

    /// <summary>The physical dimension. Only units with the same dimension convert into each other.</summary>
    public Dimension Dimension { get; }

    /// <summary>Scale relative to the SI base unit (for a reciprocal unit, the constant k in base = k / value).</summary>
    public UnitScale Scale { get; }

    /// <summary>Linear, affine or reciprocal.</summary>
    public UnitKind Kind { get; }

    /// <summary>Offset in base units for affine units (273.15 for degree Celsius), zero otherwise.</summary>
    public BigRational Offset { get; }

    /// <summary>Prefixes the symbol accepts.</summary>
    public UnitPrefixes Prefixes { get; }

    /// <summary>Other spellings accepted by the parser (names, plurals, ASCII forms).</summary>
    public IReadOnlyList<string> Aliases { get; }

    /// <summary>False for symbols written right after the number (45°, 30′).</summary>
    public bool SpaceBeforeSymbol { get; }

    /// <summary>True for units made on the fly from a prefix or a unit expression (Mm, kg/m³).</summary>
    public bool IsDerived { get; }

    /// <summary>True when the definition is an exact rational multiple of the SI base unit.</summary>
    public bool IsExact => Scale.IsRational;

    /// <summary>True when <paramref name="other"/> has the same dimension, so the two convert into each other.</summary>
    public bool IsCompatibleWith(Unit other) => Dimension == other.Dimension;

    /// <summary>Converts a value in this unit to the SI base unit. Sets <paramref name="exact"/> to false when π had to be approximated.</summary>
    public BigRational ToBase(BigRational value, ref bool exact)
    {
        switch (Kind)
        {
            case UnitKind.Affine:
                return value * Scale.Coefficient + Offset;
            case UnitKind.Reciprocal:
                if (value.IsZero) throw new ConversionException($"0 {Symbol} has no equivalent (it would be infinite)");
                return Scale.Coefficient / value;
            default:
                if (!Scale.IsRational) exact = false;
                return value * Scale.ToRational();
        }
    }

    /// <summary>Converts a value in the SI base unit to this unit.</summary>
    public BigRational FromBase(BigRational baseValue, ref bool exact)
    {
        switch (Kind)
        {
            case UnitKind.Affine:
                return (baseValue - Offset) / Scale.Coefficient;
            case UnitKind.Reciprocal:
                if (baseValue.IsZero) throw new ConversionException($"The value has no equivalent in {Symbol} (it would be infinite)");
                return Scale.Coefficient / baseValue;
            default:
                if (!Scale.IsRational) exact = false;
                return baseValue / Scale.ToRational();
        }
    }

    /// <summary>Formats a value with this unit's symbol, for example "3.106855961 mi" or "45°".</summary>
    public string Format(BigRational value, NumberFormatOptions? options = null) =>
        WithSymbol(NumberFormatter.FormatRational(value, options));

    /// <summary>Appends the symbol to an already formatted number.</summary>
    public string WithSymbol(string number) => SpaceBeforeSymbol ? $"{number} {Symbol}" : number + Symbol;

    /// <summary>"kilometre" for 1 and -1, "kilometres" otherwise.</summary>
    public string NameFor(BigRational value) => value == BigRational.One || value == BigRational.MinusOne ? Name : PluralName;

    /// <inheritdoc />
    public bool Equals(Unit? other) =>
        other is not null && (ReferenceEquals(this, other) ||
                              (Id == other.Id && Dimension == other.Dimension && Scale == other.Scale && Kind == other.Kind && Offset == other.Offset));

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Unit u && Equals(u);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Id, Dimension);

    /// <inheritdoc />
    public override string ToString() => Symbol;
}

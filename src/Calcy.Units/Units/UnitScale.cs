using Calcy.Core.Numbers;

namespace Calcy.Units;

/// <summary>
/// An exact scale factor <c>Coefficient × π^PiPower</c> relative to the SI base unit of a dimension.
/// Keeping π symbolic means degree, gradian, turn and arcminute convert into each other exactly; only a
/// conversion that changes the power of π (degree to radian, parsec to metre) needs an approximation.
/// </summary>
public readonly record struct UnitScale(BigRational Coefficient, int PiPower = 0)
{
    /// <summary>π to 100 decimal places; conversions that need it are reported as not exact.</summary>
    public static readonly BigRational Pi = BigRational.ParseDecimal(
        "3.1415926535897932384626433832795028841971693993751058209749445923078164062862089986280348253421170679");

    /// <summary>The scale of a base unit.</summary>
    public static readonly UnitScale One = new(BigRational.One);

    /// <summary>True when the scale is a plain rational number (no factor of π).</summary>
    public bool IsRational => PiPower == 0;

    /// <summary>Multiplies two scales.</summary>
    public static UnitScale operator *(UnitScale a, UnitScale b) => new(a.Coefficient * b.Coefficient, a.PiPower + b.PiPower);

    /// <summary>Divides two scales.</summary>
    public static UnitScale operator /(UnitScale a, UnitScale b) => new(a.Coefficient / b.Coefficient, a.PiPower - b.PiPower);

    /// <summary>A plain rational scale.</summary>
    public static implicit operator UnitScale(BigRational coefficient) => new(coefficient);

    /// <summary>A whole-number scale.</summary>
    public static implicit operator UnitScale(long coefficient) => new(new BigRational(coefficient));

    /// <summary>The scale raised to an integer power.</summary>
    public UnitScale Pow(int n) => new(Coefficient.Pow(n), PiPower * n);

    /// <summary>The scale as a rational number; exact when <see cref="IsRational"/>, otherwise π is approximated.</summary>
    public BigRational ToRational() => PiPower == 0 ? Coefficient : Coefficient * Pi.Pow(PiPower);

    /// <inheritdoc />
    public override string ToString() => PiPower switch
    {
        0 => Coefficient.ToString(),
        1 => $"{Coefficient}·π",
        _ => $"{Coefficient}·π^{PiPower}",
    };
}

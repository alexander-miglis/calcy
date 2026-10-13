using System.Text;

namespace Calcy.Units;

/// <summary>
/// A physical dimension as integer exponents of the base quantities (SI base quantities plus plane angle and information,
/// which are kept separate so that radians are not confused with plain numbers and bytes with counts).
/// </summary>
public readonly record struct Dimension(
    sbyte Length = 0,
    sbyte Mass = 0,
    sbyte Time = 0,
    sbyte Current = 0,
    sbyte Temperature = 0,
    sbyte Amount = 0,
    sbyte Luminosity = 0,
    sbyte Angle = 0,
    sbyte Information = 0)
{
    /// <summary>A dimensionless quantity.</summary>
    public static readonly Dimension None = default;

    /// <summary>True for a dimensionless quantity.</summary>
    public bool IsNone => this == None;

    /// <summary>The product of two dimensions (exponents add).</summary>
    public static Dimension operator *(Dimension a, Dimension b) => new(
        (sbyte)(a.Length + b.Length), (sbyte)(a.Mass + b.Mass), (sbyte)(a.Time + b.Time), (sbyte)(a.Current + b.Current),
        (sbyte)(a.Temperature + b.Temperature), (sbyte)(a.Amount + b.Amount), (sbyte)(a.Luminosity + b.Luminosity),
        (sbyte)(a.Angle + b.Angle), (sbyte)(a.Information + b.Information));

    /// <summary>The quotient of two dimensions (exponents subtract).</summary>
    public static Dimension operator /(Dimension a, Dimension b) => a * b.Pow(-1);

    /// <summary>The dimension raised to an integer power.</summary>
    public Dimension Pow(int n) => new(
        (sbyte)(Length * n), (sbyte)(Mass * n), (sbyte)(Time * n), (sbyte)(Current * n), (sbyte)(Temperature * n),
        (sbyte)(Amount * n), (sbyte)(Luminosity * n), (sbyte)(Angle * n), (sbyte)(Information * n));

    /// <summary>Readable form such as "L·T⁻¹" (or "1" when dimensionless).</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        void Part(string symbol, int exponent)
        {
            if (exponent == 0) return;
            if (sb.Length > 0) sb.Append('·');
            sb.Append(symbol);
            if (exponent != 1) sb.Append(UnitText.Superscript(exponent));
        }
        Part("L", Length);
        Part("M", Mass);
        Part("T", Time);
        Part("I", Current);
        Part("Θ", Temperature);
        Part("N", Amount);
        Part("J", Luminosity);
        Part("A", Angle);
        Part("D", Information);
        return sb.Length == 0 ? "1" : sb.ToString();
    }
}

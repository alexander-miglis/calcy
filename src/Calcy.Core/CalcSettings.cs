namespace Calcy.Core;

public enum AngleMode
{
    Radian,
    Degree,
    Gradian,
}

/// <summary>How results are presented: exact forms first (Casio style) or decimals first (Desmos style).</summary>
public enum ResultPreference
{
    ExactFirst,
    DecimalFirst,
}

public enum NumberFormatMode
{
    Float,
    Fix,
    Sci,
    Eng,
}

/// <summary>Whether implied multiplication binds tighter than division (Casio: 1/2x = 1/(2x)) or not (TI, Desmos: 1/2x = x/2).</summary>
public enum ImplicitMultiplication
{
    BindsTighter,
    LeftToRight,
}

public enum ComplexMode
{
    /// <summary>Odd roots of negative numbers stay real, even roots give complex numbers.</summary>
    Auto,

    /// <summary>Non-real results are reported as errors.</summary>
    RealOnly,

    /// <summary>Principal complex branch everywhere.</summary>
    Complex,
}

/// <summary>Calculator-wide settings. Instances are mutable; sessions share one instance.</summary>
public sealed class CalcSettings
{
    public AngleMode Angle { get; set; } = AngleMode.Radian;
    public ResultPreference Results { get; set; } = ResultPreference.ExactFirst;
    public NumberFormatMode Format { get; set; } = NumberFormatMode.Float;

    /// <summary>Significant digits for Float, decimals for Fix/Sci/Eng.</summary>
    public int Digits { get; set; } = 10;

    public ImplicitMultiplication Implicit { get; set; } = ImplicitMultiplication.BindsTighter;
    public ComplexMode Complex { get; set; } = ComplexMode.Auto;

    /// <summary>Split unknown multi-letter names into products (ab means a times b).</summary>
    public bool SplitUnknownNames { get; set; } = true;

    public CalcSettings Clone() => (CalcSettings)MemberwiseClone();

    /// <summary>Multiplier that converts the current angle unit to radians.</summary>
    public double ToRadians => Angle switch
    {
        AngleMode.Degree => Math.PI / 180,
        AngleMode.Gradian => Math.PI / 200,
        _ => 1.0,
    };
}

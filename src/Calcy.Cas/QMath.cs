using Calcy.Core.Numbers;

namespace Calcy.Cas;

/// <summary>Small helpers for exact rationals.</summary>
internal static class QMath
{
    public static BigRational Min(BigRational a, BigRational b) => a <= b ? a : b;
    public static BigRational Max(BigRational a, BigRational b) => a >= b ? a : b;
}

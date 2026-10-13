using System.Numerics;
using Calcy.Core.Numbers;

namespace Calcy.Units;

/// <summary>An SI or IEC binary prefix such as kilo (k, 10³) or kibi (Ki, 2¹⁰).</summary>
public sealed record UnitPrefix(string Symbol, string Name, BigRational Factor, UnitPrefixes Group)
{
    /// <summary>Every prefix, longest symbols first so "da" is tried before "d".</summary>
    public static readonly IReadOnlyList<UnitPrefix> All = Build();

    private static IReadOnlyList<UnitPrefix> Build()
    {
        static BigRational P10(int e) => e >= 0 ? new BigRational(BigInteger.Pow(10, e)) : new BigRational(BigInteger.One, BigInteger.Pow(10, -e));
        static BigRational P2(int e) => new(BigInteger.Pow(2, e));
        var list = new List<UnitPrefix>
        {
            new("Ki", "kibi", P2(10), UnitPrefixes.Binary),
            new("Mi", "mebi", P2(20), UnitPrefixes.Binary),
            new("Gi", "gibi", P2(30), UnitPrefixes.Binary),
            new("Ti", "tebi", P2(40), UnitPrefixes.Binary),
            new("Pi", "pebi", P2(50), UnitPrefixes.Binary),
            new("Ei", "exbi", P2(60), UnitPrefixes.Binary),
            new("Zi", "zebi", P2(70), UnitPrefixes.Binary),
            new("Yi", "yobi", P2(80), UnitPrefixes.Binary),
            new("da", "deca", P10(1), UnitPrefixes.SiDecaHecto),
            new("Q", "quetta", P10(30), UnitPrefixes.SiThousands),
            new("R", "ronna", P10(27), UnitPrefixes.SiThousands),
            new("Y", "yotta", P10(24), UnitPrefixes.SiThousands),
            new("Z", "zetta", P10(21), UnitPrefixes.SiThousands),
            new("E", "exa", P10(18), UnitPrefixes.SiThousands),
            new("P", "peta", P10(15), UnitPrefixes.SiThousands),
            new("T", "tera", P10(12), UnitPrefixes.SiThousands),
            new("G", "giga", P10(9), UnitPrefixes.SiThousands),
            new("M", "mega", P10(6), UnitPrefixes.SiThousands),
            new("k", "kilo", P10(3), UnitPrefixes.SiThousands),
            new("h", "hecto", P10(2), UnitPrefixes.SiDecaHecto),
            new("d", "deci", P10(-1), UnitPrefixes.SiSmall),
            new("c", "centi", P10(-2), UnitPrefixes.SiSmall),
            new("m", "milli", P10(-3), UnitPrefixes.SiSmall),
            new("µ", "micro", P10(-6), UnitPrefixes.SiSmall),
            new("n", "nano", P10(-9), UnitPrefixes.SiSmall),
            new("p", "pico", P10(-12), UnitPrefixes.SiSmall),
            new("f", "femto", P10(-15), UnitPrefixes.SiSmall),
            new("a", "atto", P10(-18), UnitPrefixes.SiSmall),
            new("z", "zepto", P10(-21), UnitPrefixes.SiSmall),
            new("y", "yocto", P10(-24), UnitPrefixes.SiSmall),
            new("r", "ronto", P10(-27), UnitPrefixes.SiSmall),
            new("q", "quecto", P10(-30), UnitPrefixes.SiSmall),
        };
        return list;
    }

    /// <summary>Finds a prefix by symbol. "u" is accepted for micro.</summary>
    public static UnitPrefix? FromSymbol(string symbol) =>
        All.FirstOrDefault(p => p.Symbol == (symbol == "u" ? "µ" : symbol));
}

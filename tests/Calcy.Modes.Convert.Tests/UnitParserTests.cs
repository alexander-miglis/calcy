using Calcy.Core.Numbers;

using Calcy.Units;

namespace Calcy.Modes.Convert.Tests;

/// <summary>Request parsing, compound input, prefixes, exactness, formulas and the catalog structure.</summary>
public class UnitParserTests
{
    private static readonly UnitCatalog Units = UnitCatalog.Default;

    private static BigRational D(string s) => BigRational.ParseDecimal(s);

    [Theory]
    [InlineData("5 km to mi", "km", "mi")]
    [InlineData("100 °C in °F", "°C", "°F")]
    [InlineData("60 mph to m/s", "mph", "m/s")]
    [InlineData("5 km -> m", "km", "m")]
    [InlineData("5 km → m", "km", "m")]
    [InlineData("5 km = m", "km", "m")]
    [InlineData("5 km as m", "km", "m")]
    [InlineData("5 km into m", "km", "m")]
    [InlineData("12 in in cm", "in", "cm")]
    [InlineData("2 min in s", "min", "s")]
    [InlineData("5 KM TO MI", "km", "mi")]
    [InlineData("5 kilometres to miles", "km", "mi")]
    [InlineData("10 meters to feet", "m", "ft")]
    [InlineData("1 C to mC", "C", "mC")]
    [InlineData("1 mil to mm", "thou", "mm")]
    [InlineData("1 mil to rad", "mil", "rad")]
    [InlineData("5 μm to nm", "µm", "nm")]
    [InlineData("5 um to nm", "µm", "nm")]
    [InlineData("100 C to F", "°C", "°F")]
    [InlineData("1 g0 to m/s2", "g₀", "m/s²")]
    [InlineData("3 microseconds to nanoseconds", "µs", "ns")]
    public void ParsesUnits(string request, string from, string to)
    {
        var q = Units.Parse(request);
        Assert.Equal(from, q.Unit.Symbol);
        Assert.Single(q.Targets);
        Assert.Equal(to, q.Targets[0].Symbol);
    }

    [Fact]
    public void CompoundInputIsSummed()
    {
        var q = Units.Parse("3 ft 4 in to cm");
        Assert.Equal(2, q.Terms.Count);
        Assert.Equal("3 ft 4 in", q.InputText);
        var r = Units.Evaluate(q);
        Assert.Equal(D("101.6"), r.Value);
        Assert.True(r.IsExact);
        Assert.Equal("3 ft 4 in = 101.6 cm", r.Describe());
    }

    [Theory]
    [InlineData("5' 11\" to cm", "180.34")]
    [InlineData("5 ft 11 in to cm", "180.34")]
    [InlineData("1 h 30 min 15 s to s", "5415")]
    [InlineData("2 lb 8 oz to kg", "1.133980925")]
    [InlineData("1 m 20 cm to cm", "120")]
    [InlineData("-1 h 30 min to min", "-90")]
    [InlineData("1 st 7 lb to lb", "21")]
    public void CompoundInputs(string request, string expected) => Assert.Equal(D(expected), Units.Evaluate(request).Value);

    [Fact]
    public void MixedOutputSplitsIntoParts()
    {
        var r = Units.Evaluate("170 cm to ft in");
        Assert.Equal(2, r.Parts.Count);
        Assert.Equal("ft", r.Parts[0].Unit.Symbol);
        Assert.Equal(new BigRational(5), r.Parts[0].Value);
        UnitConversionTests.AssertClose("6.929133858267717", r.Parts[1].Value);
        Assert.Equal("5 ft 6.929133858 in", r.Format());

        var t = Units.Evaluate("5415 s to h min s");
        Assert.Equal(["1 h", "30 min", "15 s"], t.Parts.Select(p => p.Format()));

        var negative = Units.Evaluate("-90 min to h min");
        Assert.Equal("-1 h 30 min", negative.Format());
    }

    [Theory]
    [InlineData("1,000 m to km", "1")]
    [InlineData("1.5e3 m to km", "1.5")]
    [InlineData("1.5E-3 km to m", "1.5")]
    [InlineData("1.5×10^3 m to km", "1.5")]
    [InlineData("1.5×10³ m to km", "1.5")]
    [InlineData("2 x 10^3 m to km", "2")]
    [InlineData("−40 °C to °F", "-40")]
    [InlineData(".5 km to m", "500")]
    [InlineData("3/4 mi to ft", "3960")]
    [InlineData("3 1/2 ft to in", "42")]
    [InlineData("1¼ in to mm", "31.75")]
    [InlineData("5 kms to m", "5000")]
    [InlineData("5 km to mi?", "3.106855961186669")]
    public void Numbers(string request, string expected) => UnitConversionTests.AssertClose(expected, Units.Evaluate(request).Value);

    [Theory]
    [InlineData("5", "Add a unit")]
    [InlineData("km to mi", "Start with a number")]
    [InlineData("5 foo to m", "Unknown unit: foo")]
    [InlineData("5 km to bar2x", "Unknown unit: bar2x")]
    [InlineData("5 km to kg", "cannot be converted")]
    [InlineData("3 ft 2 kg", "cannot be added")]
    [InlineData("10 °C 5 °C to K", "cannot be added")]
    [InlineData("3,5 m to cm", "point for decimals")]
    [InlineData("", "Type a value")]
    [InlineData("0 L/100km to mpg", "no equivalent")]
    public void ErrorsAreExplained(string request, string message)
    {
        Assert.False(Units.TryEvaluate(request, out _, out var error));
        Assert.Contains(message, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QueryWithoutTargetStaysInItsUnit()
    {
        var r = Units.Evaluate("3 ft 6 in");
        Assert.Equal("ft", r.Target.Symbol);
        Assert.Equal(D("3.5"), r.Value);
    }

    // ---- Exactness ----

    [Fact]
    public void DefinedConversionsAreExact()
    {
        Assert.Equal(new BigRational(254, 100), Units.Evaluate("1 in to cm").Value);
        Assert.Equal(D("0.45359237"), Units.Evaluate("1 lb to kg").Value);
        Assert.Equal(D("0.3048"), Units.Evaluate("1 ft to m").Value);
        Assert.Equal(D("1609.344"), Units.Evaluate("1 mi to m").Value);
        Assert.Equal(D("3.785411784"), Units.Evaluate("1 gal to L").Value);
        Assert.Equal(D("4.4482216152605"), Units.Evaluate("1 lbf to N").Value);
        Assert.Equal(new BigRational(1, 3), Units.Evaluate("1 ft to yd").Value);
        Assert.Equal(new BigRational(5000, 1609344) * 1000, Units.Evaluate("5 km to mi").Value);
        Assert.Equal(new BigRational(212), Units.Evaluate("100 °C to °F").Value);
        Assert.Equal(new BigRational(1, 360), Units.Evaluate("1 ° to turn").Value);
        Assert.True(Units.Evaluate("1 ° to turn").IsExact);
        Assert.True(Units.Evaluate("100 °C to °F").IsExact);
        Assert.True(Units.Evaluate("1 GiB to GB").IsExact);
    }

    [Fact]
    public void PiConversionsAreFlaggedApproximate()
    {
        var r = Units.Evaluate("180 ° to rad");
        Assert.False(r.IsExact);
        UnitConversionTests.AssertClose("3.14159265358979323846264338327950288", r.Value, 1e-30);
        Assert.False(Units.Evaluate("1 pc to m").IsExact);
        Assert.StartsWith("180° ≈ 3.141592654 rad", r.Describe());
    }

    [Fact]
    public void ExactRoundTripsInEveryCategory()
    {
        foreach (var category in Units.Categories)
        {
            foreach (var from in category.Units)
            {
                foreach (var to in category.CompatibleWith(from))
                {
                    var value = new BigRational(37, 7);
                    bool exact = true;
                    var there = Units.ConvertValue(value, from, to, ref exact);
                    var back = Units.ConvertValue(there, to, from, ref exact);
                    if (exact) Assert.Equal(value, back);
                    else UnitConversionTests.AssertClose((value.ToDouble()).ToString("R", System.Globalization.CultureInfo.InvariantCulture), back, 1e-14);
                }
            }
        }
    }

    // ---- Prefixes and names ----

    [Theory]
    [InlineData("km", "kilometre", "1000")]
    [InlineData("Mm", "megametre", "1000000")]
    [InlineData("µm", "micrometre", "0.000001")]
    [InlineData("nm", "nanometre", "0.000000001")]
    [InlineData("mg", "milligram", "0.000001")]
    [InlineData("GW", "gigawatt", "1000000000")]
    [InlineData("kPa", "kilopascal", "1000")]
    [InlineData("MHz", "megahertz", "1000000")]
    [InlineData("KiB", "kibibyte", "1024")]
    [InlineData("Mibit", "mebibit", "131072")]
    [InlineData("dL", "decilitre", "0.0001")]
    [InlineData("hPa", "hectopascal", "100")]
    [InlineData("kWh", "kilowatt hour", "3600000")]
    [InlineData("mAh", "milliampere hour", "3.6")]
    public void PrefixesScaleTheBaseUnit(string symbol, string name, string toBase)
    {
        var u = Units[symbol];
        Assert.Equal(symbol, u.Symbol);
        Assert.Equal(name, u.Name);
        Assert.Equal(D(toBase), u.Scale.ToRational());
    }

    [Fact]
    public void DataPrefixesStayLarge()
    {
        Assert.Null(Units.Find("dB"));
        Assert.Null(Units.Find("cbit"));
        Assert.Equal("MB", Units.Find("MB")!.Symbol);
        Assert.Equal("mbar", Units.Find("mb")!.Symbol);
    }

    [Fact]
    public void PluralsAndSpellings()
    {
        Assert.Equal("feet", Units["ft"].PluralName);
        Assert.Equal("inches", Units["in"].PluralName);
        Assert.Equal("miles per hour", Units["mph"].PluralName);
        Assert.Equal("degrees Celsius", Units["°C"].PluralName);
        Assert.Equal("pounds-force", Units["lbf"].PluralName);
        Assert.Equal("centuries", Units["century"].PluralName);
        Assert.Equal("inches of mercury", Units["inHg"].PluralName);
        Assert.Same(Units["km"], Units["kilometers"]);
        Assert.Same(Units["L"], Units["liters"]);
        Assert.Equal("kilometres", Units["km"].NameFor(new BigRational(5)));
        Assert.Equal("kilometre", Units["km"].NameFor(BigRational.One));
    }

    [Fact]
    public void UnitExpressionsBuildDimensions()
    {
        var u = Units["kg/m^3"];
        Assert.Equal("kg/m³", u.Symbol);
        Assert.Equal(new Dimension(Length: -3, Mass: 1), u.Dimension);
        var n = Units["N*m"];
        Assert.Equal("N·m", n.Symbol);
        Assert.Equal(Units["J"].Dimension, n.Dimension);
        Assert.Equal("metre per second squared", Units["m/s2"].Name);
        Assert.Same(Units["mph"], Units["mi/h"]);
        Assert.Equal("W/(m²·K)", Units["W/m^2/K"].Symbol);
    }

    // ---- Formulas ----

    [Fact]
    public void Formulas()
    {
        Assert.Equal("1 km = 0.6213711922 mi", Units.Formula(Units["km"], Units["mi"]));
        Assert.Equal("°F = °C × 9/5 + 32", Units.Formula(Units["°C"], Units["°F"]));
        Assert.Equal("°C = (°F - 32) × 5/9", Units.Formula(Units["°F"], Units["°C"]));
        Assert.Equal("K = °C + 273.15", Units.Formula(Units["°C"], Units["K"]));
        Assert.Equal("°C = K - 273.15", Units.Formula(Units["K"], Units["°C"]));
        Assert.Equal("K = (°F + 459.67) × 5/9", Units.Formula(Units["°F"], Units["K"]));
        Assert.Equal("1 GiB = 1.073741824 GB", Units.Formula(Units["GiB"], Units["GB"]));
        Assert.Equal("mpg = 235.2145833 / (L/100km)", Units.Formula(Units["L/100km"], Units["mpg"]));
        Assert.Equal("L/100km = 235.2145833 / mpg", Units.Formula(Units["mpg"], Units["L/100km"]));
        Assert.Equal("1° ≈ 0.01745329252 rad", Units.Formula(Units["°"], Units["rad"]));
        Assert.Equal("", Units.Formula(Units["km"], Units["kg"]));
    }

    // ---- Fuel economy inversion ----

    [Theory]
    [InlineData("4.5")]
    [InlineData("7.8")]
    [InlineData("12")]
    [InlineData("23.5")]
    public void FuelEconomyInverts(string litresPer100)
    {
        var l = Units["L/100km"];
        var mpg = Units["mpg"];
        var v = D(litresPer100);
        var m = Units.Convert(v, l, mpg).Value;
        Assert.Equal(v, Units.Convert(m, mpg, l).Value);
        // L/100km × mpg is the constant 235.2145833...
        Assert.Equal(D("378541.1784") / D("1609.344"), v * m);
        // Twice the fuel use is half the distance per gallon.
        Assert.Equal(m / 2, Units.Convert(v * 2, l, mpg).Value);
    }

    // ---- Catalog ----

    [Fact]
    public void CategoriesAreComplete()
    {
        string[] expected =
        [
            "Length", "Area", "Volume", "Mass", "Time", "Speed", "Acceleration", "Force", "Pressure", "Energy", "Power",
            "Temperature", "Angle", "Frequency", "Data", "Density", "Fuel economy", "Electric", "Cooking",
        ];
        Assert.Equal(expected, Units.Categories.Select(c => c.Name));
        foreach (var c in Units.Categories)
        {
            Assert.Contains(c.DefaultFrom, c.Units);
            Assert.Contains(c.DefaultTo, c.Units);
            Assert.True(c.DefaultFrom.IsCompatibleWith(c.DefaultTo), c.Name);
            Assert.Equal(c.Units.Count, c.Units.Select(u => u.Symbol).Distinct().Count());
            foreach (var u in c.Units) Assert.Same(u, Units.GetById(u.Id));
        }
        Assert.Equal(4, Units.GetCategory("electric")!.Dimensions.Count);
        Assert.Equal("Volume", Units.CategoryOf(Units["tsp"])!.Name);
        Assert.Equal("Cooking", Units.CategoryOf(Units["metric cup"])!.Name);
    }

    [Fact]
    public void EveryListedUnitResolvesFromItsSymbol()
    {
        foreach (var c in Units.Categories)
            foreach (var u in c.Units)
                Assert.Contains(u, Units.FindCandidates(u.Symbol));
    }

    [Fact]
    public void SearchFindsUnits()
    {
        Assert.Equal("psi", Units.Search("psi")[0].Symbol);
        Assert.Contains(Units.Search("kilomet"), u => u.Symbol == "km");
        Assert.Contains(Units.Search("gall"), u => u.Symbol == "gal");
        Assert.Contains(Units.Search("fahr"), u => u.Symbol == "°F");
        Assert.Equal("Mm", Units.Search("Mm")[0].Symbol);
        Assert.Empty(Units.Search("zzzz"));
    }

    [Fact]
    public void NumbersParse()
    {
        Assert.True(UnitCatalog.TryParseNumber("3 1/2", out var v));
        Assert.Equal(new BigRational(7, 2), v);
        Assert.True(UnitCatalog.TryParseNumber(" -1,234.5 ", out v));
        Assert.Equal(D("-1234.5"), v);
        Assert.True(UnitCatalog.TryParseNumber("½", out v));
        Assert.Equal(BigRational.Half, v);
        Assert.False(UnitCatalog.TryParseNumber("1/0", out _));
        Assert.False(UnitCatalog.TryParseNumber("5 km", out _));
        Assert.False(UnitCatalog.TryParseNumber("1e99999", out _));
    }
}

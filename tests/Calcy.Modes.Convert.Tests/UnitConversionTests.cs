using Calcy.Core.Numbers;

using Calcy.Units;

namespace Calcy.Modes.Convert.Tests;

/// <summary>Conversions against known values, one category at a time.</summary>
public class UnitConversionTests
{
    private static readonly UnitCatalog Units = UnitCatalog.Default;

    internal static void AssertClose(string expected, BigRational actual, double relative = 1e-12)
    {
        var e = BigRational.ParseDecimal(expected);
        var diff = (actual - e).Abs();
        var scale = e.IsZero ? BigRational.One : e.Abs();
        Assert.True(diff <= scale * BigRational.FromDouble(relative),
            $"Expected {expected}, got {actual.ToDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture)} ({actual})");
    }

    private static void Check(string request, string expected)
    {
        var result = Units.Evaluate(request);
        AssertClose(expected, result.Value);
    }

    [Theory]
    [InlineData("1 in to cm", "2.54")]
    [InlineData("1 ft to m", "0.3048")]
    [InlineData("1 yd to m", "0.9144")]
    [InlineData("1 mi to km", "1.609344")]
    [InlineData("1 nmi to m", "1852")]
    [InlineData("5 km to mi", "3.106855961186669")]
    [InlineData("1 m to ft", "3.280839895013123")]
    [InlineData("1 m to in", "39.37007874015748")]
    [InlineData("12 in to ft", "1")]
    [InlineData("1 au to km", "149597870.7")]
    [InlineData("1 ly to km", "9460730472580.8")]
    [InlineData("1 thou to mm", "0.0254")]
    [InlineData("1 fur to m", "201.168")]
    [InlineData("1 ch to ft", "66")]
    [InlineData("1 ftm to ft", "6")]
    [InlineData("1 Å to nm", "0.1")]
    [InlineData("1 µm to nm", "1000")]
    [InlineData("1 Mm to km", "1000")]
    [InlineData("1 pc to ly", "3.261563777167433")]
    [InlineData("1 pc to au", "206264.8062470964")]
    public void Length(string request, string expected) => Check(request, expected);

    [Theory]
    [InlineData("1 acre to m²", "4046.8564224")]
    [InlineData("1 ha to acre", "2.471053814671653")]
    [InlineData("1 mi² to km²", "2.589988110336")]
    [InlineData("1 ft² to in²", "144")]
    [InlineData("1 m² to ft²", "10.76391041670972")]
    [InlineData("1 km² to ha", "100")]
    [InlineData("1 yd² to m²", "0.83612736")]
    [InlineData("1 sq ft to cm²", "929.0304")]
    [InlineData("1 m^2 to cm^2", "10000")]
    [InlineData("2 square feet to square inches", "288")]
    public void Area(string request, string expected) => Check(request, expected);

    [Theory]
    [InlineData("1 gal to L", "3.785411784")]
    [InlineData("1 gal UK to L", "4.54609")]
    [InlineData("1 cup to mL", "236.5882365")]
    [InlineData("1 tbsp to mL", "14.78676478125")]
    [InlineData("1 tsp to mL", "4.92892159375")]
    [InlineData("1 fl oz to mL", "29.5735295625")]
    [InlineData("1 pt to mL", "473.176473")]
    [InlineData("1 qt to L", "0.946352946")]
    [InlineData("1 bbl to L", "158.987294928")]
    [InlineData("1 ft³ to L", "28.316846592")]
    [InlineData("1 in³ to cm³", "16.387064")]
    [InlineData("1 m³ to L", "1000")]
    [InlineData("1 gal to cup", "16")]
    [InlineData("1 tbsp to tsp", "3")]
    [InlineData("1 cup to tbsp", "16")]
    [InlineData("1 pt UK to mL", "568.26125")]
    [InlineData("1 fl oz UK to mL", "28.4130625")]
    [InlineData("1 cc to mL", "1")]
    [InlineData("1 cubic foot to litres", "28.316846592")]
    public void Volume(string request, string expected) => Check(request, expected);

    [Theory]
    [InlineData("1 lb to kg", "0.45359237")]
    [InlineData("1 oz to g", "28.349523125")]
    [InlineData("1 st to lb", "14")]
    [InlineData("1 st to kg", "6.35029318")]
    [InlineData("1 kg to lb", "2.204622621848776")]
    [InlineData("1 t to kg", "1000")]
    [InlineData("1 ton to kg", "907.18474")]
    [InlineData("1 ton UK to kg", "1016.0469088")]
    [InlineData("1 oz t to g", "31.1034768")]
    [InlineData("1 gr to mg", "64.79891")]
    [InlineData("1 ct to g", "0.2")]
    [InlineData("1 slug to kg", "14.59390293720636")]
    [InlineData("1 mg to µg", "1000")]
    [InlineData("1 kt to t", "1000")]
    public void Mass(string request, string expected) => Check(request, expected);

    [Theory]
    [InlineData("1 h to s", "3600")]
    [InlineData("1 d to h", "24")]
    [InlineData("1 wk to d", "7")]
    [InlineData("1 yr to d", "365.25")]
    [InlineData("1 mo to d", "30.4375")]
    [InlineData("1 h 30 min to min", "90")]
    [InlineData("90 min to h", "1.5")]
    [InlineData("1 ms to µs", "1000")]
    [InlineData("1 century to yr", "100")]
    [InlineData("1 fortnight to d", "14")]
    [InlineData("1 decade to mo", "120")]
    public void Time(string request, string expected) => Check(request, expected);

    [Theory]
    [InlineData("60 mph to m/s", "26.8224")]
    [InlineData("100 km/h to mph", "62.13711922373339")]
    [InlineData("1 kn to km/h", "1.852")]
    [InlineData("1 m/s to km/h", "3.6")]
    [InlineData("1 ft/s to m/s", "0.3048")]
    [InlineData("1 c to km/s", "299792.458")]
    [InlineData("1 mi/h to km/h", "1.609344")]
    [InlineData("10 m/s to ft/s", "32.80839895013123")]
    [InlineData("1 km per h to m/s", "0.2777777777777778")]
    public void Speed(string request, string expected) => Check(request, expected);

    [Theory]
    [InlineData("1 g₀ to m/s²", "9.80665")]
    [InlineData("1 g0 to ft/s2", "32.17404855643045")]
    [InlineData("1 Gal to m/s²", "0.01")]
    [InlineData("1 m/s^2 to Gal", "100")]
    [InlineData("1000 mGal to Gal", "1")]
    public void Acceleration(string request, string expected) => Check(request, expected);

    [Theory]
    [InlineData("1 lbf to N", "4.4482216152605")]
    [InlineData("1 kgf to N", "9.80665")]
    [InlineData("1 kN to lbf", "224.8089430997105")]
    [InlineData("1 dyn to N", "0.00001")]
    [InlineData("1 kip to lbf", "1000")]
    [InlineData("1 pdl to N", "0.138254954376")]
    [InlineData("16 ozf to lbf", "1")]
    [InlineData("1 tf to kN", "9.80665")]
    public void Force(string request, string expected) => Check(request, expected);

    [Theory]
    [InlineData("1 atm to Pa", "101325")]
    [InlineData("1 bar to psi", "14.50377377302092")]
    [InlineData("1 psi to kPa", "6.894757293168361")]
    [InlineData("1 atm to Torr", "760")]
    [InlineData("1 bar to hPa", "1000")]
    [InlineData("1 mbar to Pa", "100")]
    [InlineData("1 at to kPa", "98.0665")]
    [InlineData("1 inHg to mmHg", "25.4")]
    [InlineData("1 ksi to psi", "1000")]
    [InlineData("1 MPa to bar", "10")]
    [InlineData("1 cmH2O to Pa", "98.0665")]
    [InlineData("1 inH2O to Pa", "249.08891")]
    [InlineData("1 psf to Pa", "47.88025898033584")]
    [InlineData("1 kgf/cm2 to bar", "0.980665")]
    public void Pressure(string request, string expected) => Check(request, expected);

    [Theory]
    [InlineData("1 kcal to kJ", "4.184")]
    [InlineData("1 kWh to MJ", "3.6")]
    [InlineData("1 BTU to J", "1055.05585262")]
    [InlineData("1 eV to J", "1.602176634e-19")]
    [InlineData("1 Wh to J", "3600")]
    [InlineData("1 erg to J", "1e-7")]
    [InlineData("1 thm to MJ", "105.4804")]
    [InlineData("1 ft·lbf to J", "1.3558179483314004")]
    [InlineData("1 tTNT to GJ", "4.184")]
    [InlineData("1 MeV to keV", "1000")]
    [InlineData("1 J to eV", "6.241509074460763e18")]
    [InlineData("1 Cal to cal", "1000")]
    public void Energy(string request, string expected) => Check(request, expected);

    [Theory]
    [InlineData("1 hp to W", "745.69987158227022")]
    [InlineData("1 PS to W", "735.49875")]
    [InlineData("1 kW to hp", "1.341022089595028")]
    [InlineData("1 BTU/h to W", "0.2930710701722222")]
    [InlineData("1 TR to kW", "3.516852842066667")]
    [InlineData("1 MW to kW", "1000")]
    [InlineData("1 kcal/h to W", "1.162222222222222")]
    public void Power(string request, string expected) => Check(request, expected);

    [Theory]
    [InlineData("100 °C to °F", "212")]
    [InlineData("0 °C to K", "273.15")]
    [InlineData("-40 °C to °F", "-40")]
    [InlineData("32 °F to °C", "0")]
    [InlineData("98.6 °F to °C", "37")]
    [InlineData("0 K to °C", "-273.15")]
    [InlineData("0 K to °F", "-459.67")]
    [InlineData("491.67 °R to °C", "0")]
    [InlineData("300 K to °R", "540")]
    [InlineData("20 °C to °R", "527.67")]
    [InlineData("212 degF in degC", "100")]
    [InlineData("100 C to F", "212")]
    [InlineData("-459.67 °F to °R", "0")]
    public void Temperature(string request, string expected) => Check(request, expected);

    [Theory]
    [InlineData("1 turn to °", "360")]
    [InlineData("90 ° to grad", "100")]
    [InlineData("1 ° to ′", "60")]
    [InlineData("1 ′ to ″", "60")]
    [InlineData("45° 30′ to °", "45.5")]
    [InlineData("1 turn to mil", "6400")]
    [InlineData("1 rad to mrad", "1000")]
    [InlineData("2 grad to arcmin", "108")]
    [InlineData("180 ° to rad", "3.141592653589793")]
    [InlineData("1 rad to °", "57.29577951308232")]
    public void Angle(string request, string expected) => Check(request, expected);

    [Theory]
    [InlineData("60 rpm to Hz", "1")]
    [InlineData("1 kHz to Hz", "1000")]
    [InlineData("1 GHz to MHz", "1000")]
    [InlineData("1 Hz to rpm", "60")]
    public void Frequency(string request, string expected) => Check(request, expected);

    [Theory]
    [InlineData("1 GiB to GB", "1.073741824")]
    [InlineData("1 KiB to B", "1024")]
    [InlineData("1 MiB to KiB", "1024")]
    [InlineData("1 GB to MB", "1000")]
    [InlineData("1 B to bit", "8")]
    [InlineData("1 kB to bit", "8000")]
    [InlineData("1 Mbit to kB", "125")]
    [InlineData("1 TiB to GiB", "1024")]
    [InlineData("1 Gb to MB", "125")]
    [InlineData("1 TB to TiB", "0.9094947017729282")]
    [InlineData("1 KB to B", "1000")]
    [InlineData("1 PiB to TiB", "1024")]
    [InlineData("1 EB to PB", "1000")]
    [InlineData("1 kibibyte to bytes", "1024")]
    [InlineData("2 gigabytes to megabits", "16000")]
    public void Data(string request, string expected) => Check(request, expected);

    [Theory]
    [InlineData("1 g/cm³ to kg/m³", "1000")]
    [InlineData("1 g/mL to kg/L", "1")]
    [InlineData("1 lb/ft³ to kg/m³", "16.01846337396014")]
    [InlineData("1 kg/m^3 to g/L", "1")]
    [InlineData("1 lb/gal to kg/L", "0.1198264273168966")]
    [InlineData("1 t/m3 to g/cm3", "1")]
    [InlineData("10 kg/m3 to g/cm3", "0.01")]
    public void Density(string request, string expected) => Check(request, expected);

    [Theory]
    [InlineData("10 L/100km to mpg", "23.52145833333333")]
    [InlineData("30 mpg to L/100km", "7.840486111111111")]
    [InlineData("10 km/L to L/100km", "10")]
    [InlineData("1 mpg UK to mpg", "0.8326741846289889")]
    [InlineData("1 L/100km to gal/100mi", "0.4251437074302720")]
    [InlineData("50 mpg to km/L", "21.2571853715136")]
    public void FuelEconomy(string request, string expected) => Check(request, expected);

    [Theory]
    [InlineData("1 Ah to C", "3600")]
    [InlineData("1000 mAh to Ah", "1")]
    [InlineData("1 mA to µA", "1000")]
    [InlineData("1 kV to V", "1000")]
    [InlineData("1 MΩ to kΩ", "1000")]
    [InlineData("4.7 kohm to Ω", "4700")]
    [InlineData("1 C to e", "6.241509074460763e18")]
    [InlineData("1 µC to nC", "1000")]
    public void Electric(string request, string expected) => Check(request, expected);

    [Theory]
    [InlineData("1 metric cup to metric tbsp", "16.66666666666667")]
    [InlineData("3 1/2 cups to mL", "828.05882775")]
    [InlineData("½ cup to tbsp", "8")]
    [InlineData("1 tbsp metric to mL", "15")]
    [InlineData("1 metric tsp to mL", "5")]
    [InlineData("1½ tsp to mL", "7.393382390625")]
    public void Cooking(string request, string expected) => Check(request, expected);
}

using Calcy.Core.Numbers;

namespace Calcy.Units;

public sealed partial class UnitCatalog
{
    // Base dimensions and the derived ones used below.
    private static readonly Dimension Len = new(Length: 1);
    private static readonly Dimension Mas = new(Mass: 1);
    private static readonly Dimension Tim = new(Time: 1);
    private static readonly Dimension Cur = new(Current: 1);
    private static readonly Dimension Tmp = new(Temperature: 1);
    private static readonly Dimension Ang = new(Angle: 1);
    private static readonly Dimension Inf = new(Information: 1);
    private static readonly Dimension Area = Len.Pow(2);
    private static readonly Dimension Vol = Len.Pow(3);
    private static readonly Dimension Spd = Len / Tim;
    private static readonly Dimension Acc = Len / Tim.Pow(2);
    private static readonly Dimension Frc = Mas * Acc;
    private static readonly Dimension Prs = Frc / Area;
    private static readonly Dimension Nrg = Frc * Len;
    private static readonly Dimension Pwr = Nrg / Tim;
    private static readonly Dimension Frq = Tim.Pow(-1);
    private static readonly Dimension Fuel = Len / Vol;
    private static readonly Dimension Chg = Cur * Tim;
    private static readonly Dimension Vlt = Pwr / Cur;
    private static readonly Dimension Res = Vlt / Cur;

    private static BigRational R(string decimalText) => BigRational.ParseDecimal(decimalText);

    private static BigRational Q(long numerator, long denominator = 1) => new(numerator, denominator);

    private Unit Add(
        string symbol, string name, Dimension dimension, UnitScale scale, string aliases = "",
        UnitPrefixes prefixes = UnitPrefixes.None, UnitKind kind = UnitKind.Linear, BigRational offset = default,
        bool space = true, string preferred = "", string prefixable = "", string? plural = null)
    {
        static string[] Split(string s) => s.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var unit = new Unit(symbol, symbol, name, dimension, scale, kind, offset, prefixes, Split(aliases), space, pluralName: plural);
        return Register(unit, Split(preferred), Split(prefixable));
    }

    private void DefineUnits()
    {
        // Exact definitions (international yard and pound of 1959, US customary, imperial, SI 2019).
        var inch = R("0.0254");
        var foot = R("0.3048");
        var yard = R("0.9144");
        var mile = R("1609.344");
        var lb = R("0.45359237");
        var oz = lb / 16;
        var grain = lb / 7000;
        var gn = R("9.80665");
        var lbf = lb * gn;
        var usGallon = inch.Pow(3) * 231;
        var ukGallon = R("0.00454609");
        var julianYear = R("31557600");
        var c = new BigRational(299792458);
        var au = new BigRational(149597870700);
        var calorie = R("4.184");
        var btu = R("1055.05585262");
        var electronvolt = R("1.602176634e-19");
        var mmHg = R("133.322387415");

        // Length
        Add("m", "metre", Len, 1, prefixes: UnitPrefixes.Si);
        Add("in", "inch", Len, inch, "\"|''|in.|inches");
        Add("ft", "foot", Len, foot, "'|ft.|feet");
        Add("yd", "yard", Len, yard, "yds");
        Add("mi", "mile", Len, mile, "statute mile|statute miles");
        Add("nmi", "nautical mile", Len, 1852, "NM|nmile|nautical miles");
        Add("thou", "thou", Len, inch / 1000, "mil|mils", plural: "thou");
        Add("fur", "furlong", Len, yard * 220);
        Add("ch", "chain", Len, yard * 22);
        Add("ftm", "fathom", Len, foot * 6);
        Add("Å", "ångström", Len, R("1e-10"), "angstrom|angstroms|Angstrom");
        Add("au", "astronomical unit", Len, au, "AU|ua");
        Add("ly", "light-year", Len, c * julianYear, "light year|light years|lightyear|lightyears");
        Add("pc", "parsec", Len, new UnitScale(au * 648000, -1), prefixes: UnitPrefixes.SiThousands);

        // Area (square units such as km² come from unit expressions)
        Add("ha", "hectare", Area, 10000);
        Add("acre", "acre", Area, R("4046.8564224"), "ac");

        // Volume
        Add("L", "litre", Vol, R("0.001"), "l|ℓ", UnitPrefixes.Si, prefixable: "l");
        Add("tsp", "teaspoon", Vol, usGallon / 768, "teaspoon (US)|tsp US|tsps");
        Add("tbsp", "tablespoon", Vol, usGallon / 256, "Tbsp|tbs|tbl|tablespoon (US)|tbsp US");
        Add("fl oz", "fluid ounce", Vol, usGallon / 128, "floz|fl. oz.|fluid ounce (US)|fl oz US|US fl oz");
        Add("cup", "cup", Vol, usGallon / 16, "cup (US)|US cup");
        Add("pt", "pint", Vol, usGallon / 8, "pint (US)|US pint|pt US");
        Add("qt", "quart", Vol, usGallon / 4, "quart (US)|US quart|qt US");
        Add("gal", "gallon", Vol, usGallon, "gallon (US)|US gal|gal US|US gallon");
        Add("gal UK", "imperial gallon", Vol, ukGallon, "UK gal|imp gal|gallon (UK)|UK gallon");
        Add("pt UK", "imperial pint", Vol, ukGallon / 8, "UK pint|imp pt|pint (UK)");
        Add("fl oz UK", "imperial fluid ounce", Vol, ukGallon / 160, "UK fl oz|imp fl oz|fluid ounce (UK)");
        Add("bbl", "barrel (oil)", Vol, usGallon * 42, "barrel|oil barrel");
        Add("metric tsp", "metric teaspoon", Vol, R("0.000005"), "tsp metric|metric teaspoons");
        Add("metric tbsp", "metric tablespoon", Vol, R("0.000015"), "tbsp metric");
        Add("metric cup", "metric cup", Vol, R("0.00025"), "cup metric");

        // Mass
        Add("g", "gram", Mas, R("0.001"), "gramme|grammes", UnitPrefixes.Si);
        Add("t", "tonne", Mas, 1000, "metric ton|metric tons", UnitPrefixes.SiThousands);
        Add("lb", "pound", Mas, lb, "lbs|lbm");
        Add("oz", "ounce", Mas, oz);
        Add("st", "stone", Mas, lb * 14);
        Add("gr", "grain", Mas, grain);
        Add("ct", "carat", Mas, R("0.0002"));
        Add("oz t", "troy ounce", Mas, grain * 480, "ozt|troy oz|oz troy");
        Add("ton", "short ton", Mas, lb * 2000, "US ton|ton US|short tons|tn");
        Add("ton UK", "long ton", Mas, lb * 2240, "UK ton|long tons|imperial ton");
        Add("slug", "slug", Mas, lbf / foot);

        // Time
        Add("s", "second", Tim, 1, "sec|secs", UnitPrefixes.Si);
        Add("min", "minute", Tim, 60, "mins");
        Add("h", "hour", Tim, 3600, "hr|hrs");
        Add("d", "day", Tim, 86400);
        Add("wk", "week", Tim, 604800, "wks");
        Add("fortnight", "fortnight", Tim, 1209600);
        Add("mo", "month", Tim, julianYear / 12, "mos|month (average)");
        Add("yr", "year", Tim, julianYear, "a|y|yrs|annum|Julian year");
        Add("decade", "decade", Tim, julianYear * 10);
        Add("century", "century", Tim, julianYear * 100);

        // Speed
        Add("m/s", "metre per second", Spd, 1, "mps");
        Add("km/h", "kilometre per hour", Spd, Q(5, 18), "kph|kmh|kmph|km/hr|kilometres an hour");
        Add("mph", "mile per hour", Spd, mile / 3600, "mi/h|MPH|miles an hour");
        Add("kn", "knot", Spd, Q(1852, 3600), "kts|knots");
        Add("ft/s", "foot per second", Spd, foot, "fps");
        Add("c", "speed of light", Spd, c);

        // Acceleration
        Add("m/s²", "metre per second squared", Acc, 1);
        Add("g₀", "standard gravity", Acc, gn, "g0|gn|gee|G");
        Add("Gal", "galileo", Acc, R("0.01"), prefixes: UnitPrefixes.SiSmall);

        // Force
        Add("N", "newton", Frc, 1, prefixes: UnitPrefixes.Si);
        Add("dyn", "dyne", Frc, R("1e-5"));
        Add("kgf", "kilogram-force", Frc, gn, "kp");
        Add("tf", "tonne-force", Frc, gn * 1000);
        Add("lbf", "pound-force", Frc, lbf);
        Add("ozf", "ounce-force", Frc, lbf / 16);
        Add("kip", "kip", Frc, lbf * 1000, "kipf|klbf");
        Add("pdl", "poundal", Frc, lb * foot);

        // Pressure
        Add("Pa", "pascal", Prs, 1, prefixes: UnitPrefixes.Si);
        Add("bar", "bar", Prs, 100000, "bars", UnitPrefixes.Si);
        Add("atm", "standard atmosphere", Prs, 101325, "atmosphere|atmospheres");
        Add("at", "technical atmosphere", Prs, gn * 10000, "kgf/cm²");
        Add("Torr", "torr", Prs, Q(101325, 760));
        Add("mmHg", "millimetre of mercury", Prs, mmHg, "mm Hg");
        Add("inHg", "inch of mercury", Prs, mmHg * R("25.4"), "in Hg");
        Add("psi", "pound per square inch", Prs, lbf / inch.Pow(2), "lbf/in²|lb/in²|pounds per square inch");
        Add("ksi", "kilopound per square inch", Prs, lbf * 1000 / inch.Pow(2));
        Add("psf", "pound per square foot", Prs, lbf / foot.Pow(2), "lbf/ft²");
        Add("cmH₂O", "centimetre of water", Prs, gn * 10, "cmH2O|cm H2O");
        Add("inH₂O", "inch of water", Prs, gn * 1000 * inch, "inH2O|in H2O");

        // Energy
        Add("J", "joule", Nrg, 1, prefixes: UnitPrefixes.Si);
        Add("cal", "calorie", Nrg, calorie, "thermochemical calorie", UnitPrefixes.SiThousands);
        Add("Wh", "watt hour", Nrg, 3600, "W·h|watt-hour|watt-hours", UnitPrefixes.SiThousands);
        Add("eV", "electronvolt", Nrg, electronvolt, "electron volt|electron volts", UnitPrefixes.Si);
        Add("BTU", "British thermal unit", Nrg, btu, "Btu|btu|BTUs");
        Add("thm", "therm (US)", Nrg, 105480400, "therm|therms|US therm");
        Add("erg", "erg", Nrg, R("1e-7"));
        Add("ft·lbf", "foot-pound", Nrg, foot * lbf, "ft-lbf|ft lbf|ftlbf|ft·lb|ft-lb|foot pound|foot pounds|foot-pound force");
        Add("tTNT", "tonne of TNT", Nrg, R("4.184e9"), "t TNT|ton of TNT|tons of TNT");

        // Power
        Add("W", "watt", Pwr, 1, prefixes: UnitPrefixes.Si);
        Add("hp", "horsepower", Pwr, foot * lbf * 550, "bhp|mechanical horsepower|imperial horsepower");
        Add("PS", "metric horsepower", Pwr, gn * 75, "cv|CV|metric hp");
        Add("BTU/h", "BTU per hour", Pwr, btu / 3600, "Btu/h|BTU/hr|Btu/hr");
        Add("TR", "ton of refrigeration", Pwr, btu * 12000 / 3600, "RT|refrigeration ton");

        // Temperature
        Add("K", "kelvin", Tmp, 1, "degK|°K", UnitPrefixes.Si);
        Add("°C", "degree Celsius", Tmp, 1, "degC|deg C|celsius|centigrade|℃|degrees C", kind: UnitKind.Affine, offset: R("273.15"), preferred: "C");
        Add("°F", "degree Fahrenheit", Tmp, Q(5, 9), "degF|deg F|fahrenheit|℉|degrees F", kind: UnitKind.Affine, offset: R("459.67") * Q(5, 9), preferred: "F");
        Add("°R", "degree Rankine", Tmp, Q(5, 9), "degR|deg R|rankine|R|°Ra");

        // Angle (π stays symbolic, so degree, gradian, turn and arcminute convert exactly)
        Add("°", "degree", Ang, new UnitScale(Q(1, 180), 1), "deg|degs", space: false);
        Add("rad", "radian", Ang, 1, "rads", UnitPrefixes.SiSmall);
        Add("grad", "gradian", Ang, new UnitScale(Q(1, 200), 1), "gon|grade|grads");
        Add("turn", "turn", Ang, new UnitScale(2, 1), "rev|revolution|revolutions|tr|cycle|cycles");
        Add("′", "arcminute", Ang, new UnitScale(Q(1, 10800), 1), "arcmin|amin|'|minute of arc", space: false);
        Add("″", "arcsecond", Ang, new UnitScale(Q(1, 648000), 1), "arcsec|asec|\"|second of arc", space: false);
        Add("mil", "mil (NATO)", Ang, new UnitScale(Q(1, 3200), 1), "NATO mil");

        // Frequency
        Add("Hz", "hertz", Frq, 1, prefixes: UnitPrefixes.Si);
        Add("rpm", "revolution per minute", Frq, Q(1, 60), "RPM|r/min|rev/min");

        // Data
        Add("bit", "bit", Inf, Q(1, 8), prefixes: UnitPrefixes.SiThousands | UnitPrefixes.Binary, prefixable: "b");
        Add("B", "byte", Inf, 1, "octet|octets", UnitPrefixes.SiThousands | UnitPrefixes.Binary);

        // Fuel economy (distance per volume; litres per 100 km is reciprocal)
        Add("km/L", "kilometre per litre", Fuel, R("1e6"), "km/l|kmpl");
        Add("L/100km", "litre per 100 kilometres", Fuel, R("1e8"), "l/100km|L/100 km|l/100 km|litres per 100 km|liters per 100 km",
            kind: UnitKind.Reciprocal, plural: "litres per 100 kilometres");
        Add("mpg", "mile per gallon (US)", Fuel, mile / usGallon, "mpg US|MPG|mi/gal|miles per gallon");
        Add("mpg UK", "mile per gallon (UK)", Fuel, mile / ukGallon, "mpg imp|imperial mpg|UK mpg");
        Add("gal/100mi", "gallon per 100 miles", Fuel, mile * 100 / usGallon, "gal/100 mi|gallons per 100 miles",
            kind: UnitKind.Reciprocal, plural: "gallons per 100 miles");

        // Electric
        Add("C", "coulomb", Chg, 1, prefixes: UnitPrefixes.Si);
        Add("Ah", "ampere hour", Chg, 3600, "A·h|amp hour|amp hours|amp-hour", UnitPrefixes.Si);
        Add("e", "elementary charge", Chg, electronvolt);
        Add("A", "ampere", Cur, 1, "amp|amps", UnitPrefixes.Si);
        Add("V", "volt", Vlt, 1, prefixes: UnitPrefixes.Si);
        Add("Ω", "ohm", Res, 1, "Ohm|ohms", UnitPrefixes.Si, prefixable: "ohm|Ohm");
    }

    private void DefineCategories()
    {
        Category("length", "Length", "km", "mi", "mm|cm|m|km|in|ft|yd|mi|nmi|µm|nm|thou|au|ly|pc");
        Category("area", "Area", "m²", "ft²", "mm²|cm²|m²|ha|km²|in²|ft²|yd²|acre|mi²");
        Category("volume", "Volume", "L", "gal", "mL|L|m³|cm³|in³|ft³|tsp|tbsp|fl oz|cup|pt|qt|gal|gal UK|pt UK|fl oz UK|bbl");
        Category("mass", "Mass", "kg", "lb", "µg|mg|g|kg|t|oz|lb|st|gr|ct|oz t|ton|ton UK|slug");
        Category("time", "Time", "h", "min", "ns|µs|ms|s|min|h|d|wk|mo|yr|decade|century");
        Category("speed", "Speed", "km/h", "mph", "m/s|km/h|mph|kn|ft/s|km/s|c");
        Category("acceleration", "Acceleration", "g₀", "m/s²", "m/s²|ft/s²|in/s²|Gal|mGal|g₀");
        Category("force", "Force", "N", "lbf", "mN|N|kN|MN|dyn|kgf|tf|lbf|ozf|kip|pdl");
        Category("pressure", "Pressure", "bar", "psi", "Pa|hPa|kPa|MPa|GPa|bar|mbar|atm|at|Torr|mmHg|inHg|psi|ksi|psf|cmH₂O|inH₂O");
        Category("energy", "Energy", "kcal", "kJ", "J|kJ|MJ|GJ|Wh|kWh|MWh|cal|kcal|eV|keV|MeV|BTU|thm|erg|ft·lbf|tTNT");
        Category("power", "Power", "hp", "kW", "mW|W|kW|MW|GW|hp|PS|BTU/h|kcal/h|ft·lbf/s|TR");
        Category("temperature", "Temperature", "°C", "°F", "°C|°F|K|°R");
        Category("angle", "Angle", "°", "rad", "°|rad|grad|turn|′|″|mrad|mil");
        Category("frequency", "Frequency", "rpm", "Hz", "mHz|Hz|kHz|MHz|GHz|THz|rpm");
        Category("data", "Data", "GiB", "GB", "bit|B|kB|KiB|MB|MiB|GB|GiB|TB|TiB|PB|PiB|kbit|Mbit|Gbit|Tbit");
        Category("density", "Density", "g/cm³", "kg/m³", "kg/m³|g/cm³|g/mL|g/L|mg/L|kg/L|t/m³|lb/ft³|lb/in³|lb/gal|oz/in³");
        Category("fuel-economy", "Fuel economy", "L/100km", "mpg", "L/100km|km/L|mpg|mpg UK|gal/100mi");
        Category("electric", "Electric", "mAh", "C", "C|mC|µC|nC|Ah|mAh|e|A|mA|µA|kA|V|mV|µV|kV|MV|Ω|mΩ|kΩ|MΩ");
        Category("cooking", "Cooking", "cup", "mL", "tsp|tbsp|fl oz|cup|pt|qt|metric tsp|metric tbsp|metric cup|mL|dL|L");

        Alias("cm³", "cc", "ccm");
        Alias("kcal", "Cal", "kilocalorie", "kilocalories", "food calorie");
        Alias("kB", "KB", "kilobyte", "kilobytes");
        Alias("µg", "mcg");
        Alias("mbar", "mb", "millibars");
        Alias("kbit", "Kbit", "kb");
        Alias("kWh", "kW·h", "kw h", "kilowatt-hour", "kilowatt-hours");
        Alias("ft²", "sqft", "sq ft");
        Alias("m²", "sqm", "sq m");
        Alias("m³", "cbm");
    }

    private void Category(string id, string name, string from, string to, string units)
    {
        var list = new List<Unit>();
        foreach (var symbol in units.Split('|'))
        {
            var u = FindCandidates(symbol).FirstOrDefault(x => x.Symbol == symbol)
                    ?? throw new InvalidOperationException($"Category {id}: unknown unit {symbol}");
            if (u.IsDerived && !_byId.ContainsKey(u.Id)) Register(u, [], []);
            list.Add(u);
        }
        var category = new UnitCategory(id, name, list, list.First(u => u.Symbol == from), list.First(u => u.Symbol == to));
        _categories.Add(category);
        foreach (var d in category.Dimensions) _categoryByDimension.TryAdd(d, category);
    }
}

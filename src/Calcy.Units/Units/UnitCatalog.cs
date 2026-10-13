using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Calcy.Core.Numbers;
using Calcy.Core.Printing;

namespace Calcy.Units;

/// <summary>
/// The unit library: categories, units with symbols and aliases, SI and binary prefixes, unit expressions (m/s², kg/m³),
/// exact conversions (rational arithmetic, π kept symbolic) and a parser for requests such as "5 km to mi",
/// "100 °C in °F", "3 ft 4 in to cm" or "60 mph to m/s". Thread-safe; use <see cref="Default"/>.
/// </summary>
public sealed partial class UnitCatalog
{
    private static readonly Lazy<UnitCatalog> Shared = new(() => new UnitCatalog());

    private readonly List<Unit> _units = [];
    private readonly List<UnitCategory> _categories = [];
    private readonly Dictionary<string, Unit> _byId = new(StringComparer.Ordinal);

    // Lookup tables. Lists keep the order of preference (preferred aliases, then symbols, then other aliases).
    private readonly Dictionary<string, List<(Unit Unit, int Rank)>> _exact = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(Unit Unit, int Rank)>> _folded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Unit> _prefixableSymbols = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Unit> _prefixableNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Unit> _derived = new(StringComparer.Ordinal);
    private readonly Dictionary<Dimension, UnitCategory> _categoryByDimension = [];
    private readonly QueryParser _parser;

    private const int RankPreferred = 0;
    private const int RankSymbol = 1;
    private const int RankAlias = 2;

    private UnitCatalog()
    {
        _parser = new QueryParser(this);
        DefineUnits();
        DefineCategories();
    }

    /// <summary>The shared catalog with every built-in unit and category.</summary>
    public static UnitCatalog Default => Shared.Value;

    /// <summary>The categories in display order.</summary>
    public IReadOnlyList<UnitCategory> Categories => _categories;

    /// <summary>Every named unit (defined units plus the prefixed and compound units listed in categories).</summary>
    public IReadOnlyList<Unit> Units => _units;

    /// <summary>Finds a unit by symbol, alias or name (throws <see cref="KeyNotFoundException"/> when unknown).</summary>
    public Unit this[string text] => Find(text) ?? throw new KeyNotFoundException($"Unknown unit: {text}");

    /// <summary>The category with this id or name (case-insensitive), or null.</summary>
    public UnitCategory? GetCategory(string idOrName) =>
        _categories.FirstOrDefault(c => string.Equals(c.Id, idOrName, StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(c.Name, idOrName, StringComparison.OrdinalIgnoreCase));

    /// <summary>The category for a unit: the first category listing it, otherwise the first one with its dimension.</summary>
    public UnitCategory? CategoryOf(Unit unit) =>
        _categories.FirstOrDefault(c => c.Contains(unit)) ?? CategoryOf(unit.Dimension);

    /// <summary>The first category that has units of this dimension, or null.</summary>
    public UnitCategory? CategoryOf(Dimension dimension) => _categoryByDimension.GetValueOrDefault(dimension);

    /// <summary>A unit by its <see cref="Unit.Id"/> (falls back to <see cref="Find"/>).</summary>
    public Unit? GetById(string id) => _byId.TryGetValue(id, out var u) ? u : Find(id);

    /// <summary>The best match for a unit text, or null. See <see cref="FindCandidates"/>.</summary>
    public Unit? Find(string text)
    {
        var c = FindCandidates(text);
        return c.Count > 0 ? c[0] : null;
    }

    /// <summary>Finds a unit by symbol, alias or name; returns false when unknown.</summary>
    public bool TryFind(string text, [NotNullWhen(true)] out Unit? unit)
    {
        unit = Find(text);
        return unit is not null;
    }

    /// <summary>
    /// Every unit a text can mean, best first. Accepts symbols (km), names and plurals (kilometres, feet), American spellings,
    /// SI and binary prefixes (µm, MW, KiB), and unit expressions (m/s², kg/m^3, km per h, sq ft, cubic inches).
    /// Ambiguous texts give several units (C is a coulomb or a degree Celsius), which the parser resolves by dimension.
    /// </summary>
    public IReadOnlyList<Unit> FindCandidates(string text)
    {
        var s = UnitText.Normalize(text);
        if (s.Length == 0) return [];
        var simple = FindSimple(s);
        if (simple.Count > 0) return simple;
        if (s.Length > 1 && s[^1] == '.')
        {
            simple = FindSimple(s[..^1]);
            if (simple.Count > 0) return simple;
        }
        var compound = ParseCompound(s);
        return compound is null ? [] : [compound];
    }

    /// <summary>Units whose symbol, name or alias matches the query, best matches first (for a search box).</summary>
    public IReadOnlyList<Unit> Search(string query, int max = 30)
    {
        var q = UnitText.Normalize(query);
        if (q.Length == 0) return [];
        var scored = new List<(Unit Unit, int Score, int Order)>();
        for (int i = 0; i < _units.Count; i++)
        {
            int score = Score(_units[i], q);
            if (score < int.MaxValue) scored.Add((_units[i], score, i));
        }
        var result = scored.OrderBy(x => x.Score).ThenBy(x => x.Order).Select(x => x.Unit).ToList();
        foreach (var c in FindCandidates(q).Reverse())
        {
            result.Remove(c);
            result.Insert(0, c);
        }
        return [.. result.Take(max)];
    }

    private static int Score(Unit u, string q)
    {
        if (u.Symbol == q) return 0;
        if (string.Equals(u.Symbol, q, StringComparison.OrdinalIgnoreCase)) return 1;
        if (string.Equals(u.Name, q, StringComparison.OrdinalIgnoreCase) || string.Equals(u.PluralName, q, StringComparison.OrdinalIgnoreCase) ||
            u.Aliases.Any(a => string.Equals(a, q, StringComparison.OrdinalIgnoreCase))) return 2;
        if (u.Symbol.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 3;
        if (u.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ||
            u.Name.Split(' ', '-').Any(w => w.StartsWith(q, StringComparison.OrdinalIgnoreCase))) return 4;
        if (u.Aliases.Any(a => a.StartsWith(q, StringComparison.OrdinalIgnoreCase))) return 5;
        if (q.Length >= 3 && u.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) return 6;
        if (q.Length >= 3 && u.Aliases.Any(a => a.Contains(q, StringComparison.OrdinalIgnoreCase))) return 7;
        return int.MaxValue;
    }

    // ---- Conversion ----

    /// <summary>Converts a value between two units of the same dimension.</summary>
    /// <exception cref="ConversionException">The units have different dimensions, or the value has no equivalent.</exception>
    public ConversionResult Convert(BigRational value, Unit from, Unit to)
    {
        bool exact = true;
        var r = ConvertValue(value, from, to, ref exact);
        return new ConversionResult(null, new Quantity(value, from), [new Quantity(r, to)], r, exact, CategoryOf(to));
    }

    /// <summary>Converts a value; returns false (with a message for the user) instead of throwing.</summary>
    public bool TryConvert(BigRational value, Unit from, Unit to, [NotNullWhen(true)] out ConversionResult? result, [NotNullWhen(false)] out string? error)
    {
        try
        {
            result = Convert(value, from, to);
            error = null;
            return true;
        }
        catch (ConversionException ex)
        {
            result = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Converts a value between compatible units. Sets <paramref name="exact"/> to false when π had to be approximated.</summary>
    public BigRational ConvertValue(BigRational value, Unit from, Unit to, ref bool exact)
    {
        if (from.Dimension != to.Dimension) throw Incompatible(from, to);
        if (from.Equals(to)) return value;
        if (from.Kind == UnitKind.Linear && to.Kind == UnitKind.Linear)
        {
            var ratio = from.Scale / to.Scale;
            if (!ratio.IsRational) exact = false;
            return value * ratio.ToRational();
        }
        var b = from.ToBase(value, ref exact);
        return to.FromBase(b, ref exact);
    }

    internal ConversionException Incompatible(Unit from, Unit to)
    {
        string What(Unit u) => CategoryOf(u)?.Name.ToLowerInvariant() ?? u.Dimension.ToString();
        return new ConversionException($"{from.Symbol} ({What(from)}) cannot be converted to {to.Symbol} ({What(to)})");
    }

    /// <summary>Parses a request such as "5 km to mi", "3 ft 4 in to cm", "170 cm to ft in" or just "5 km".</summary>
    /// <exception cref="ConversionException">The text cannot be read; the message says why.</exception>
    public ConversionQuery Parse(string text) => _parser.Parse(text);

    /// <summary>Parses a request; returns false with a message for the user instead of throwing.</summary>
    public bool TryParse(string text, [NotNullWhen(true)] out ConversionQuery? query, [NotNullWhen(false)] out string? error)
    {
        try
        {
            query = Parse(text);
            error = null;
            return true;
        }
        catch (ConversionException ex)
        {
            query = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Parses and converts, for example Evaluate("60 mph to m/s").Value is 26.8224.</summary>
    /// <exception cref="ConversionException">The text cannot be read or converted.</exception>
    public ConversionResult Evaluate(string text) => Evaluate(Parse(text));

    /// <summary>Parses and converts; returns false with a message for the user instead of throwing.</summary>
    public bool TryEvaluate(string text, [NotNullWhen(true)] out ConversionResult? result, [NotNullWhen(false)] out string? error)
    {
        try
        {
            result = Evaluate(text);
            error = null;
            return true;
        }
        catch (ConversionException ex)
        {
            result = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Converts a parsed request. The terms are added in the unit of the first term; the result goes to
    /// <paramref name="targets"/> (or the request's own targets, or the first term's unit when there are none).
    /// </summary>
    public ConversionResult Evaluate(ConversionQuery query, IReadOnlyList<Unit>? targets = null)
    {
        targets ??= query.Targets;
        var first = query.Terms[0].Unit;
        bool exact = true;
        BigRational total = BigRational.Zero;
        if (query.Terms.Count > 1 && query.Terms.Any(t => t.Unit.Kind != UnitKind.Linear))
            throw new ConversionException($"{query.Terms.First(t => t.Unit.Kind != UnitKind.Linear).Unit.Symbol} values cannot be added together");
        foreach (var t in query.Terms) total += ConvertValue(t.Value, t.Unit, first, ref exact);
        var input = new Quantity(total, first);
        if (targets.Count == 0) targets = [first];

        if (targets.Count == 1)
        {
            var v = ConvertValue(total, first, targets[0], ref exact);
            return new ConversionResult(query, input, [new Quantity(v, targets[0])], v, exact, CategoryOf(targets[0]));
        }

        if (targets.Any(t => t.Kind != UnitKind.Linear || t.Dimension != first.Dimension))
            throw new ConversionException("A mixed result needs units of the same kind, for example ft in or h min s");
        var ordered = targets.OrderByDescending(t => t.Scale.ToRational()).ToList();
        var smallest = ordered[^1];
        var remaining = ConvertValue(total, first, smallest, ref exact);
        bool negative = remaining.IsNegative;
        remaining = remaining.Abs();
        var amounts = new List<BigRational>();
        for (int i = 0; i < ordered.Count - 1; i++)
        {
            var ratio = ordered[i].Scale / smallest.Scale;
            if (!ratio.IsRational) exact = false;
            var step = ratio.ToRational();
            var whole = new BigRational((remaining / step).Floor());
            amounts.Add(whole);
            remaining -= whole * step;
        }
        amounts.Add(remaining);
        if (negative)
        {
            int k = amounts.FindIndex(a => !a.IsZero);
            if (k >= 0) amounts[k] = -amounts[k];
        }
        var parts = ordered.Select((u, i) => new Quantity(amounts[i], u)).ToList();
        var value = ConvertValue(total, first, ordered[0], ref exact);
        return new ConversionResult(query, input, parts, value, exact, CategoryOf(ordered[0]));
    }

    /// <summary>Reads a plain number (5, -2.5, 1e3, 3/4, 3 1/2, ½, 1.5×10³); returns false when the text is anything else.</summary>
    public static bool TryParseNumber(string text, out BigRational value) => NumberReader.TryParse(text, out value);

    // ---- Formulas ----

    /// <summary>
    /// A one-line description of how <paramref name="from"/> relates to <paramref name="to"/>:
    /// "1 km = 0.6213711922 mi", "°F = °C × 9/5 + 32", "mpg = 235.2145833 / (L/100km)". Empty for incompatible units.
    /// </summary>
    public string Formula(Unit from, Unit to, NumberFormatOptions? options = null)
    {
        if (from.Dimension != to.Dimension) return "";
        string F(BigRational v) => NumberFormatter.FormatRational(v, options);
        if (from.Kind == UnitKind.Linear && to.Kind == UnitKind.Linear)
        {
            var ratio = from.Scale / to.Scale;
            return $"{from.WithSymbol("1")} {(ratio.IsRational ? "=" : "≈")} {to.Format(ratio.ToRational(), options)}";
        }
        if (from.Kind == UnitKind.Reciprocal || to.Kind == UnitKind.Reciprocal)
        {
            if (from.Kind == UnitKind.Reciprocal && to.Kind == UnitKind.Reciprocal)
                return $"{from.WithSymbol("1")} = {to.Format(to.Scale.Coefficient / from.Scale.Coefficient, options)}";
            var k = from.Kind == UnitKind.Reciprocal
                ? from.Scale.Coefficient / to.Scale.ToRational()
                : to.Scale.Coefficient / from.Scale.ToRational();
            return $"{to.Symbol} = {F(k)} / {Grouped(from.Symbol)}";
        }

        // Affine: to = from × a + b, or equivalently to = (from + c) × a.
        var a = from.Scale.Coefficient / to.Scale.Coefficient;
        var b = (from.Offset - to.Offset) / to.Scale.Coefficient;
        var c = (from.Offset - to.Offset) / from.Scale.Coefficient;
        string times = a == BigRational.One ? "" : $" × {Factor(a, options)}";
        if (b.IsZero) return $"{to.Symbol} = {from.Symbol}{times}";
        if (!IsShortDecimal(b) && IsShortDecimal(c) && !a.IsOne)
            return $"{to.Symbol} = ({from.Symbol} {Signed(c, options)}){times}";
        return $"{to.Symbol} = {from.Symbol}{times} {Signed(b, options)}";
    }

    private static string Grouped(string symbol) => symbol.Contains('/') || symbol.Contains(' ') ? $"({symbol})" : symbol;

    private static string Signed(BigRational v, NumberFormatOptions? o) =>
        (v.IsNegative ? "- " : "+ ") + NumberFormatter.FormatRational(v.Abs(), o);

    /// <summary>Small fractions read better than decimals in a formula (× 9/5 rather than × 1.8).</summary>
    private static string Factor(BigRational a, NumberFormatOptions? o) =>
        !a.IsInteger && a.Den <= 12 && a.Num.Sign > 0 ? $"{a.Num}/{a.Den}" : NumberFormatter.FormatRational(a, o);

    private static bool IsShortDecimal(BigRational v) => v.ToTerminatingDecimal(4) is not null;

    // ---- Registration ----

    private Unit Register(Unit u, IEnumerable<string> preferred, IEnumerable<string> prefixableAliases)
    {
        _units.Add(u);
        _byId.TryAdd(u.Id, u);
        foreach (var p in preferred) AddKey(p, u, RankPreferred);
        AddKey(u.Symbol, u, RankSymbol);
        foreach (var a in AllNames(u)) AddKey(a, u, RankAlias);
        if (!u.IsDerived && u.Prefixes != UnitPrefixes.None)
        {
            _prefixableSymbols.TryAdd(u.Symbol, u);
            foreach (var a in prefixableAliases) _prefixableSymbols.TryAdd(a, u);
            foreach (var n in new[] { u.Name, u.PluralName, UnitText.American(u.Name), UnitText.American(u.PluralName) })
                if (n is not null) _prefixableNames.TryAdd(n, u);
        }
        return u;
    }

    private static IEnumerable<string> AllNames(Unit u)
    {
        yield return u.Name;
        yield return u.PluralName;
        if (UnitText.American(u.Name) is { } an) yield return an;
        if (UnitText.American(u.PluralName) is { } ap) yield return ap;
        foreach (var a in u.Aliases) yield return a;
    }

    private void AddKey(string key, Unit u, int rank)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        Add(_exact, key, u, rank);
        Add(_folded, key, u, rank);
    }

    private static void Add(Dictionary<string, List<(Unit Unit, int Rank)>> map, string key, Unit u, int rank)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        int existing = list.FindIndex(x => x.Unit.Equals(u));
        if (existing >= 0)
        {
            if (rank < list[existing].Rank) list[existing] = (u, rank);
        }
        else list.Add((u, rank));
        list.Sort((x, y) => x.Rank.CompareTo(y.Rank));
    }

    /// <summary>Adds extra spellings for a unit after it has been registered.</summary>
    private void Alias(string symbol, params string[] aliases)
    {
        var u = this[symbol];
        foreach (var a in aliases) AddKey(a, u, RankAlias);
    }

    // ---- Lookup ----

    private List<Unit> FindSimple(string s)
    {
        if (_exact.TryGetValue(s, out var exact)) return [.. exact.Select(x => x.Unit)];
        if (Prefixed(s) is { } p) return [p];
        if (PrefixedName(s) is { } pn) return [pn];
        if (_folded.TryGetValue(s, out var folded)) return [.. folded.Select(x => x.Unit)];
        // Plurals that are not generated (kms, hrs): strip a final s.
        if (s.Length > 2 && s[^1] == 's' && char.IsLetter(s[^2]))
        {
            var single = s[..^1];
            if (_exact.TryGetValue(single, out exact)) return [.. exact.Select(x => x.Unit)];
            if (PrefixedName(single) is { } pn2) return [pn2];
        }
        return [];
    }

    private Unit? Prefixed(string s)
    {
        foreach (var prefix in UnitPrefix.All)
        {
            foreach (var symbol in prefix.Symbol == "µ" ? new[] { "µ", "u" } : [prefix.Symbol])
            {
                if (s.Length <= symbol.Length || !s.StartsWith(symbol, StringComparison.Ordinal)) continue;
                if (_prefixableSymbols.TryGetValue(s[symbol.Length..], out var baseUnit) && (baseUnit.Prefixes & prefix.Group) != 0)
                    return Derive(prefix, baseUnit);
            }
        }
        return null;
    }

    private Unit? PrefixedName(string s)
    {
        foreach (var prefix in UnitPrefix.All)
        {
            if (s.Length <= prefix.Name.Length || !s.StartsWith(prefix.Name, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = s[prefix.Name.Length..].TrimStart('-');
            if (_prefixableNames.TryGetValue(rest, out var baseUnit) && (baseUnit.Prefixes & prefix.Group) != 0)
                return Derive(prefix, baseUnit);
        }
        return null;
    }

    private Unit Derive(UnitPrefix prefix, Unit baseUnit)
    {
        var symbol = prefix.Symbol + baseUnit.Symbol;
        return _derived.GetOrAdd(symbol, _ =>
        {
            if (_byId.TryGetValue(symbol, out var listed)) return listed;
            var name = prefix.Name + baseUnit.Name;
            var aliases = new List<string>();
            if (UnitText.American(name) is { } american) aliases.Add(american);
            if (prefix.Symbol == "µ") aliases.Add("u" + baseUnit.Symbol);
            return new Unit(symbol, symbol, name, baseUnit.Dimension, new UnitScale(prefix.Factor) * baseUnit.Scale,
                baseUnit.Kind, baseUnit.Offset * prefix.Factor, UnitPrefixes.None, aliases, baseUnit.SpaceBeforeSymbol, isDerived: true,
                pluralName: prefix.Name + baseUnit.PluralName);
        });
    }

    // ---- Unit expressions ----

    private static readonly string[] SquareWords = ["square ", "sq "];
    private static readonly string[] CubicWords = ["cubic ", "cu "];

    /// <summary>Parses m/s², kg/m^3, N·m, km per h, sq ft, cubic inches, m2. Returns null when any part is unknown.</summary>
    private Unit? ParseCompound(string text)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(text, @"\s+per\s+", "/", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = s.Replace("**", "^", StringComparison.Ordinal).Replace("(", "", StringComparison.Ordinal).Replace(")", "", StringComparison.Ordinal);
        var factors = new List<(string Atom, int Sign)>();
        var current = new StringBuilder();
        int sign = 1;
        void Flush()
        {
            var a = current.ToString().Trim();
            if (a.Length > 0) factors.Add((a, sign));
            current.Clear();
        }
        foreach (var ch in s)
        {
            if (ch == '/') { Flush(); sign = -1; }
            else if (ch is '*' or '·' or '×') Flush();
            else current.Append(ch);
        }
        Flush();
        if (factors.Count == 0) return null;

        var parts = new List<(Unit Unit, int Power)>();
        foreach (var (atom, sgn) in factors)
        {
            var (text1, power) = SplitPower(atom);
            if (text1 == "1" && sgn > 0 && power == 1) continue;
            if (power == 0) return null;
            var found = FindSimple(text1);
            var unit = found.FirstOrDefault(u => u.Kind == UnitKind.Linear);
            if (unit is null) return null;
            parts.Add((unit, power * sgn));
        }
        if (parts.Count == 0 || (parts.Count == 1 && parts[0].Power == 1)) return null;

        var dim = Dimension.None;
        var scale = UnitScale.One;
        foreach (var (u, p) in parts)
        {
            dim *= u.Dimension.Pow(p);
            scale *= u.Scale.Pow(p);
        }
        var symbol = CompoundSymbol(parts);
        if (_exact.TryGetValue(symbol, out var known) && known.FirstOrDefault(k => k.Unit.Dimension == dim && k.Unit.Scale == scale).Unit is { } same)
            return same;
        return _derived.GetOrAdd("expr:" + symbol, _ =>
        {
            var name = CompoundName(parts);
            var aliases = new List<string>();
            if (UnitText.American(name) is { } american) aliases.Add(american);
            return new Unit(symbol, symbol, name, dim, scale, isDerived: true, aliases: aliases);
        });
    }

    private static (string Atom, int Power) SplitPower(string atom)
    {
        int power = 1;
        foreach (var w in SquareWords)
            if (atom.StartsWith(w, StringComparison.OrdinalIgnoreCase)) { atom = atom[w.Length..].Trim(); power = 2; }
        foreach (var w in CubicWords)
            if (atom.StartsWith(w, StringComparison.OrdinalIgnoreCase)) { atom = atom[w.Length..].Trim(); power = 3; }
        if (atom.EndsWith(" squared", StringComparison.OrdinalIgnoreCase)) { atom = atom[..^8].Trim(); power *= 2; }
        else if (atom.EndsWith(" cubed", StringComparison.OrdinalIgnoreCase)) { atom = atom[..^6].Trim(); power *= 3; }

        int caret = atom.IndexOf('^');
        if (caret > 0)
        {
            if (int.TryParse(atom[(caret + 1)..], System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var p))
                return (atom[..caret].Trim(), power * p);
            return (atom, 0);
        }
        int sup = atom.Length;
        while (sup > 0 && UnitText.IsSuperscript(atom[sup - 1])) sup--;
        if (sup < atom.Length && sup > 0 && UnitText.TryParseSuperscript(atom[sup..], out var sp)) return (atom[..sup], power * sp);
        int digits = atom.Length;
        while (digits > 0 && char.IsAsciiDigit(atom[digits - 1])) digits--;
        if (digits < atom.Length && digits > 0 && char.IsLetter(atom[digits - 1]) &&
            int.TryParse(atom[digits..], System.Globalization.CultureInfo.InvariantCulture, out var dp) && dp is >= 2 and <= 4)
            return (atom[..digits], power * dp);
        return (atom, power);
    }

    private static string CompoundSymbol(List<(Unit Unit, int Power)> parts)
    {
        static string Part(Unit u, int p) => p == 1 ? u.Symbol : u.Symbol + UnitText.Superscript(p);
        var num = parts.Where(x => x.Power > 0).Select(x => Part(x.Unit, x.Power)).ToList();
        var den = parts.Where(x => x.Power < 0).Select(x => Part(x.Unit, -x.Power)).ToList();
        var top = num.Count == 0 ? "1" : string.Join("·", num);
        if (den.Count == 0) return top;
        return top + "/" + (den.Count == 1 ? den[0] : "(" + string.Join("·", den) + ")");
    }

    private static string CompoundName(List<(Unit Unit, int Power)> parts)
    {
        static string Part(Unit u, int p)
        {
            bool time = u.Dimension == new Dimension(Time: 1);
            return p switch
            {
                1 => u.Name,
                2 => time ? u.Name + " squared" : "square " + u.Name,
                3 => time ? u.Name + " cubed" : "cubic " + u.Name,
                _ => $"{u.Name} to the power {p}",
            };
        }
        var num = parts.Where(x => x.Power > 0).Select(x => Part(x.Unit, x.Power)).ToList();
        var den = parts.Where(x => x.Power < 0).Select(x => Part(x.Unit, -x.Power)).ToList();
        var top = num.Count == 0 ? "one" : string.Join(" ", num);
        return den.Count == 0 ? top : top + " per " + string.Join(" ", den);
    }
}

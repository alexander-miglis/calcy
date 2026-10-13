using System.Globalization;
using System.Text;

namespace Calcy.Units;

/// <summary>Text helpers shared by the unit catalog and parser: superscripts, plurals and input clean-up.</summary>
internal static class UnitText
{
    private const string Sup = "⁰¹²³⁴⁵⁶⁷⁸⁹";

    private static readonly HashSet<string> Uncountable = new(StringComparer.OrdinalIgnoreCase)
    {
        "hertz", "horsepower", "siemens", "lux", "stone", "Celsius", "Fahrenheit",
    };

    /// <summary>Integer exponent as superscript text, for example -2 gives "⁻²".</summary>
    public static string Superscript(int n)
    {
        var sb = new StringBuilder();
        foreach (var c in n.ToString(CultureInfo.InvariantCulture))
            sb.Append(c == '-' ? '⁻' : Sup[c - '0']);
        return sb.ToString();
    }

    /// <summary>True for superscript digits and the superscript minus.</summary>
    public static bool IsSuperscript(char c) => c == '⁻' || Sup.Contains(c);

    /// <summary>Reads a superscript integer such as "⁻¹" (returns false when the text is not one).</summary>
    public static bool TryParseSuperscript(string s, out int value)
    {
        value = 0;
        if (s.Length == 0) return false;
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (c == '⁻') sb.Append('-');
            else if (Sup.IndexOf(c) is var d and >= 0) sb.Append((char)('0' + d));
            else return false;
        }
        return int.TryParse(sb.ToString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>English plural of a unit name: "kilometre per hour" gives "kilometres per hour", "foot" gives "feet".</summary>
    public static string Pluralize(string name)
    {
        foreach (var joiner in new[] { " per ", " of " })
        {
            int at = name.IndexOf(joiner, StringComparison.Ordinal);
            if (at > 0) return Pluralize(name[..at]) + name[at..];
        }
        int paren = name.IndexOf(" (", StringComparison.Ordinal);
        if (paren > 0) return Pluralize(name[..paren]) + name[paren..];
        if (name.StartsWith("degree ", StringComparison.Ordinal)) return "degrees " + name["degree ".Length..];
        int space = name.LastIndexOf(' ');
        string head = space >= 0 ? name[..(space + 1)] : "";
        string word = space >= 0 ? name[(space + 1)..] : name;
        return head + PluralWord(word);
    }

    private static string PluralWord(string w)
    {
        if (w.Length == 0 || Uncountable.Contains(w)) return w;
        int force = w.IndexOf("-force", StringComparison.Ordinal);
        if (force > 0) return PluralWord(w[..force]) + w[force..];
        if (w.EndsWith("foot", StringComparison.Ordinal)) return w[..^4] + "feet";
        if (w.EndsWith("s", StringComparison.Ordinal) || w.EndsWith("x", StringComparison.Ordinal) || w.EndsWith("z", StringComparison.Ordinal) ||
            w.EndsWith("ch", StringComparison.Ordinal) || w.EndsWith("sh", StringComparison.Ordinal))
            return w + "es";
        if (w.Length > 1 && w[^1] == 'y' && !"aeiou".Contains(w[^2])) return w[..^1] + "ies";
        return w + "s";
    }

    /// <summary>American spelling of a British unit name (metre to meter, litre to liter), or null when the same.</summary>
    public static string? American(string name)
    {
        var s = name.Replace("metre", "meter", StringComparison.Ordinal).Replace("litre", "liter", StringComparison.Ordinal);
        return s == name ? null : s;
    }

    /// <summary>
    /// Cleans user input: trims, folds runs of white space (including no-break and thin spaces), turns the minus sign into a hyphen
    /// and the Greek letter mu into the micro sign.
    /// </summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool space = false;
        foreach (var raw in text)
        {
            char c = raw switch
            {
                '−' => '-',
                'μ' => 'µ',
                ' ' or ' ' or ' ' or '\t' or '\r' or '\n' => ' ',
                '⋅' => '·',
                _ => raw,
            };
            if (c == ' ')
            {
                space = true;
                continue;
            }
            if (space && sb.Length > 0) sb.Append(' ');
            space = false;
            sb.Append(c);
        }
        return sb.ToString();
    }
}

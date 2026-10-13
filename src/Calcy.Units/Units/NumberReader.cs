using System.Globalization;
using System.Numerics;
using Calcy.Core.Numbers;

namespace Calcy.Units;

/// <summary>
/// Reads exact numbers from conversion input, culture-invariant: 5, -2.5, .5, 1e-3, 1.5×10^3, 1.5×10³, 1,000,000,
/// fractions 3/4, mixed numbers 3 1/2 and vulgar fractions ½ or 1½.
/// </summary>
internal static class NumberReader
{
    private const int MaxExponent = 1000;

    private static readonly Dictionary<char, BigRational> Vulgar = new()
    {
        ['½'] = new(1, 2), ['⅓'] = new(1, 3), ['⅔'] = new(2, 3), ['¼'] = new(1, 4), ['¾'] = new(3, 4),
        ['⅕'] = new(1, 5), ['⅛'] = new(1, 8), ['⅜'] = new(3, 8), ['⅝'] = new(5, 8), ['⅞'] = new(7, 8),
    };

    /// <summary>True when <paramref name="text"/> is exactly one number (white space around it is allowed).</summary>
    public static bool TryParse(string text, out BigRational value)
    {
        var s = UnitText.Normalize(text);
        int pos = 0;
        if (!TryRead(s, ref pos, out value, out _)) return false;
        return pos == s.Length;
    }

    /// <summary>True when a number starts at <paramref name="i"/> (a digit, a vulgar fraction, or a sign or point before one).</summary>
    public static bool StartsNumber(string s, int i)
    {
        if (i >= s.Length) return false;
        char c = s[i];
        if (char.IsAsciiDigit(c) || Vulgar.ContainsKey(c)) return true;
        if (c is '-' or '+' or '.' && i + 1 < s.Length)
        {
            char n = s[i + 1];
            if (char.IsAsciiDigit(n) || Vulgar.ContainsKey(n)) return true;
            if (c != '.' && n == '.' && i + 2 < s.Length && char.IsAsciiDigit(s[i + 2])) return true;
        }
        return false;
    }

    /// <summary>Reads a number at <paramref name="pos"/> and moves past it. Leaves <paramref name="pos"/> unchanged on failure.</summary>
    public static bool TryRead(string s, ref int pos, out BigRational value, out string text)
    {
        value = BigRational.Zero;
        text = "";
        int i = pos;
        while (i < s.Length && s[i] == ' ') i++;
        int start = i;
        bool negative = false;
        if (i < s.Length && s[i] is '-' or '+')
        {
            negative = s[i] == '-';
            i++;
        }

        // A lone vulgar fraction: ½
        if (i < s.Length && Vulgar.TryGetValue(s[i], out var lone))
        {
            value = negative ? -lone : lone;
            i++;
            text = s[start..i];
            pos = i;
            return true;
        }

        int intStart = i;
        while (i < s.Length && (char.IsAsciiDigit(s[i]) || (s[i] == ',' && IsGroupComma(s, i, intStart)))) i++;
        string intDigits = s[intStart..i].Replace(",", "", StringComparison.Ordinal);
        string fracDigits = "";
        bool hasPoint = false;
        if (i < s.Length && s[i] == '.' && i + 1 < s.Length && char.IsAsciiDigit(s[i + 1]))
        {
            hasPoint = true;
            int f = ++i;
            while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
            fracDigits = s[f..i];
        }
        if (intDigits.Length == 0 && fracDigits.Length == 0) return false;

        var number = BigRational.ParseDecimal((intDigits.Length == 0 ? "0" : intDigits) + (hasPoint ? "." + fracDigits : ""));
        bool plainInteger = !hasPoint;

        // Exponent: e5, E-3, ×10^5, x10^-5, *10^5, ×10⁵
        int exponent = 0;
        bool hasExponent = false;
        if (i < s.Length && s[i] is 'e' or 'E')
        {
            int j = i + 1;
            if (j < s.Length && s[j] is '+' or '-') j++;
            int d = j;
            while (j < s.Length && char.IsAsciiDigit(s[j])) j++;
            if (j > d)
            {
                if (!int.TryParse(s[(i + 1)..j], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent)) return false;
                hasExponent = true;
                i = j;
            }
        }
        else if (TryReadTimesTen(s, i, out int e10, out int after))
        {
            exponent = e10;
            hasExponent = true;
            i = after;
        }
        if (hasExponent)
        {
            if (Math.Abs(exponent) > MaxExponent) return false;
            number *= exponent >= 0 ? new BigRational(BigInteger.Pow(10, exponent)) : new BigRational(BigInteger.One, BigInteger.Pow(10, -exponent));
            plainInteger = false;
        }

        if (plainInteger)
        {
            // Fraction 3/4 (only directly after the digits).
            if (i + 1 < s.Length && s[i] == '/' && char.IsAsciiDigit(s[i + 1]))
            {
                int j = i + 1;
                while (j < s.Length && char.IsAsciiDigit(s[j])) j++;
                var den = BigInteger.Parse(s[(i + 1)..j], CultureInfo.InvariantCulture);
                if (den.IsZero) return false;
                number /= new BigRational(den);
                i = j;
            }
            // Vulgar fraction right after the digits or after one space: 1½, 1 ½
            else if (TryVulgarAfter(s, i, out var vf, out int afterVulgar))
            {
                number += vf;
                i = afterVulgar;
            }
            // Mixed number: 3 1/2
            else if (TryMixedFraction(s, i, out var mf, out int afterMixed))
            {
                number += mf;
                i = afterMixed;
            }
        }

        value = negative ? -number : number;
        text = s[start..i];
        pos = i;
        return true;
    }

    private static bool IsGroupComma(string s, int i, int intStart)
    {
        // Grouping commas need 1 to 3 digits before the first comma and exactly 3 digits after each comma.
        if (i == intStart) return false;
        int after = i + 1;
        int n = 0;
        while (after + n < s.Length && char.IsAsciiDigit(s[after + n])) n++;
        if (n != 3) return false;
        int before = 0;
        int k = i - 1;
        while (k >= intStart && char.IsAsciiDigit(s[k])) { before++; k--; }
        return k >= intStart ? before == 3 : before is >= 1 and <= 3;
    }

    private static bool TryReadTimesTen(string s, int i, out int exponent, out int after)
    {
        exponent = 0;
        after = i;
        int j = i;
        if (j < s.Length && s[j] == ' ') j++;
        if (j >= s.Length || s[j] is not ('×' or 'x' or '*' or '·')) return false;
        j++;
        if (j < s.Length && s[j] == ' ') j++;
        if (j + 1 >= s.Length || s[j] != '1' || s[j + 1] != '0') return false;
        j += 2;
        if (j < s.Length && s[j] == '^')
        {
            int k = j + 1;
            if (k < s.Length && s[k] is '+' or '-') k++;
            int d = k;
            while (k < s.Length && char.IsAsciiDigit(s[k])) k++;
            if (k == d) return false;
            if (!int.TryParse(s[(j + 1)..k], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent)) return false;
            after = k;
            return true;
        }
        int sup = j;
        while (sup < s.Length && UnitText.IsSuperscript(s[sup])) sup++;
        if (sup == j || !UnitText.TryParseSuperscript(s[j..sup], out exponent)) return false;
        after = sup;
        return true;
    }

    private static bool TryVulgarAfter(string s, int i, out BigRational value, out int after)
    {
        value = BigRational.Zero;
        after = i;
        int j = i;
        if (j < s.Length && s[j] == ' ') j++;
        if (j < s.Length && Vulgar.TryGetValue(s[j], out value))
        {
            after = j + 1;
            return true;
        }
        return false;
    }

    private static bool TryMixedFraction(string s, int i, out BigRational value, out int after)
    {
        value = BigRational.Zero;
        after = i;
        if (i >= s.Length || s[i] != ' ') return false;
        int j = i + 1;
        int n0 = j;
        while (j < s.Length && char.IsAsciiDigit(s[j])) j++;
        if (j == n0 || j >= s.Length || s[j] != '/') return false;
        int d0 = ++j;
        while (j < s.Length && char.IsAsciiDigit(s[j])) j++;
        if (j == d0) return false;
        if (j < s.Length && s[j] is not (' ' or '\'' or '"') && !char.IsLetter(s[j])) return false;
        var num = BigInteger.Parse(s[n0..(d0 - 1)], CultureInfo.InvariantCulture);
        var den = BigInteger.Parse(s[d0..j], CultureInfo.InvariantCulture);
        if (den.IsZero || num >= den) return false;
        value = new BigRational(num, den);
        after = j;
        return true;
    }
}

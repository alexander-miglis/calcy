using System.Globalization;
using System.Text;

namespace Calcy.Formulas;

/// <summary>Text helpers for search: normalisation (case, accents, punctuation) and typo tolerant matching.</summary>
public static class SearchText
{
    private static readonly HashSet<string> s_stopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "of", "for", "in", "on", "and", "to", "with", "by", "from",
        "formula", "formulas", "formulae", "equation", "equations", "calculate", "calculator",
        "how", "what", "is", "do", "i", "find",
    };

    /// <summary>
    /// Lower-cases, removes accents (é becomes e), drops apostrophes (so "Ohm's" becomes "ohms"),
    /// turns every other non letter or digit into a space and collapses repeated spaces.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        bool pendingSpace = false;
        foreach (char ch in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark || cat == UnicodeCategory.EnclosingMark) continue;
            if (ch is '\'' or '’' or '‘' or 'ʼ' or '`') continue;
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSpace && sb.Length > 0) sb.Append(' ');
                pendingSpace = false;
                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                pendingSpace = true;
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Splits normalised text into words.</summary>
    public static string[] Words(string? text) =>
        Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Query words with filler words ("the", "formula", ...) removed, unless nothing else is left.</summary>
    public static string[] QueryWords(string? query)
    {
        var words = Words(query);
        var kept = words.Where(w => !s_stopWords.Contains(w)).ToArray();
        return kept.Length > 0 ? kept : words;
    }

    /// <summary>Lower-cased text without whitespace or multiplication signs, used to match typed equations such as "E=mc^2".</summary>
    public static string CompactEquation(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        foreach (char ch in text)
        {
            if (char.IsWhiteSpace(ch) || ch is '*' or '·' or '×') continue;
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    /// <summary>
    /// True when <paramref name="a"/> and <paramref name="b"/> differ by at most one edit:
    /// one insertion, deletion, substitution, or swap of two neighbouring letters.
    /// </summary>
    public static bool WithinOneEdit(string a, string b)
    {
        int la = a.Length, lb = b.Length;
        if (Math.Abs(la - lb) > 1) return false;
        if (la == lb)
        {
            int first = -1, mismatches = 0;
            for (int i = 0; i < la; i++)
            {
                if (a[i] == b[i]) continue;
                mismatches++;
                if (mismatches == 1) first = i;
                else if (mismatches == 2)
                {
                    // Allow one transposition of neighbours: "ab" vs "ba".
                    if (i != first + 1 || a[first] != b[i] || a[i] != b[first]) return false;
                }
                else return false;
            }
            return true;
        }

        // Lengths differ by one: the longer must equal the shorter with one character inserted.
        string s = la < lb ? a : b, l = la < lb ? b : a;
        int si = 0, li = 0;
        bool skipped = false;
        while (si < s.Length && li < l.Length)
        {
            if (s[si] == l[li]) { si++; li++; continue; }
            if (skipped) return false;
            skipped = true;
            li++;
        }
        return true;
    }

    /// <summary>
    /// How well a query word matches an indexed word, from 0 (no match) to 1 (exact).
    /// Prefixes and simple plurals score high, substrings and one-edit typos (words of 4 letters or more) lower.
    /// </summary>
    public static double MatchQuality(string queryWord, string word)
    {
        if (queryWord.Length == 0 || word.Length == 0) return 0;
        if (string.Equals(queryWord, word, StringComparison.Ordinal)) return 1.0;
        if (IsPlural(queryWord, word) || IsPlural(word, queryWord)) return 0.92;
        if (queryWord.Length >= 2 && word.StartsWith(queryWord, StringComparison.Ordinal))
            return 0.65 + 0.2 * queryWord.Length / word.Length;
        if (queryWord.Length >= 4 && WithinOneEdit(queryWord, word)) return 0.6;
        if (queryWord.Length >= 3 && word.Contains(queryWord, StringComparison.Ordinal)) return 0.45;
        if (queryWord.Length >= 5 && word.Length > queryWord.Length + 1
            && WithinOneEdit(queryWord, word[..queryWord.Length])) return 0.4;
        return 0;
    }

    private static bool IsPlural(string longer, string shorter) =>
        longer.Length == shorter.Length + 1 && longer[^1] == 's' && longer.StartsWith(shorter, StringComparison.Ordinal)
        || longer.Length == shorter.Length + 2 && longer.EndsWith("es", StringComparison.Ordinal) && longer.StartsWith(shorter, StringComparison.Ordinal);
}

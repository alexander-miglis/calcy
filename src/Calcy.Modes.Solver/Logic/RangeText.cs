namespace Calcy.Modes.Solver.Logic;

/// <summary>
/// Splits a search range typed by the user into its two ends: "[-10, 10]", "-10, 10", "0 to 2pi", "0..5" and
/// "(-1, 1)" all work. Commas inside brackets of an end (log(8, 2)) are kept.
/// </summary>
public static class RangeText
{
    /// <summary>The two end texts, or null when the text is not a range. Empty text gives null too.</summary>
    public static (string Low, string High)? Split(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        if (t.Length >= 2 && (t[0] is '[' or '(') && (t[^1] is ']' or ')') && Balanced(t[1..^1])) t = t[1..^1].Trim();
        int split = TopLevelIndex(t, ",");
        int width = 1;
        if (split < 0)
        {
            split = TopLevelIndex(t, "..");
            width = 2;
        }
        if (split < 0)
        {
            split = TopLevelIndex(t, " to ");
            width = 4;
        }
        if (split < 0) return null;
        var low = t[..split].Trim();
        var high = t[(split + width)..].Trim();
        if (low.Length == 0 || high.Length == 0) return null;
        return (MathText.Ascii(low), MathText.Ascii(high));
    }

    /// <summary>The range as the user would type it: [a, b].</summary>
    public static string Format(string low, string high) => $"[{low}, {high}]";

    private static int TopLevelIndex(string t, string token)
    {
        int depth = 0;
        for (int i = 0; i <= t.Length - token.Length; i++)
        {
            char c = t[i];
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            else if (depth == 0 && string.CompareOrdinal(t, i, token, 0, token.Length) == 0) return i;
        }
        return -1;
    }

    private static bool Balanced(string t)
    {
        int depth = 0;
        foreach (var c in t)
        {
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}')
            {
                depth--;
                if (depth < 0) return false;
            }
        }
        return depth == 0;
    }
}

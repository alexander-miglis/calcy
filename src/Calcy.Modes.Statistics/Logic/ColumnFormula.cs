namespace Calcy.Modes.Statistics.Logic;

/// <summary>Splits a column formula such as "L3 = L2 / L1" into the target list and the expression.</summary>
public static class ColumnFormula
{
    /// <summary>
    /// True when <paramref name="text"/> has the form "name = expression" (a single =, not ≤, ≥, ≠ or ==).
    /// The quotes of the TI live-formula spelling L3 = "L1^2" are accepted and dropped.
    /// </summary>
    public static bool TryParse(string? text, out string target, out string expression)
    {
        target = "";
        expression = "";
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        int eq = -1;
        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] != '=') continue;
            char before = i > 0 ? t[i - 1] : ' ';
            char after = i + 1 < t.Length ? t[i + 1] : ' ';
            if (before is '<' or '>' or '!' or '=' || after == '=') return false;
            eq = i;
            break;
        }
        if (eq <= 0) return false;
        var name = t[..eq].Trim();
        var rhs = t[(eq + 1)..].Trim();
        if (rhs.Length >= 2 && rhs[0] == '"' && rhs[^1] == '"') rhs = rhs[1..^1].Trim();
        if (name.Length == 0 || rhs.Length == 0) return false;
        if (!char.IsLetter(name[0]) || !name.All(ch => char.IsLetterOrDigit(ch) || ch == '_')) return false;
        target = name;
        expression = rhs;
        return true;
    }
}

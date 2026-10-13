using System.Text;

namespace Calcy.Modes.Statistics.Logic;

/// <summary>
/// Turns formula text such as "4.54262x + 47.9667" or "3.2e^(0.5x)" into LaTeX. Decimals stay decimals (a fitted
/// coefficient should not become a fraction); powers get braces, function names get backslashes and * becomes \cdot.
/// </summary>
public static class FormulaText
{
    private static readonly string[] Functions = ["asin", "acos", "atan", "sinh", "cosh", "tanh", "sin", "cos", "tan", "ln", "log", "exp"];

    /// <summary>LaTeX for a formula.</summary>
    public static string Latex(string formula)
    {
        var sb = new StringBuilder();
        Convert(formula.Trim(), sb);
        return sb.ToString();
    }

    private static void Convert(string s, StringBuilder sb)
    {
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '^')
            {
                i++;
                sb.Append("^{");
                if (i < s.Length && s[i] == '(')
                {
                    int close = Matching(s, i);
                    Convert(s[(i + 1)..close], sb);
                    i = close + 1;
                }
                else
                {
                    int start = i;
                    if (i < s.Length && s[i] is '-' or '+') i++;
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '.')) i++;
                    sb.Append(s[start..i]);
                }
                sb.Append('}');
                continue;
            }
            if (c == '*')
            {
                sb.Append(" \\cdot ");
                i++;
                continue;
            }
            if (s.AsSpan(i).StartsWith("sqrt(", StringComparison.Ordinal))
            {
                int close = Matching(s, i + 4);
                sb.Append("\\sqrt{");
                Convert(s[(i + 5)..close], sb);
                sb.Append('}');
                i = close + 1;
                continue;
            }
            if (char.IsLetter(c) && (i == 0 || !char.IsLetter(s[i - 1])))
            {
                var fn = Functions.FirstOrDefault(f => s.AsSpan(i).StartsWith(f + "(", StringComparison.Ordinal));
                if (fn is not null)
                {
                    sb.Append('\\').Append(fn);
                    i += fn.Length;
                    continue;
                }
            }
            if (c == 'π')
            {
                sb.Append("\\pi ");
                i++;
                continue;
            }
            sb.Append(c);
            i++;
        }
    }

    private static int Matching(string s, int open)
    {
        int depth = 0;
        for (int k = open; k < s.Length; k++)
        {
            if (s[k] == '(') depth++;
            else if (s[k] == ')' && --depth == 0) return k;
        }
        return s.Length - 1;
    }
}

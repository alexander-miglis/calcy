namespace Calcy.Modes.Programmer;

/// <summary>Text edits on an expression that need to understand its tokens (base switching, the ± key).</summary>
public static class ExpressionText
{
    /// <summary>
    /// Rewrites literals that have no prefix from one input base to another so the expression keeps its meaning
    /// ("FF AND 0F" in HEX becomes "255 AND 15" in DEC). Prefixed literals, names and operators are kept as typed.
    /// Text that cannot be read in the old base is returned unchanged.
    /// </summary>
    public static string ChangeBase(string text, NumberBase from, NumberBase to)
    {
        if (from == to || string.IsNullOrWhiteSpace(text)) return text;
        List<Token> tokens;
        try
        {
            tokens = ExpressionLexer.Tokenize(text, from);
        }
        catch (ProgrammerException)
        {
            return text;
        }
        var sb = new System.Text.StringBuilder(text.Length + 8);
        int last = 0;
        foreach (var t in tokens)
        {
            if (t.Kind != TokenKind.Number || t.HasPrefix) continue;
            sb.Append(text, last, t.Start - last);
            sb.Append(BaseFormatter.ToDigits(t.Value, to));
            last = t.End;
        }
        sb.Append(text, last, text.Length - last);
        return sb.ToString();
    }

    /// <summary>
    /// The ± key: removes or adds a unary minus in front of the number at (or just before) the caret.
    /// When there is no number there, a minus sign is inserted at the caret.
    /// </summary>
    public static (string Text, int Caret) ToggleSign(string text, int caret, NumberBase inputBase)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        List<Token> tokens;
        try
        {
            tokens = ExpressionLexer.Tokenize(text, inputBase);
        }
        catch (ProgrammerException)
        {
            return (text.Insert(caret, "-"), caret + 1);
        }
        int index = tokens.FindLastIndex(t => t.Kind is TokenKind.Number or TokenKind.Name or TokenKind.CardRef && t.Start <= caret && caret <= t.End);
        if (index < 0) return (text.Insert(caret, "-"), caret + 1);
        var operand = tokens[index];
        if (index > 0 && tokens[index - 1] is { Kind: TokenKind.Operator, Op: "-" } minus &&
            (index == 1 || tokens[index - 2].Kind is TokenKind.Operator or TokenKind.LeftParen or TokenKind.Comma or TokenKind.Question or TokenKind.Colon or TokenKind.Assign))
        {
            var removed = text.Remove(minus.Start, operand.Start - minus.Start);
            return (removed, Math.Max(minus.Start, caret - (operand.Start - minus.Start)));
        }
        return (text.Insert(operand.Start, "-"), caret + 1);
    }

    /// <summary>Inserts text at the caret, adding spaces around word operators so the result stays readable.</summary>
    public static (string Text, int Caret) Insert(string text, int caret, string insert)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        if (insert.StartsWith(' ') && caret > 0 && text[caret - 1] == ' ') insert = insert.TrimStart();
        if (insert.EndsWith(' ') && caret < text.Length && text[caret] == ' ') insert = insert.TrimEnd();
        return (text.Insert(caret, insert), caret + insert.Length);
    }
}

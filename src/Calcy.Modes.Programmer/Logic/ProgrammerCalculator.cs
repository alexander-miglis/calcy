using System.Numerics;

namespace Calcy.Modes.Programmer;

/// <summary>One evaluated operation in the cards list.</summary>
/// <param name="Number">Card number (1 for the first card); usable in expressions as #1.</param>
/// <param name="Expression">The expression as typed.</param>
/// <param name="Format">The word it was evaluated in.</param>
/// <param name="Base">The input base at the time; the result is shown in it.</param>
/// <param name="Value">The result (canonical value in <paramref name="Format"/>).</param>
/// <param name="AssignedName">Variable defined by the expression, if any.</param>
/// <param name="Notes">Notes such as "overflow wrapped".</param>
/// <param name="Error">The error message when the expression failed.</param>
public sealed record ProgrammerCard(int Number, string Expression, IntegerFormat Format, NumberBase Base, BigInteger Value,
    string? AssignedName, IReadOnlyList<string> Notes, string? Error)
{
    /// <summary>True when the expression failed.</summary>
    public bool IsError => Error is not null;

    /// <summary>Context line such as "16-bit signed".</summary>
    public string Context => Format.Describe();

    /// <summary>The result in the card's base, written so it can be typed back in (0xFF, 0b1010, -1).</summary>
    public string Result => Error ?? BaseFormatter.FormatLiteral(Format, Value, Base);

    /// <summary>The result in a second base (decimal for hex, octal and binary results; hex for decimal results).</summary>
    public string Secondary => IsError ? "" : Base == NumberBase.Dec
        ? BaseFormatter.FormatLiteral(Format, Value, NumberBase.Hex)
        : BaseFormatter.Format(Format, Value, NumberBase.Dec);
}

/// <summary>What the value panel shows while an expression is being typed.</summary>
/// <param name="Value">The live result, or the current value when the expression is empty or has an error.</param>
/// <param name="IsLive">True when <paramref name="Value"/> comes from the expression being typed.</param>
/// <param name="Error">Why the expression cannot be evaluated yet.</param>
/// <param name="ErrorPosition">Where the problem is in the expression, or -1.</param>
/// <param name="Notes">Notes such as "overflow wrapped".</param>
public sealed record ProgrammerPreview(BigInteger Value, bool IsLive, string? Error, int ErrorPosition, IReadOnlyList<string> Notes);

/// <summary>
/// The integer calculator behind the Programmer screen, without any UI: the word format, input base, current value,
/// the expression being typed (with its caret), keypad behaviour, variables and the cards list.
/// </summary>
public sealed class ProgrammerCalculator : IVariableScope
{
    /// <summary>Most cards kept; older ones are dropped.</summary>
    public const int MaxCards = 500;

    private readonly Dictionary<string, BigInteger> _variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ProgrammerCard> _cards = [];
    private int _nextCard = 1;

    /// <summary>Raised after any change of state.</summary>
    public event EventHandler? Changed;

    /// <summary>The word: width, signedness and overflow mode. Default 16-bit signed, wrap.</summary>
    public IntegerFormat Format { get; private set; } = new(16, true);

    /// <summary>The base unprefixed literals are typed in. Default HEX.</summary>
    public NumberBase InputBase { get; private set; } = NumberBase.Hex;

    /// <summary>The current value (also available as ans).</summary>
    public BigInteger Value { get; private set; }

    /// <summary>The expression being typed.</summary>
    public string Expression { get; private set; } = "";

    /// <summary>Caret position in <see cref="Expression"/>.</summary>
    public int Caret { get; private set; }

    /// <summary>Cards, oldest first.</summary>
    public IReadOnlyList<ProgrammerCard> Cards => _cards;

    /// <summary>User-defined variable names.</summary>
    public IEnumerable<string> VariableNames => _variables.Keys;

    // ---- settings -----------------------------------------------------------------------------------------

    /// <summary>Changes the word size, keeping the value where it fits (wrapping when the word shrinks).</summary>
    public void SetWordSize(int bits)
    {
        if (bits == Format.Bits) return;
        var f = Format.WithBits(bits);
        Value = IntegerOps.Convert(f, Value);
        Format = f;
        OnChanged();
    }

    /// <summary>Switches signed or unsigned, keeping the bit pattern (0xFF becomes -1 in 8-bit signed).</summary>
    public void SetSigned(bool signed)
    {
        if (signed == Format.Signed) return;
        var f = Format.WithSigned(signed);
        Value = IntegerOps.Convert(f, Value);
        Format = f;
        OnChanged();
    }

    /// <summary>Changes what arithmetic does on overflow.</summary>
    public void SetOverflow(OverflowMode mode)
    {
        if (mode == Format.Overflow) return;
        Format = Format.WithOverflow(mode);
        OnChanged();
    }

    /// <summary>Changes the input base; unprefixed literals in the expression are rewritten so it keeps its meaning.</summary>
    public void SetInputBase(NumberBase b)
    {
        if (b == InputBase) return;
        bool atEnd = Caret >= Expression.Length;
        Expression = ExpressionText.ChangeBase(Expression, InputBase, b);
        Caret = atEnd ? Expression.Length : Math.Min(Caret, Expression.Length);
        InputBase = b;
        OnChanged();
    }

    // ---- value and expression -----------------------------------------------------------------------------

    /// <summary>Sets the current value (wrapped into the word).</summary>
    public void SetValue(BigInteger value)
    {
        Value = Format.Wrap(value);
        OnChanged();
    }

    /// <summary>Flips one bit of the current value (the bit grid).</summary>
    public void FlipBit(int index)
    {
        Value = IntegerOps.FlipBit(Format, Value, index);
        OnChanged();
    }

    /// <summary>Replaces the expression text and caret (from the text box).</summary>
    public void SetExpression(string text, int caret)
    {
        text ??= "";
        caret = Math.Clamp(caret, 0, text.Length);
        if (text == Expression && caret == Caret) return;
        Expression = text;
        Caret = caret;
        OnChanged();
    }

    /// <summary>The live result of the expression being typed (nothing is stored).</summary>
    public ProgrammerPreview GetPreview()
    {
        if (string.IsNullOrWhiteSpace(Expression)) return new ProgrammerPreview(Value, false, null, -1, []);
        try
        {
            var r = ExpressionEvaluator.Evaluate(Expression, Format, InputBase, this);
            return new ProgrammerPreview(r.Value, true, null, -1, r.Notes);
        }
        catch (ProgrammerException ex)
        {
            return new ProgrammerPreview(Value, false, ex.Message, ex.Position, []);
        }
    }

    /// <summary>
    /// Evaluates the expression and adds a card. On success the result becomes the current value and the expression is
    /// cleared; on error an error card is added and the expression is kept for fixing. Returns null for an empty expression.
    /// </summary>
    public ProgrammerCard? Evaluate()
    {
        var text = Expression.Trim();
        if (text.Length == 0) return null;
        ProgrammerCard card;
        try
        {
            var r = ExpressionEvaluator.Evaluate(text, Format, InputBase, this);
            card = new ProgrammerCard(_nextCard++, text, Format, InputBase, r.Value, r.AssignedName, r.Notes, null);
            Value = r.Value;
            if (r.AssignedName is { } name) _variables[name] = r.Value;
            Expression = "";
            Caret = 0;
        }
        catch (ProgrammerException ex)
        {
            card = new ProgrammerCard(_nextCard++, text, Format, InputBase, BigInteger.Zero, null, [], ex.Message);
        }
        _cards.Add(card);
        if (_cards.Count > MaxCards) _cards.RemoveAt(0);
        OnChanged();
        return card;
    }

    /// <summary>Evaluates <paramref name="expression"/> on its own (replacing the input) and adds a card.</summary>
    public ProgrammerCard? Apply(string expression)
    {
        Expression = expression;
        Caret = expression.Length;
        return Evaluate();
    }

    /// <summary>Clears the expression and sets the value to 0 (the C key).</summary>
    public void Clear()
    {
        Expression = "";
        Caret = 0;
        Value = BigInteger.Zero;
        OnChanged();
    }

    /// <summary>Escape: clears the expression, or the value when the expression is already empty.</summary>
    public void Escape()
    {
        if (Expression.Length > 0) { Expression = ""; Caret = 0; OnChanged(); }
        else if (!Value.IsZero) { Value = BigInteger.Zero; OnChanged(); }
    }

    /// <summary>Removes every card (variables and the value stay).</summary>
    public void ClearCards()
    {
        _cards.Clear();
        _nextCard = 1;
        OnChanged();
    }

    // ---- keypad -------------------------------------------------------------------------------------------

    /// <summary>Keys of the 8-column keypad, in reading order (as in the design).</summary>
    public static IReadOnlyList<string> KeypadKeys { get; } =
    [
        "A", "B", "C", "D", "E", "F", "AND", "OR",
        "7", "8", "9", "÷", "MOD", "(", "XOR", "NOT",
        "4", "5", "6", "×", "<<", ")", "NAND", "NOR",
        "1", "2", "3", "−", ">>", "⌫", "ROL", "ROR",
        "±", "0", ".", "+", "=", "CLR",
    ];

    private static readonly Dictionary<string, string> BinaryKeys = new()
    {
        ["AND"] = " AND ", ["OR"] = " OR ", ["XOR"] = " XOR ", ["NAND"] = " NAND ", ["NOR"] = " NOR ",
        ["<<"] = " << ", [">>"] = " >> ", ["+"] = " + ", ["−"] = " - ", ["×"] = " * ", ["÷"] = " / ", ["MOD"] = " MOD ",
    };

    /// <summary>True when the key does something in the current base (digits above the base are off; "." is always off).</summary>
    public bool IsKeyEnabled(string key)
    {
        if (key.Length == 1 && BaseFormatter.DigitValue(key[0]) >= 0) return BaseFormatter.IsDigit(key[0], InputBase);
        return key != ".";
    }

    /// <summary>
    /// Presses a keypad key. Digits and operators edit the expression at the caret (an operator on an empty input
    /// starts with ans). NOT, ±, ROL and ROR on an empty input act on the current value at once and add a card.
    /// Returns false when the key does nothing.
    /// </summary>
    public bool Press(string key)
    {
        if (!IsKeyEnabled(key)) return false;
        bool empty = string.IsNullOrWhiteSpace(Expression);
        if (key.Length == 1 && BaseFormatter.DigitValue(key[0]) >= 0) { InsertText(key); return true; }
        if (BinaryKeys.TryGetValue(key, out var op))
        {
            if (empty) { Expression = ""; Caret = 0; InsertText("ans" + op); }
            else InsertText(op);
            return true;
        }
        switch (key)
        {
            case "NOT":
                if (empty) Apply("NOT ans"); else InsertText("NOT ");
                return true;
            case "ROL":
                if (empty) Apply("ans ROL 1"); else InsertText(" ROL ");
                return true;
            case "ROR":
                if (empty) Apply("ans ROR 1"); else InsertText(" ROR ");
                return true;
            case "±":
                if (empty) { Apply("-ans"); return true; }
                (Expression, Caret) = ExpressionText.ToggleSign(Expression, Caret, InputBase);
                OnChanged();
                return true;
            case "(" or ")":
                InsertText(key);
                return true;
            case "⌫":
                return Backspace();
            case "=":
                return Evaluate() is not null;
            case "CLR":
                Clear();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Inserts text at the caret.</summary>
    public void InsertText(string text)
    {
        (Expression, Caret) = ExpressionText.Insert(Expression, Caret, text);
        OnChanged();
    }

    /// <summary>Deletes the character (or the whole word operator) before the caret.</summary>
    public bool Backspace()
    {
        if (Caret == 0) return false;
        int end = Caret;
        int start = end - 1;
        // A word operator inserted by the keypad (" AND ") goes in one press, with its spaces.
        int i = end;
        while (i > 0 && Expression[i - 1] == ' ') i--;
        int w = i;
        while (w > 0 && char.IsAsciiLetter(Expression[w - 1])) w--;
        if (w < i && (w == 0 || !char.IsAsciiLetterOrDigit(Expression[w - 1])) && ExpressionLexer.IsKeyword(Expression[w..i]))
        {
            start = w;
            while (start > 0 && Expression[start - 1] == ' ') start--;
        }
        Expression = Expression.Remove(start, end - start);
        Caret = start;
        OnChanged();
        return true;
    }

    // ---- variables ----------------------------------------------------------------------------------------

    /// <inheritdoc />
    public bool TryGetVariable(string name, out BigInteger value)
    {
        if (name.Equals("ans", StringComparison.OrdinalIgnoreCase)) { value = Value; return true; }
        return _variables.TryGetValue(name, out value);
    }

    /// <inheritdoc />
    public bool TryGetCard(int number, out BigInteger value)
    {
        foreach (var c in _cards)
        {
            if (c.Number == number && !c.IsError) { value = c.Value; return true; }
        }
        value = default;
        return false;
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

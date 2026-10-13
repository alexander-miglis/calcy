namespace Calcy.Modes.Simple;

/// <summary>Every key on the Simple keypad (plus CE, which is keyboard only).</summary>
public enum CalcKey
{
    Digit0,
    Digit1,
    Digit2,
    Digit3,
    Digit4,
    Digit5,
    Digit6,
    Digit7,
    Digit8,
    Digit9,
    Point,
    Add,
    Subtract,
    Multiply,
    Divide,
    Equals,
    Percent,
    SquareRoot,
    Square,
    Reciprocal,
    Negate,
    Backspace,
    Clear,
    ClearEntry,
    MemoryClear,
    MemoryRecall,
    MemoryAdd,
    MemorySubtract,
}

/// <summary>The four desk-calculator operators.</summary>
public enum CalcOp
{
    Add,
    Subtract,
    Multiply,
    Divide,
}

/// <summary>A function applied to one number on the tape (√9, 3², 1/4).</summary>
public enum TermFunction
{
    SquareRoot,
    Square,
    Reciprocal,
}

/// <summary>Labels, accessible names, keyboard hints and the keypad layout for <see cref="CalcKey"/>.</summary>
public static class CalcKeys
{
    /// <summary>The keypad, row by row (four columns; 0 spans two columns).</summary>
    public static readonly IReadOnlyList<CalcKey> Layout =
    [
        CalcKey.MemoryClear, CalcKey.MemoryRecall, CalcKey.MemoryAdd, CalcKey.MemorySubtract,
        CalcKey.Percent, CalcKey.SquareRoot, CalcKey.Square, CalcKey.Reciprocal,
        CalcKey.Clear, CalcKey.Backspace, CalcKey.Negate, CalcKey.Divide,
        CalcKey.Digit7, CalcKey.Digit8, CalcKey.Digit9, CalcKey.Multiply,
        CalcKey.Digit4, CalcKey.Digit5, CalcKey.Digit6, CalcKey.Subtract,
        CalcKey.Digit1, CalcKey.Digit2, CalcKey.Digit3, CalcKey.Add,
        CalcKey.Digit0, CalcKey.Point, CalcKey.Equals,
    ];

    /// <summary>True for the ten digit keys.</summary>
    public static bool IsDigit(CalcKey key) => key is >= CalcKey.Digit0 and <= CalcKey.Digit9;

    /// <summary>The digit character for a digit key.</summary>
    public static char DigitChar(CalcKey key) => (char)('0' + (key - CalcKey.Digit0));

    /// <summary>The text printed on the key.</summary>
    public static string Label(CalcKey key) => key switch
    {
        _ when IsDigit(key) => DigitChar(key).ToString(),
        CalcKey.Point => ".",
        CalcKey.Add => "+",
        CalcKey.Subtract => "−",
        CalcKey.Multiply => "×",
        CalcKey.Divide => "÷",
        CalcKey.Equals => "=",
        CalcKey.Percent => "%",
        CalcKey.SquareRoot => "√",
        CalcKey.Square => "x²",
        CalcKey.Reciprocal => "1/x",
        CalcKey.Negate => "±",
        CalcKey.Backspace => "⌫",
        CalcKey.Clear => "C",
        CalcKey.ClearEntry => "CE",
        CalcKey.MemoryClear => "MC",
        CalcKey.MemoryRecall => "MR",
        CalcKey.MemoryAdd => "M+",
        CalcKey.MemorySubtract => "M−",
        _ => key.ToString(),
    };

    /// <summary>The name read by screen readers.</summary>
    public static string AutomationName(CalcKey key) => key switch
    {
        _ when IsDigit(key) => DigitChar(key).ToString(),
        CalcKey.Point => "decimal point",
        CalcKey.Add => "plus",
        CalcKey.Subtract => "minus",
        CalcKey.Multiply => "times",
        CalcKey.Divide => "divide",
        CalcKey.Equals => "equals",
        CalcKey.Percent => "percent",
        CalcKey.SquareRoot => "square root",
        CalcKey.Square => "square",
        CalcKey.Reciprocal => "reciprocal",
        CalcKey.Negate => "change sign",
        CalcKey.Backspace => "backspace",
        CalcKey.Clear => "clear",
        CalcKey.ClearEntry => "clear entry",
        CalcKey.MemoryClear => "memory clear",
        CalcKey.MemoryRecall => "memory recall",
        CalcKey.MemoryAdd => "memory plus",
        CalcKey.MemorySubtract => "memory minus",
        _ => key.ToString(),
    };

    /// <summary>The keyboard shortcut shown in the key's tooltip.</summary>
    public static string ShortcutHint(CalcKey key) => key switch
    {
        _ when IsDigit(key) => DigitChar(key).ToString(),
        CalcKey.Point => ". or ,",
        CalcKey.Add => "+",
        CalcKey.Subtract => "-",
        CalcKey.Multiply => "*",
        CalcKey.Divide => "/",
        CalcKey.Equals => "Enter or =",
        CalcKey.Percent => "%",
        CalcKey.SquareRoot => "@",
        CalcKey.Square => "Q",
        CalcKey.Reciprocal => "R",
        CalcKey.Negate => "F9",
        CalcKey.Backspace => "Backspace",
        CalcKey.Clear => "Esc",
        CalcKey.ClearEntry => "Delete",
        CalcKey.MemoryClear => "Ctrl+L",
        CalcKey.MemoryRecall => "Ctrl+R",
        CalcKey.MemoryAdd => "Ctrl+P",
        CalcKey.MemorySubtract => "Ctrl+Q",
        _ => "",
    };

    /// <summary>Maps a typed character (from any keyboard layout, including the numpad) to a key.</summary>
    public static CalcKey? FromChar(char c) => c switch
    {
        >= '0' and <= '9' => CalcKey.Digit0 + (c - '0'),
        '.' or ',' => CalcKey.Point,
        '+' => CalcKey.Add,
        '-' or '−' => CalcKey.Subtract,
        '*' or '×' or 'x' or 'X' => CalcKey.Multiply,
        '/' or '÷' => CalcKey.Divide,
        '=' => CalcKey.Equals,
        '%' => CalcKey.Percent,
        '@' or '√' => CalcKey.SquareRoot,
        'q' or 'Q' or '²' => CalcKey.Square,
        'r' or 'R' => CalcKey.Reciprocal,
        '±' => CalcKey.Negate,
        _ => null,
    };

    /// <summary>The operator symbol used on the display and the tape.</summary>
    public static string Symbol(CalcOp op) => op switch
    {
        CalcOp.Add => "+",
        CalcOp.Subtract => "−",
        CalcOp.Multiply => "×",
        _ => "÷",
    };
}

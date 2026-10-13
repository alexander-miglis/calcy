using System.Collections.Immutable;
using Calcy.Core.Numbers;

namespace Calcy.Modes.Simple;

/// <summary>
/// The Simple mode calculator: an immediate-execution desk calculator with exact decimal arithmetic,
/// memory keys and an editable tape. It has no UI dependencies; the view only forwards key presses.
/// </summary>
/// <remarks>
/// Operators run left to right as they are pressed (2 + 3 × 4 = 20). Percent completes the calculation with
/// desk semantics (200 + 10% = 220, 200 − 10% = 180, 200 × 10% = 20). Pressing = again repeats the last
/// operation. √, x² and 1/x work on the displayed number: inside a calculation they change the current operand,
/// on their own they become a tape line. Every completed calculation is a <see cref="TapeLine"/>; numbers that
/// came from an earlier line keep a link to it, so editing that line recalculates everything that used it.
/// </remarks>
public sealed class SimpleCalculator
{
    /// <summary>The most digits that can be typed into one number.</summary>
    public const int MaxEntryDigits = 14;

    /// <summary>The oldest lines are dropped beyond this many.</summary>
    public const int MaxTapeLines = 500;

    private readonly TimeProvider _time;
    private readonly List<TapeLine> _tape = [];
    private readonly List<TapeStep> _steps = [];
    private TapeTerm? _first;
    private CalcOp? _pending;
    private BigRational _acc;
    private TapeTerm? _operand;
    private string? _entry;
    private CalcOp? _repeatOp;
    private TapeTerm? _repeatTerm;
    private TapeLine? _shownLine;
    private bool _error;
    private int _nextNumber = 1;

    /// <summary>Creates a calculator. <paramref name="time"/> stamps tape lines (defaults to the system clock).</summary>
    public SimpleCalculator(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The main display text ("1,234.5", "0.3333333333", "Error").</summary>
    public string Display { get; private set; } = "0";

    /// <summary>The small line above the display: the calculation so far ("2 + 3 ×") or just completed ("2 + 3 =").</summary>
    public string Expression { get; private set; } = "";

    /// <summary>The memory register.</summary>
    public BigRational Memory { get; private set; }

    /// <summary>True when memory holds a value other than zero (shows the M indicator).</summary>
    public bool HasMemory => !Memory.IsZero;

    /// <summary>True after a division by zero, a square root of a negative number or an overflow. C clears it.</summary>
    public bool IsError => _error;

    /// <summary>True while a number is being typed.</summary>
    public bool IsEntering => _entry is not null;

    /// <summary>The operator waiting for its second number, if any.</summary>
    public CalcOp? PendingOperator => _pending;

    /// <summary>The value on the display (null while it shows an error).</summary>
    public BigRational? Value => _error ? null : CurrentValue;

    /// <summary>The tape, oldest line first.</summary>
    public IReadOnlyList<TapeLine> Tape => _tape;

    /// <summary>Raised after the display, expression or memory changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised after lines were added, edited, dropped or cleared.</summary>
    public event EventHandler<TapeChangedEventArgs>? TapeChanged;

    private BigRational? CurrentValue => _operand is not null ? _operand.Value : _pending is not null ? _acc : BigRational.Zero;

    /// <summary>Presses a key.</summary>
    public void Press(CalcKey key)
    {
        switch (key)
        {
            case >= CalcKey.Digit0 and <= CalcKey.Digit9: Digit(CalcKeys.DigitChar(key)); break;
            case CalcKey.Point: Point(); break;
            case CalcKey.Add: Operator(CalcOp.Add); break;
            case CalcKey.Subtract: Operator(CalcOp.Subtract); break;
            case CalcKey.Multiply: Operator(CalcOp.Multiply); break;
            case CalcKey.Divide: Operator(CalcOp.Divide); break;
            case CalcKey.Equals: EqualsKey(); break;
            case CalcKey.Percent: Percent(); break;
            case CalcKey.SquareRoot: Function(TermFunction.SquareRoot); break;
            case CalcKey.Square: Function(TermFunction.Square); break;
            case CalcKey.Reciprocal: Function(TermFunction.Reciprocal); break;
            case CalcKey.Negate: Negate(); break;
            case CalcKey.Backspace: Backspace(); break;
            case CalcKey.Clear: Clear(); break;
            case CalcKey.ClearEntry: ClearEntry(); break;
            case CalcKey.MemoryClear: Memory = BigRational.Zero; break;
            case CalcKey.MemoryRecall: Recall(); break;
            case CalcKey.MemoryAdd: MemoryChange(add: true); break;
            case CalcKey.MemorySubtract: MemoryChange(add: false); break;
        }
        Refresh();
    }

    /// <summary>Presses several keys, for example from a test or a macro.</summary>
    public void Press(params ReadOnlySpan<CalcKey> keys)
    {
        foreach (var k in keys) Press(k);
    }

    /// <summary>Types a sequence of characters as if on the keyboard ("12.5+3=", "200+10%"). Unknown characters are ignored.</summary>
    public void Type(string keys)
    {
        foreach (var c in keys)
        {
            if (CalcKeys.FromChar(c) is { } k) Press(k);
        }
    }

    /// <summary>Puts a number on the display, as the operand of a pending calculation or as a new start.</summary>
    public void Enter(BigRational value)
    {
        if (_error) ResetAll();
        if (DeskMath.Bound(value) is not { } v)
        {
            Fail("Too large");
            Refresh();
            return;
        }
        _entry = null;
        StartValue();
        _operand = TapeTerm.FromLiteral(v);
        Refresh();
    }

    /// <summary>Parses a number (see <see cref="DeskMath.TryParseNumber"/>) and puts it on the display.</summary>
    public bool TryEnter(string text)
    {
        if (!DeskMath.TryParseNumber(text, out var v)) return false;
        Enter(v);
        return true;
    }

    /// <summary>Puts a tape line's result on the display ("Use"). The number stays linked to that line.</summary>
    public void Use(TapeLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Result is null) return;
        if (_error) ResetAll();
        _entry = null;
        if (_pending is null)
        {
            Expression = line.Expression + " =";
            _shownLine = line;
        }
        _operand = TapeTerm.FromLine(line);
        Refresh();
    }

    /// <summary>
    /// Changes the numbers of a tape line from its edit text (see <see cref="TapeLine.EditText"/>) and recalculates
    /// every later line that used it. Only numbers and "#n" references can change, not the operators or functions.
    /// </summary>
    public bool TryEdit(TapeLine line, string text, out string? error)
    {
        ArgumentNullException.ThrowIfNull(line);
        int index = _tape.IndexOf(line);
        if (index < 0)
        {
            error = "That line is no longer on the tape";
            return false;
        }
        if (!TapeEditText.TryParse(line.EditText, out var original, out _))
        {
            error = "This line cannot be edited";
            return false;
        }
        if (!TapeEditText.TryParse(text ?? "", out var edited, out error)) return false;
        if (edited.Shape != original.Shape)
        {
            error = "Only the numbers can change. Keep the operators as they are: " + line.EditText;
            return false;
        }

        var terms = line.Terms.ToList();
        var replaced = new List<TapeTerm>(terms.Count);
        for (int i = 0; i < terms.Count; i++)
        {
            var slot = edited.Slots[i];
            if (slot.Text == original.Slots[i].Text)
            {
                replaced.Add(terms[i]);
                continue;
            }
            if (slot.LineRef is { } n)
            {
                var source = _tape.Take(index).FirstOrDefault(l => l.Number == n);
                if (source is null)
                {
                    error = n >= line.Number ? $"#{n} is not an earlier line" : $"#{n} is no longer on the tape";
                    return false;
                }
                replaced.Add(terms[i].WithLineBase(source, slot.Negative));
                continue;
            }
            if (!DeskMath.TryParseNumber(slot.Number, out var value))
            {
                error = $"\"{slot.Text.Trim()}\" is not a number";
                return false;
            }
            replaced.Add(terms[i].WithLiteralBase(slot.Negative ? -value : value));
        }

        line.Replace(replaced[0], [.. line.Steps.Select((s, i) => s with { Term = replaced[i + 1] })]);
        line.IsEdited = true;
        for (int i = index; i < _tape.Count; i++) _tape[i].Recalculate();
        error = null;
        RefreshAfterRecalculation();
        TapeChanged?.Invoke(this, new TapeChangedEventArgs(TapeChange.Recalculated, line));
        Refresh();
        return true;
    }

    /// <summary>Removes every line from the tape. Numbers on the display keep their values.</summary>
    public void ClearTape()
    {
        foreach (var line in _tape) Detach(line);
        _tape.Clear();
        _nextNumber = 1;
        TapeChanged?.Invoke(this, new TapeChangedEventArgs(TapeChange.Cleared, null));
        Refresh();
    }

    /// <summary>The whole tape as text, oldest line first (one line per calculation).</summary>
    public string ExportTape() => string.Join(Environment.NewLine, _tape.Select(l => l.ExportText));

    // Keys

    private void Digit(char d)
    {
        if (_error) return;
        if (_entry is null) StartEntry();
        var (sign, body) = SplitSign(_entry!);
        if (CountDigits(body) >= MaxEntryDigits) return;
        SetEntry(sign + (body == "0" ? d.ToString() : body + d));
    }

    private void Point()
    {
        if (_error) return;
        if (_entry is null) StartEntry();
        if (_entry!.Contains('.')) return;
        SetEntry(_entry + ".");
    }

    private void Operator(CalcOp op)
    {
        if (_error) return;
        if (_operand is null && _pending is not null)
        {
            _pending = op;
            Expression = ChainText() + " " + CalcKeys.Symbol(op);
            return;
        }
        var term = CurrentTerm();
        _entry = null;
        if (term.Value is not { } v)
        {
            Fail(term.Text);
            return;
        }
        if (_pending is not { } previous)
        {
            _first = term;
            _steps.Clear();
            _acc = v;
        }
        else
        {
            _steps.Add(new TapeStep(previous, term));
            if (DeskMath.Apply(_acc, previous, v) is not { } r)
            {
                Fail(ChainText() + " =");
                return;
            }
            _acc = r;
        }
        _pending = op;
        _operand = null;
        _shownLine = null;
        Expression = ChainText() + " " + CalcKeys.Symbol(op);
    }

    private void EqualsKey()
    {
        if (_error) return;
        if (_pending is { } op)
        {
            var term = _operand ?? TapeTerm.FromLiteral(_acc);
            Complete(_first!, [.. _steps, new TapeStep(op, term)], op, term);
        }
        else if (_repeatOp is { } rop && _repeatTerm is { } rterm && _operand is { } current)
        {
            Complete(current, [new TapeStep(rop, rterm)], rop, rterm);
        }
        else
        {
            _entry = null;
        }
    }

    private void Percent()
    {
        if (_error) return;
        var term = CurrentTerm().AsPercent();
        _entry = null;
        if (_pending is { } op) Complete(_first!, [.. _steps, new TapeStep(op, term)], null, null);
        else Complete(term, [], null, null);
    }

    private void Function(TermFunction f)
    {
        if (_error) return;
        var term = CurrentTerm().With(f);
        _entry = null;
        if (_pending is { } op)
        {
            var text = ChainText() + " " + CalcKeys.Symbol(op) + " " + term.Text;
            if (term.Value is null)
            {
                Fail(text);
                return;
            }
            _operand = term;
            Expression = text;
        }
        else
        {
            Complete(term, [], null, null);
        }
    }

    private void Negate()
    {
        if (_error) return;
        if (_entry is not null)
        {
            var (sign, body) = SplitSign(_entry);
            SetEntry(sign.Length > 0 ? body : "-" + body);
        }
        else if (_operand is not null)
        {
            _operand = _operand.Negated();
        }
        else if (_pending is not null)
        {
            _operand = TapeTerm.FromLiteral(-_acc);
        }
    }

    private void Backspace()
    {
        if (_error)
        {
            ResetAll();
            return;
        }
        if (_entry is not null)
        {
            var e = _entry[..^1];
            SetEntry(e is "" or "-" ? "0" : e);
        }
        else if (_operand is not null)
        {
            SetEntry("0");
        }
    }

    private void Clear() => ResetAll();

    private void ClearEntry()
    {
        if (_error || (_operand is null && _pending is null))
        {
            ResetAll();
            return;
        }
        SetEntry("0");
    }

    private void Recall()
    {
        if (_error) return;
        _entry = null;
        StartValue();
        _operand = TapeTerm.FromLiteral(Memory);
    }

    private void MemoryChange(bool add)
    {
        if (_error || CurrentValue is not { } v) return;
        var m = DeskMath.Bound(add ? Memory + v : Memory - v);
        if (m is null)
        {
            Fail("Memory overflow");
            return;
        }
        Memory = m.Value;
        _entry = null;
    }

    // State helpers

    private TapeTerm CurrentTerm() => _operand ?? TapeTerm.FromLiteral(CurrentValue ?? BigRational.Zero);

    private void StartEntry()
    {
        StartValue();
        SetEntry("0");
    }

    /// <summary>A new number is starting. Without a pending operator it begins a new calculation.</summary>
    private void StartValue()
    {
        if (_pending is null)
        {
            Expression = "";
            _shownLine = null;
        }
    }

    private void SetEntry(string entry)
    {
        _entry = entry;
        _operand = TapeTerm.FromLiteral(BigRational.ParseDecimal(entry.EndsWith('.') ? entry[..^1] : entry));
    }

    private void Complete(TapeTerm first, ImmutableArray<TapeStep> steps, CalcOp? repeatOp, TapeTerm? repeatTerm)
    {
        var text = TapeLine.Render(first, steps);
        if (TapeLine.Evaluate(first, steps) is null)
        {
            Fail(text + " =");
            return;
        }
        var line = new TapeLine(_nextNumber++, _time.GetLocalNow(), first, steps);
        _tape.Add(line);
        _repeatOp = repeatOp;
        _repeatTerm = repeatTerm?.WithoutPercent();
        ResetChain();
        _entry = null;
        _operand = TapeTerm.FromLine(line);
        Expression = text + " =";
        _shownLine = line;
        TapeChanged?.Invoke(this, new TapeChangedEventArgs(TapeChange.Added, line));
        while (_tape.Count > MaxTapeLines)
        {
            var oldest = _tape[0];
            _tape.RemoveAt(0);
            foreach (var l in _tape) l.DetachFrom(oldest);
            Detach(oldest);
            TapeChanged?.Invoke(this, new TapeChangedEventArgs(TapeChange.Removed, oldest));
        }
    }

    private void Fail(string expression)
    {
        ResetChain();
        _entry = null;
        _operand = null;
        _repeatOp = null;
        _repeatTerm = null;
        _shownLine = null;
        _error = true;
        Expression = expression;
    }

    private void ResetChain()
    {
        _first = null;
        _steps.Clear();
        _pending = null;
        _acc = BigRational.Zero;
    }

    private void ResetAll()
    {
        ResetChain();
        _entry = null;
        _operand = null;
        _repeatOp = null;
        _repeatTerm = null;
        _shownLine = null;
        _error = false;
        Expression = "";
    }

    /// <summary>Replaces live references to a line that leaves the tape with its value.</summary>
    private void Detach(TapeLine line)
    {
        _operand = _operand?.DetachedFrom(line);
        _first = _first?.DetachedFrom(line);
        _repeatTerm = _repeatTerm?.DetachedFrom(line);
        for (int i = 0; i < _steps.Count; i++) _steps[i] = _steps[i] with { Term = _steps[i].Term.DetachedFrom(line) };
        if (_shownLine == line) _shownLine = null;
    }

    /// <summary>After an edit: the running total and the expression follow the recalculated lines.</summary>
    private void RefreshAfterRecalculation()
    {
        if (_error) return;
        if (_pending is { } op && _first is not null)
        {
            if (TapeLine.Evaluate(_first, _steps) is not { } acc)
            {
                Fail(ChainText() + " =");
                return;
            }
            _acc = acc;
            Expression = ChainText() + " " + CalcKeys.Symbol(op) + (_operand is { Functions.IsEmpty: false } t ? " " + t.Text : "");
        }
        else if (_shownLine is not null)
        {
            Expression = _shownLine.Expression + " =";
        }
    }

    private string ChainText() => _first is null ? "" : TapeLine.Render(_first, _steps);

    private void Refresh()
    {
        if (_error) Display = "Error";
        else if (_entry is not null) Display = DisplayFormat.FormatEntry(_entry);
        else Display = CurrentValue is { } v ? DisplayFormat.Format(v) : "Error";
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static (string Sign, string Body) SplitSign(string entry) =>
        entry.StartsWith('-') ? ("-", entry[1..]) : ("", entry);

    private static int CountDigits(string body)
    {
        int n = body.Count(char.IsAsciiDigit);
        return body.StartsWith("0.", StringComparison.Ordinal) ? n - 1 : n;
    }
}

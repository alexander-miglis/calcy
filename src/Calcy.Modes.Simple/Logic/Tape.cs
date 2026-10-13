using System.Collections.Immutable;
using System.Globalization;
using Calcy.Core.Numbers;

namespace Calcy.Modes.Simple;

/// <summary>
/// One number in a tape line: a literal (typed, recalled from memory, received from another mode) or the result
/// of an earlier line, optionally with functions applied (√, x², 1/x) and an optional trailing percent.
/// Terms are immutable; editing a line replaces its terms.
/// </summary>
public sealed class TapeTerm
{
    private TapeTerm(BigRational literal, TapeLine? source, bool negatesSource, ImmutableArray<TermFunction> functions, bool percent)
    {
        Literal = literal;
        Source = source;
        NegatesSource = negatesSource;
        Functions = functions;
        IsPercent = percent;
    }

    /// <summary>The number itself when <see cref="Source"/> is null.</summary>
    public BigRational Literal { get; }

    /// <summary>The earlier line whose result this term uses, or null for a literal.</summary>
    public TapeLine? Source { get; }

    /// <summary>True when the term is minus the source line's result.</summary>
    public bool NegatesSource { get; }

    /// <summary>Functions applied to the base number, innermost first.</summary>
    public ImmutableArray<TermFunction> Functions { get; }

    /// <summary>True for a percent operand (always the last term of its line).</summary>
    public bool IsPercent { get; }

    /// <summary>A literal number.</summary>
    public static TapeTerm FromLiteral(BigRational value) => new(value, null, false, [], false);

    /// <summary>The result of an earlier line; follows that line when it is edited.</summary>
    public static TapeTerm FromLine(TapeLine line) => new(BigRational.Zero, line, false, [], false);

    /// <summary>The base number before functions (null when the source line is an error).</summary>
    public BigRational? BaseValue => Source is null ? Literal : Source.Result is { } r ? (NegatesSource ? -r : r) : null;

    /// <summary>The term's value with its functions applied (percent is applied by the line). Null means error.</summary>
    public BigRational? Value
    {
        get
        {
            var v = BaseValue;
            foreach (var f in Functions)
            {
                if (v is null) return null;
                v = DeskMath.Apply(f, v.Value);
            }
            return v;
        }
    }

    /// <summary>How the term reads on the tape, for example "√2", "(1.5)²" or "10%".</summary>
    public string Text => Render(edit: false);

    /// <summary>The editable form: plain numbers, and "#n" for the result of line n.</summary>
    public string EditText => Render(edit: true);

    internal string BaseEditText => BaseText(edit: true);

    internal TapeTerm With(TermFunction f) => new(Literal, Source, NegatesSource, Functions.Add(f), IsPercent);

    internal TapeTerm AsPercent() => new(Literal, Source, NegatesSource, Functions, true);

    internal TapeTerm WithoutPercent() => IsPercent ? new(Literal, Source, NegatesSource, Functions, false) : this;

    internal TapeTerm WithLiteralBase(BigRational value) => new(value, null, false, Functions, IsPercent);

    internal TapeTerm WithLineBase(TapeLine line, bool negate) => new(BigRational.Zero, line, negate, Functions, IsPercent);

    /// <summary>The term with its sign changed (the ± key on a value that is not being typed).</summary>
    internal TapeTerm Negated()
    {
        if (Functions.IsEmpty && !IsPercent)
            return Source is null ? FromLiteral(-Literal) : new TapeTerm(BigRational.Zero, Source, !NegatesSource, [], false);
        return Value is { } v ? FromLiteral(-v) : this;
    }

    /// <summary>Replaces a reference to <paramref name="line"/> with its current value (used when the line leaves the tape).</summary>
    internal TapeTerm DetachedFrom(TapeLine line)
    {
        if (Source != line || BaseValue is not { } v) return this;
        return new TapeTerm(v, null, false, Functions, IsPercent);
    }

    private string BaseText(bool edit)
    {
        if (Source is null) return DisplayFormat.Format(Literal, grouping: !edit);
        var sign = NegatesSource ? "-" : "";
        if (edit) return sign + "#" + Source.Number.ToString(CultureInfo.InvariantCulture);
        return BaseValue is { } v ? DisplayFormat.Format(v) : sign + "Error";
    }

    private string Render(bool edit)
    {
        var s = BaseText(edit);
        int applied = 0;
        foreach (var f in Functions)
        {
            bool wrap = s.StartsWith('-') || (applied > 0 && !(f == TermFunction.SquareRoot && Functions[applied - 1] == TermFunction.SquareRoot));
            var inner = wrap ? "(" + s + ")" : s;
            s = f switch
            {
                TermFunction.SquareRoot => "√" + inner,
                TermFunction.Square => inner + "²",
                _ => "1/" + inner,
            };
            applied++;
        }
        return IsPercent ? s + "%" : s;
    }
}

/// <summary>An operator and the number it applies to, for example "× 12".</summary>
public sealed record TapeStep(CalcOp Op, TapeTerm Term);

/// <summary>
/// One calculation on the tape: a first number and zero or more operator steps, evaluated left to right
/// like a desk calculator. Lines can use the results of earlier lines, so editing a line recalculates the ones below.
/// </summary>
public sealed class TapeLine
{
    private TapeTerm _first;
    private ImmutableArray<TapeStep> _steps;

    internal TapeLine(int number, DateTimeOffset time, TapeTerm first, ImmutableArray<TapeStep> steps)
    {
        Number = number;
        Time = time;
        _first = first;
        _steps = steps;
        Result = Evaluate(first, steps);
    }

    /// <summary>The line number shown as "#n" (1 is the oldest line).</summary>
    public int Number { get; }

    /// <summary>When the calculation was made.</summary>
    public DateTimeOffset Time { get; }

    /// <summary>The first number.</summary>
    public TapeTerm First => _first;

    /// <summary>The operator steps after the first number.</summary>
    public ImmutableArray<TapeStep> Steps => _steps;

    /// <summary>The result, or null when the line is an error (for example after editing a divisor to 0).</summary>
    public BigRational? Result { get; private set; }

    /// <summary>True when the result is an error.</summary>
    public bool IsError => Result is null;

    /// <summary>True once the user has edited this line.</summary>
    public bool IsEdited { get; internal set; }

    /// <summary>All terms, first number included, in reading order.</summary>
    public IEnumerable<TapeTerm> Terms => _steps.Select(s => s.Term).Prepend(_first);

    /// <summary>The calculation as it reads on the tape, for example "250 + 15%".</summary>
    public string Expression => Render(_first, _steps, edit: false);

    /// <summary>The editable form of the calculation (plain numbers, "#n" for earlier results).</summary>
    public string EditText => Render(_first, _steps, edit: true);

    /// <summary>The result as shown on the tape ("1,234.5" or "Error").</summary>
    public string ResultText => Result is { } r ? DisplayFormat.Format(r) : "Error";

    /// <summary>The earlier lines this line uses.</summary>
    public IEnumerable<TapeLine> Sources => Terms.Select(t => t.Source).OfType<TapeLine>().Distinct();

    /// <summary>One line of the exported tape.</summary>
    public string ExportText => $"#{Number.ToString(CultureInfo.InvariantCulture)}  {Time.ToString("HH:mm", CultureInfo.InvariantCulture)}  {Expression} = {ResultText}";

    internal void Replace(TapeTerm first, ImmutableArray<TapeStep> steps)
    {
        _first = first;
        _steps = steps;
    }

    /// <summary>Re-evaluates with the current values of the lines it uses. Returns true when the result changed.</summary>
    internal bool Recalculate()
    {
        var old = Result;
        Result = Evaluate(_first, _steps);
        return old != Result;
    }

    internal void DetachFrom(TapeLine removed)
    {
        if (!Sources.Contains(removed)) return;
        _first = _first.DetachedFrom(removed);
        _steps = [.. _steps.Select(s => s with { Term = s.Term.DetachedFrom(removed) })];
    }

    /// <summary>Evaluates a calculation left to right with desk-calculator percent. Null means error.</summary>
    public static BigRational? Evaluate(TapeTerm first, IEnumerable<TapeStep> steps)
    {
        var acc = first.Value;
        if (acc is null) return null;
        if (first.IsPercent) acc = DeskMath.PercentOperand(acc.Value, null, acc.Value);
        foreach (var step in steps)
        {
            var v = step.Term.Value;
            if (v is null) return null;
            if (step.Term.IsPercent) v = DeskMath.PercentOperand(acc.Value, step.Op, v.Value);
            acc = DeskMath.Apply(acc.Value, step.Op, v.Value);
            if (acc is null) return null;
        }
        return acc;
    }

    /// <summary>Renders a calculation, for example "2 + 3 × 4".</summary>
    public static string Render(TapeTerm first, IEnumerable<TapeStep> steps, bool edit = false)
    {
        var parts = new List<string> { edit ? first.EditText : first.Text };
        foreach (var s in steps)
        {
            parts.Add(CalcKeys.Symbol(s.Op));
            parts.Add(edit ? s.Term.EditText : s.Term.Text);
        }
        return string.Join(" ", parts);
    }
}

/// <summary>What happened to the tape.</summary>
public enum TapeChange
{
    /// <summary>A new line was added (<see cref="TapeChangedEventArgs.Line"/>).</summary>
    Added,

    /// <summary>The oldest line was dropped to keep the tape within its size limit.</summary>
    Removed,

    /// <summary>A line was edited and the lines below it recalculated.</summary>
    Recalculated,

    /// <summary>The tape was cleared.</summary>
    Cleared,
}

/// <summary>Details of a tape change.</summary>
public sealed class TapeChangedEventArgs(TapeChange change, TapeLine? line) : EventArgs
{
    public TapeChange Change { get; } = change;

    /// <summary>The added, removed or edited line.</summary>
    public TapeLine? Line { get; } = line;
}

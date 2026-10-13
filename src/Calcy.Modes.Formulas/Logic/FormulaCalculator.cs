using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;
using Calcy.Core.Printing;
using Calcy.Engine;
using Calcy.Formulas;

namespace Calcy.Modes.Formulas.Logic;

/// <summary>A request to solve (or check) a formula: the formula, the text of every input box and the unknown.</summary>
/// <param name="Formula">The formula.</param>
/// <param name="Texts">Box text by variable symbol; missing or empty entries are empty boxes.</param>
/// <param name="Unknown">The variable to solve for; ignored for identities, and the default unknown when null.</param>
public sealed record FormulaJob(Formula Formula, IReadOnlyDictionary<string, string> Texts, string? Unknown);

/// <summary>How a job ended.</summary>
public enum OutcomeKind
{
    /// <summary>There is an answer.</summary>
    Answered,

    /// <summary>Some boxes are still empty.</summary>
    NeedsInput,

    /// <summary>Some boxes hold text that cannot be used (see <see cref="FormulaOutcome.InputErrors"/>).</summary>
    Invalid,

    /// <summary>The values are fine but the formula has no valid answer for them.</summary>
    Failed,

    /// <summary>The work was stopped (cancelled or too slow).</summary>
    Cancelled,
}

/// <summary>The result of a job: an answer, or a plain message saying what is missing or wrong.</summary>
/// <param name="Kind">How the job ended.</param>
/// <param name="Message">Why there is no answer ("Fill in u to find a"); empty when answered.</param>
/// <param name="InputErrors">Problems with individual boxes, by variable symbol.</param>
/// <param name="Answer">The answer, when <paramref name="Kind"/> is <see cref="OutcomeKind.Answered"/>.</param>
public sealed record FormulaOutcome(OutcomeKind Kind, string Message, IReadOnlyDictionary<string, string> InputErrors, FormulaAnswer? Answer)
{
    private static readonly IReadOnlyDictionary<string, string> NoErrors = new Dictionary<string, string>();

    /// <summary>An outcome with an answer.</summary>
    public static FormulaOutcome Of(FormulaAnswer answer) => new(OutcomeKind.Answered, "", NoErrors, answer);

    /// <summary>An outcome without an answer.</summary>
    public static FormulaOutcome Without(OutcomeKind kind, string message, IReadOnlyDictionary<string, string>? errors = null) =>
        new(kind, message, errors ?? NoErrors, null);
}

/// <summary>
/// Solves formulas from the collection for any variable, or checks an identity, from the text of the input boxes.
/// Physical constants are filled in, empty boxes use the usual value where there is one, solutions outside the
/// variable's limits are dropped, and every problem comes back as a plain message instead of an exception.
/// Thread safe as long as the session is not changed meanwhile; the view runs it off the UI thread.
/// </summary>
public sealed class FormulaCalculator
{
    /// <summary>The most solutions reported for one answer.</summary>
    public const int MaxSolutions = 6;

    private readonly CalcSession _session;
    private readonly FormulaLibrary _library;

    /// <summary>Creates a calculator on a session (variables and settings) and a library (the default one when null).</summary>
    public FormulaCalculator(CalcSession session, FormulaLibrary? library = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _library = library ?? FormulaLibrary.Default;
    }

    /// <summary>The variable solved for when nothing else was picked: the worked example's unknown, else the first variable.</summary>
    public static string DefaultUnknown(Formula formula)
    {
        ArgumentNullException.ThrowIfNull(formula);
        if (formula.Example?.SolveFor is { } s && formula.GetVariable(s) is not null) return s;
        return formula.Variables.Count > 0 ? formula.Variables[0].Symbol : "";
    }

    /// <summary>Solves or checks, whichever the formula needs.</summary>
    public FormulaOutcome Run(FormulaJob job, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        return job.Formula.IsIdentity
            ? Check(job.Formula, job.Texts, ct)
            : Solve(job.Formula, job.Texts, job.Unknown ?? DefaultUnknown(job.Formula), ct);
    }

    /// <summary>Solves <paramref name="formula"/> for <paramref name="unknown"/> from the box texts.</summary>
    public FormulaOutcome Solve(Formula formula, IReadOnlyDictionary<string, string> texts, string unknown, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(formula);
        ArgumentNullException.ThrowIfNull(texts);
        if (formula.IsIdentity)
            return FormulaOutcome.Without(OutcomeKind.Failed, "This identity holds for every value, so there is nothing to solve: use CHECK");
        var target = formula.GetVariable(unknown);
        if (target is null) return FormulaOutcome.Without(OutcomeKind.Failed, $"{formula.Name} has no variable {unknown}");

        try
        {
            var errors = new Dictionary<string, string>(StringComparer.Ordinal);
            var missing = new List<FormulaVariable>();
            var known = new Dictionary<string, Expr>(StringComparer.Ordinal);
            var given = new List<GivenValue>();
            foreach (var v in formula.Variables)
            {
                if (v.Symbol == unknown) continue;
                var reading = InputReader.Read(_session, v, texts.GetValueOrDefault(v.Symbol), ct);
                switch (reading.Source)
                {
                    case ValueSource.Typed:
                        known[v.Symbol] = reading.Value!;
                        given.Add(new GivenValue(v.Symbol, reading.Text, v.Unit, false));
                        break;
                    case ValueSource.Default:
                        // Left out of the known values so the solver uses the usual value and says so in a note.
                        given.Add(new GivenValue(v.Symbol, SymbolText.Literal(reading.Approx), v.Unit, true));
                        break;
                    case ValueSource.Missing:
                        missing.Add(v);
                        break;
                    default:
                        errors[v.Symbol] = reading.Error ?? "This is not a number";
                        break;
                }
            }
            if (errors.Count > 0)
            {
                var first = errors.First();
                return FormulaOutcome.Without(OutcomeKind.Invalid, $"{SymbolText.Plain(first.Key)}: {first.Value}", errors);
            }
            if (missing.Count > 0)
                return FormulaOutcome.Without(OutcomeKind.NeedsInput, $"Fill in {Names(missing)} to find {SymbolText.Plain(unknown)}");

            var result = FormulaSolver.Solve(_session, formula, known, unknown, _library, ct);
            var values = result.Solutions
                .OrderBy(s => Math.Abs(s.Approx))
                .ThenByDescending(s => s.Approx)
                .Take(MaxSolutions)
                .Select(s => Value(s.Value, s.Approx, s.Exact))
                .ToArray();
            return FormulaOutcome.Of(new FormulaAnswer
            {
                Formula = formula,
                Kind = AnswerKind.Solved,
                Variable = target,
                Unit = result.Unit,
                Values = values,
                Given = given,
                Notes = result.Notes,
            });
        }
        catch (OperationCanceledException)
        {
            return FormulaOutcome.Without(OutcomeKind.Cancelled, "Stopped: this took too long");
        }
        catch (CalcException ex)
        {
            return FormulaOutcome.Without(OutcomeKind.Failed, ex.Message);
        }
        catch (Exception ex) when (ex is ArithmeticException or InvalidOperationException or ArgumentException or InsufficientExecutionStackException)
        {
            return FormulaOutcome.Without(OutcomeKind.Failed, $"No valid value of {SymbolText.Plain(unknown)} fits these numbers");
        }
    }

    /// <summary>Evaluates both sides of an identity with the box values and says whether they agree.</summary>
    public FormulaOutcome Check(Formula formula, IReadOnlyDictionary<string, string> texts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(formula);
        ArgumentNullException.ThrowIfNull(texts);
        try
        {
            var errors = new Dictionary<string, string>(StringComparer.Ordinal);
            var missing = new List<FormulaVariable>();
            var locals = new Dictionary<string, Expr>(StringComparer.Ordinal);
            var given = new List<GivenValue>();
            foreach (var c in _library.ConstantsFor(formula)) locals[c.Symbol] = new NumberExpr(Number.Approx(c.Value));
            foreach (var v in formula.Variables)
            {
                var reading = InputReader.Read(_session, v, texts.GetValueOrDefault(v.Symbol), ct);
                switch (reading.Source)
                {
                    case ValueSource.Typed:
                    case ValueSource.Default:
                        locals[v.Symbol] = reading.Value!;
                        given.Add(new GivenValue(v.Symbol, reading.Source == ValueSource.Typed ? reading.Text : SymbolText.Literal(reading.Approx), v.Unit,
                            reading.Source == ValueSource.Default));
                        break;
                    case ValueSource.Missing:
                        missing.Add(v);
                        break;
                    default:
                        errors[v.Symbol] = reading.Error ?? "This is not a number";
                        break;
                }
            }
            if (errors.Count > 0)
            {
                var first = errors.First();
                return FormulaOutcome.Without(OutcomeKind.Invalid, $"{SymbolText.Plain(first.Key)}: {first.Value}", errors);
            }
            if (missing.Count > 0)
                return FormulaOutcome.Without(OutcomeKind.NeedsInput, $"Fill in {Names(missing)} to check both sides");

            var ctx = _session.Context(true, ct).WithLocals(locals);
            var left = Evaluator.Eval(FormulaSolver.Parse(_session, formula, formula.LeftSide, _library), ctx);
            var right = Evaluator.Eval(FormulaSolver.Parse(_session, formula, formula.RightSide, _library), ctx);
            if (left is not NumberExpr { Value: { IsReal: true } l } || right is not NumberExpr { Value: { IsReal: true } r }
                || !double.IsFinite(l.Re) || !double.IsFinite(r.Re))
                return FormulaOutcome.Without(OutcomeKind.Failed, "Not defined for these values");
            double a = l.Re, b = r.Re;
            bool holds = Math.Abs(a - b) <= 1e-9 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
            return FormulaOutcome.Of(new FormulaAnswer
            {
                Formula = formula,
                Kind = AnswerKind.Checked,
                Left = Value(new NumberExpr(Number.Approx(a)), a, exact: false),
                Right = Value(new NumberExpr(Number.Approx(b)), b, exact: false),
                Holds = holds,
                Given = given,
            });
        }
        catch (OperationCanceledException)
        {
            return FormulaOutcome.Without(OutcomeKind.Cancelled, "Stopped: this took too long");
        }
        catch (CalcException ex)
        {
            return FormulaOutcome.Without(OutcomeKind.Failed, ex.Message);
        }
        catch (Exception ex) when (ex is ArithmeticException or InvalidOperationException or ArgumentException or InsufficientExecutionStackException)
        {
            return FormulaOutcome.Without(OutcomeKind.Failed, "Not defined for these values");
        }
    }

    /// <summary>The texts for one solution: decimal, exact form (when it adds something) and the USE expression.</summary>
    private AnswerValue Value(Expr value, double approx, bool exact)
    {
        var options = NumberFormatOptions.From(_session.Settings);
        var number = SymbolText.Number(approx, options);
        if (exact && CalcSession.IsNiceExact(value))
        {
            var printed = _session.Print(value);
            return new AnswerValue(number, printed == number ? null : printed, ExprPrinter.ToInput(value), approx);
        }
        return new AnswerValue(number, null, SymbolText.Literal(approx), approx);
    }

    /// <summary>"u", "u and t", "u, a and t".</summary>
    private static string Names(IReadOnlyList<FormulaVariable> variables)
    {
        var names = variables.Select(v => SymbolText.Plain(v.Symbol)).ToArray();
        return names.Length == 1 ? $"{names[0]} ({variables[0].Name})" : string.Join(", ", names[..^1]) + " and " + names[^1];
    }
}

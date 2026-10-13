using System.Globalization;
using Calcy.Statistics;

namespace Calcy.Modes.Finance.Logic;

/// <summary>The text typed into the TVM solver (each field may hold an expression). Blank means "solve for this".</summary>
public sealed class TvmInputs
{
    /// <summary>N: number of payments.</summary>
    public string N { get; set; } = "";

    /// <summary>I%: annual interest rate in percent.</summary>
    public string InterestRate { get; set; } = "";

    /// <summary>PV: present value.</summary>
    public string PresentValue { get; set; } = "";

    /// <summary>PMT: payment per period.</summary>
    public string Payment { get; set; } = "";

    /// <summary>FV: future value.</summary>
    public string FutureValue { get; set; } = "";

    /// <summary>P/Y: payments per year.</summary>
    public string PaymentsPerYear { get; set; } = "12";

    /// <summary>C/Y: compounding periods per year (blank means the same as P/Y).</summary>
    public string CompoundingsPerYear { get; set; } = "12";

    /// <summary>Payments at the end (ordinary annuity) or the beginning (annuity due) of each period.</summary>
    public PaymentTiming Timing { get; set; } = PaymentTiming.End;

    /// <summary>The first-run values from the design: a 20,000 loan over 60 months at 4.5 %, solving PMT.</summary>
    public static TvmInputs Default() => new() { N = "60", InterestRate = "4.5", PresentValue = "20000", Payment = "", FutureValue = "0" };

    /// <summary>The text of one of the five TVM variables.</summary>
    public string Get(TvmVariable v) => v switch
    {
        TvmVariable.N => N,
        TvmVariable.InterestRate => InterestRate,
        TvmVariable.PresentValue => PresentValue,
        TvmVariable.Payment => Payment,
        TvmVariable.FutureValue => FutureValue,
        _ => throw new ArgumentOutOfRangeException(nameof(v)),
    };

    /// <summary>Replaces the text of one of the five TVM variables.</summary>
    public void Set(TvmVariable v, string text)
    {
        switch (v)
        {
            case TvmVariable.N: N = text; break;
            case TvmVariable.InterestRate: InterestRate = text; break;
            case TvmVariable.PresentValue: PresentValue = text; break;
            case TvmVariable.Payment: Payment = text; break;
            case TvmVariable.FutureValue: FutureValue = text; break;
            default: throw new ArgumentOutOfRangeException(nameof(v));
        }
    }

    /// <summary>A copy of these inputs.</summary>
    public TvmInputs Clone() => (TvmInputs)MemberwiseClone();
}

/// <summary>A solved TVM problem: all five values filled in.</summary>
public sealed record TvmSolution(TvmVariable Solved, TvmValues Values)
{
    /// <summary>The value that was worked out.</summary>
    public double Value => TvmWorksheet.Get(Values, Solved);

    /// <summary>Sum of all regular payments, PMT × N (negative when you pay).</summary>
    public double TotalPayments => Values.Payment * Values.N;

    /// <summary>
    /// The sum of every cash flow, PV + PMT × N + FV: the interest you pay (negative) or earn (positive) over the whole term.
    /// </summary>
    public double NetInterest => Values.PresentValue + Values.Payment * Values.N + Values.FutureValue;
}

/// <summary>The result of reading and solving the TVM fields.</summary>
/// <param name="Target">The variable being solved, or null when it is unclear which one.</param>
/// <param name="Solution">The solution, when every other field is valid and a solution exists.</param>
/// <param name="Problem">Why there is no solution, in plain words (null when solved).</param>
/// <param name="FieldErrors">Messages for individual fields, keyed by <see cref="TvmWorksheet.Key"/>.</param>
public sealed record TvmOutcome(TvmVariable? Target, TvmSolution? Solution, string? Problem, IReadOnlyDictionary<string, string> FieldErrors);

/// <summary>Reads the TVM fields and solves for the blank one (or a chosen one) with <see cref="Tvm"/>.</summary>
public static class TvmWorksheet
{
    /// <summary>The five TVM variables in screen order.</summary>
    public static readonly IReadOnlyList<TvmVariable> Variables =
        [TvmVariable.N, TvmVariable.InterestRate, TvmVariable.PresentValue, TvmVariable.Payment, TvmVariable.FutureValue];

    /// <summary>Key of the P/Y field in <see cref="TvmOutcome.FieldErrors"/>.</summary>
    public const string PaymentsPerYearKey = "P/Y";

    /// <summary>Key of the C/Y field in <see cref="TvmOutcome.FieldErrors"/>.</summary>
    public const string CompoundingsPerYearKey = "C/Y";

    /// <summary>The short name of a variable as on the calculator: N, I%, PV, PMT, FV.</summary>
    public static string Key(TvmVariable v) => v switch
    {
        TvmVariable.N => "N",
        TvmVariable.InterestRate => "I%",
        TvmVariable.PresentValue => "PV",
        TvmVariable.Payment => "PMT",
        TvmVariable.FutureValue => "FV",
        _ => throw new ArgumentOutOfRangeException(nameof(v)),
    };

    /// <summary>What a variable means, in plain words.</summary>
    public static string Meaning(TvmVariable v) => v switch
    {
        TvmVariable.N => "number of payments",
        TvmVariable.InterestRate => "interest per year",
        TvmVariable.PresentValue => "value now",
        TvmVariable.Payment => "each payment",
        TvmVariable.FutureValue => "value at the end",
        _ => throw new ArgumentOutOfRangeException(nameof(v)),
    };

    /// <summary>The input rule for a variable.</summary>
    public static FieldRule Rule(TvmVariable v) => v switch
    {
        TvmVariable.N => new FieldRule("N", Min: 0, MinIncluded: false, Max: 1_000_000),
        TvmVariable.InterestRate => FieldRule.Rate("I%"),
        _ => FieldRule.Amount(Key(v)),
    };

    /// <summary>Rule for P/Y.</summary>
    public static readonly FieldRule PaymentsPerYearRule = new("P/Y", Min: 0, MinIncluded: false, Max: 100_000);

    /// <summary>Rule for C/Y.</summary>
    public static readonly FieldRule CompoundingsPerYearRule = new("C/Y", Min: 0, MinIncluded: false, Max: 100_000);

    /// <summary>The value of one variable.</summary>
    public static double Get(TvmValues values, TvmVariable v) => v switch
    {
        TvmVariable.N => values.N,
        TvmVariable.InterestRate => values.InterestRate,
        TvmVariable.PresentValue => values.PresentValue,
        TvmVariable.Payment => values.Payment,
        TvmVariable.FutureValue => values.FutureValue,
        _ => throw new ArgumentOutOfRangeException(nameof(v)),
    };

    /// <summary>
    /// Which variable to solve: the requested one; otherwise the only blank field; otherwise the last solved one
    /// (<paramref name="lastSolved"/>) when nothing is blank. Returns null with a message when it is unclear.
    /// </summary>
    public static TvmVariable? ChooseTarget(TvmInputs inputs, TvmVariable? requested, TvmVariable? lastSolved, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        problem = null;
        if (requested is { } r) return r;
        var blanks = Variables.Where(v => string.IsNullOrWhiteSpace(inputs.Get(v))).ToList();
        if (blanks.Count == 1) return blanks[0];
        if (blanks.Count == 0)
        {
            if (lastSolved is { } last) return last;
            problem = "Every field is filled in. Clear the one you want to work out, or press SOLVE next to it.";
            return null;
        }
        problem = $"Leave only one field blank: {string.Join(", ", blanks.Take(blanks.Count - 1).Select(Key))} and {Key(blanks[^1])} are empty.";
        return null;
    }

    /// <summary>Reads every field and solves for the target variable (see <see cref="ChooseTarget"/>).</summary>
    public static TvmOutcome Solve(TvmInputs inputs, ValueReader reader, TvmVariable? requested = null, TvmVariable? lastSolved = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(reader);
        var errors = new Dictionary<string, string>();
        var target = ChooseTarget(inputs, requested, lastSolved, out var problem);

        var known = new Dictionary<TvmVariable, double>();
        foreach (var v in Variables)
        {
            if (v == target) continue;
            var f = reader.Read(inputs.Get(v), Rule(v));
            if (f.IsValid) known[v] = f.Value;
            else if (f.Error is { } e) errors[Key(v)] = e;
            else if (target is not null) errors[Key(v)] = $"{Key(v)} is empty: type a value, or press SOLVE next to it to work it out.";
        }
        var py = reader.Read(inputs.PaymentsPerYear, PaymentsPerYearRule);
        if (py.IsBlank) errors[PaymentsPerYearKey] = "P/Y is empty: type how many payments there are in a year, for example 12.";
        else if (!py.IsValid) errors[PaymentsPerYearKey] = py.Error!;
        var cy = reader.Read(inputs.CompoundingsPerYear, CompoundingsPerYearRule);
        if (!cy.IsValid && !cy.IsBlank) errors[CompoundingsPerYearKey] = cy.Error!;

        if (target is null) return new TvmOutcome(null, null, problem, errors);
        if (errors.Count > 0)
            return new TvmOutcome(target, null, errors.Count == 1 ? "Fix the field marked in red to see the answer." : "Fix the fields marked in red to see the answer.", errors);

        double cyValue = cy.IsBlank ? py.Value : cy.Value;
        var values = new TvmValues(
            known.GetValueOrDefault(TvmVariable.N),
            known.GetValueOrDefault(TvmVariable.InterestRate),
            known.GetValueOrDefault(TvmVariable.PresentValue),
            known.GetValueOrDefault(TvmVariable.Payment),
            known.GetValueOrDefault(TvmVariable.FutureValue),
            py.Value,
            cyValue == py.Value ? null : cyValue,
            inputs.Timing);
        try
        {
            var solved = Tvm.SolveFor(target.Value, values);
            double x = Get(solved, target.Value);
            if (!double.IsFinite(x))
                return new TvmOutcome(target, null, $"{Key(target.Value)} has no finite value for these numbers. Check the signs: money you pay is negative.", errors);
            return new TvmOutcome(target, new TvmSolution(target.Value, solved), null, errors);
        }
        catch (ArgumentException ex)
        {
            return new TvmOutcome(target, null, Explain(ex.Message), errors);
        }
    }

    private static string Explain(string message)
    {
        var m = ValueReader.Sentence(message);
        return m.Contains("sign", StringComparison.OrdinalIgnoreCase)
            ? m + " A loan is usually PV positive (money you get) and PMT negative (money you pay)."
            : m;
    }

    /// <summary>Label for the solved box: "PMT (PER MONTH)", "N (PAYMENTS)", "I% (PER YEAR)".</summary>
    public static string SolvedLabel(TvmSolution s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Solved switch
        {
            TvmVariable.N => "N (PAYMENTS)",
            TvmVariable.InterestRate => "I% (PER YEAR)",
            TvmVariable.Payment => $"PMT (PER {Periods.Name(s.Values.PaymentsPerYear).ToUpperInvariant()})",
            TvmVariable.PresentValue => "PV (VALUE NOW)",
            TvmVariable.FutureValue => "FV (VALUE AT THE END)",
            _ => Key(s.Solved),
        };
    }

    /// <summary>The solved value for display: money with the currency symbol, a count, or a percentage.</summary>
    public static string ValueText(TvmSolution s, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Solved switch
        {
            TvmVariable.N => Money.Number(s.Value, 4),
            TvmVariable.InterestRate => Money.Percent(s.Value, 4),
            _ => Money.Format(s.Value, culture),
        };
    }

    /// <summary>One sentence explaining the answer in plain words, including what its sign means.</summary>
    public static string Explain(TvmSolution s, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(s);
        var v = s.Values;
        string period = Periods.Name(v.PaymentsPerYear);
        string term = Periods.Duration(v.N, v.PaymentsPerYear);
        string when = v.Timing == PaymentTiming.Begin ? "at the start of" : "at the end of";
        switch (s.Solved)
        {
            case TvmVariable.Payment:
                if (Money.Cents(v.Payment) == 0) return "No regular payments are needed.";
                return v.Payment < 0
                    ? $"Negative: you pay {Money.Format(-v.Payment, culture)} {when} every {period} for {term}."
                    : $"Positive: you receive {Money.Format(v.Payment, culture)} {when} every {period} for {term}.";
            case TvmVariable.PresentValue:
                if (Money.Cents(v.PresentValue) == 0) return "Nothing changes hands at the start.";
                return v.PresentValue > 0
                    ? $"Positive: you receive {Money.Format(v.PresentValue, culture)} at the start, like a loan you take out."
                    : $"Negative: you pay {Money.Format(-v.PresentValue, culture)} at the start, like a deposit or a price.";
            case TvmVariable.FutureValue:
                if (Money.Cents(v.FutureValue) == 0) return $"Nothing is left after {term}.";
                return v.FutureValue > 0
                    ? $"Positive: you receive {Money.Format(v.FutureValue, culture)} after {term}."
                    : $"Negative: you still pay {Money.Format(-v.FutureValue, culture)} after {term}.";
            case TvmVariable.N:
                return $"{Periods.Count(v.N, "payment")}, one every {period}: {term}.";
            case TvmVariable.InterestRate:
            {
                double periodic = 100 * Tvm.PeriodicRate(v.InterestRate, v.PaymentsPerYear, v.CompoundingsPerYear);
                double effective = Statistics.Finance.EffectiveRate(v.InterestRate, v.EffectiveCompoundingsPerYear);
                return $"{Money.Percent(periodic, 4)} per {period}, effective {Money.Percent(effective, 4)} a year.";
            }
            default:
                return "";
        }
    }
}

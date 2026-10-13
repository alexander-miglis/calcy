using System.Globalization;
using System.Text;
using Calcy.Statistics;

namespace Calcy.Modes.Finance.Logic;

/// <summary>One cash flow row: an amount and how many periods in a row it repeats.</summary>
public sealed class CashFlowEntry
{
    /// <summary>The amount (an expression).</summary>
    public string Amount { get; set; } = "";

    /// <summary>How many periods in a row (blank means 1).</summary>
    public string Count { get; set; } = "1";
}

/// <summary>The text typed into the cash flow worksheet.</summary>
public sealed class CashFlowInputs
{
    /// <summary>CF0: the flow at the start (usually the investment, negative).</summary>
    public string Initial { get; set; } = "";

    /// <summary>CF1, CF2, ... with their frequencies.</summary>
    public List<CashFlowEntry> Flows { get; set; } = [];

    /// <summary>Discount rate per period in percent, for NPV and the discounted payback.</summary>
    public string Rate { get; set; } = "";

    /// <summary>Finance rate in percent for MIRR (blank means the discount rate).</summary>
    public string FinanceRate { get; set; } = "";

    /// <summary>Reinvestment rate in percent for MIRR (blank means the discount rate).</summary>
    public string ReinvestRate { get; set; } = "";

    /// <summary>The first-run values from the design.</summary>
    public static CashFlowInputs Default() => new()
    {
        Initial = "-1000",
        Flows = [new() { Amount = "300" }, new() { Amount = "400" }, new() { Amount = "500" }, new() { Amount = "200" }],
        Rate = "8",
    };

    /// <summary>A deep copy.</summary>
    public CashFlowInputs Clone() => new()
    {
        Initial = Initial,
        Flows = [.. Flows.Select(f => new CashFlowEntry { Amount = f.Amount, Count = f.Count })],
        Rate = Rate,
        FinanceRate = FinanceRate,
        ReinvestRate = ReinvestRate,
    };
}

/// <summary>NPV, IRR, MIRR and payback for a set of cash flows.</summary>
public sealed record CashFlowResult(
    double Initial,
    IReadOnlyList<double> Amounts,
    IReadOnlyList<int> Counts,
    double Rate,
    double FinanceRate,
    double ReinvestRate,
    double Npv,
    double? Irr,
    IReadOnlyList<double> AllIrr,
    double? Mirr,
    double? Payback,
    double? DiscountedPayback,
    string? IrrNote,
    string? MirrNote)
{
    /// <summary>Every flow in time order, with the repeats written out (CF0 first).</summary>
    public IReadOnlyList<double> Expanded
    {
        get
        {
            var list = new List<double> { Initial };
            for (int j = 0; j < Amounts.Count; j++)
                for (int k = 0; k < Counts[j]; k++) list.Add(Amounts[j]);
            return list;
        }
    }

    /// <summary>True when every frequency is 1.</summary>
    public bool AllSingle => Counts.All(c => c == 1);
}

/// <summary>The result of reading the cash flow worksheet.</summary>
/// <param name="Result">The answers, when every field is valid.</param>
/// <param name="Problem">Why there are no answers, in plain words.</param>
/// <param name="FieldErrors">Messages keyed by <see cref="CashFlowWorksheet.InitialKey"/>, "cf1", "n1", ..., or the rate keys.</param>
public sealed record CashFlowOutcome(CashFlowResult? Result, string? Problem, IReadOnlyDictionary<string, string> FieldErrors);

/// <summary>Reads the cash flow worksheet and works out NPV, IRR, MIRR and payback with <see cref="CashFlow"/>.</summary>
public static class CashFlowWorksheet
{
    /// <summary>Key of the CF0 field.</summary>
    public const string InitialKey = "cf0";

    /// <summary>Key of the rate field.</summary>
    public const string RateKey = "rate";

    /// <summary>Key of the finance rate field.</summary>
    public const string FinanceKey = "finance";

    /// <summary>Key of the reinvestment rate field.</summary>
    public const string ReinvestKey = "reinvest";

    /// <summary>The most flows the worksheet takes.</summary>
    public const int MaxFlows = 60;

    /// <summary>Key of the amount field of flow j (1 based).</summary>
    public static string AmountKey(int j) => "cf" + j.ToString(CultureInfo.InvariantCulture);

    /// <summary>Key of the frequency field of flow j (1 based).</summary>
    public static string CountKey(int j) => "n" + j.ToString(CultureInfo.InvariantCulture);

    /// <summary>Reads every field and computes the results.</summary>
    public static CashFlowOutcome Evaluate(CashFlowInputs inputs, ValueReader reader)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(reader);
        var errors = new Dictionary<string, string>();

        var cf0 = reader.Read(inputs.Initial, FieldRule.Amount("CF0"));
        if (cf0.IsBlank) errors[InitialKey] = "CF0 is empty: type the starting amount (an investment is negative).";
        else if (!cf0.IsValid) errors[InitialKey] = cf0.Error!;

        // Trailing rows with nothing typed are ignored, so an empty row can be added before filling it in.
        int last = inputs.Flows.Count - 1;
        while (last >= 0 && string.IsNullOrWhiteSpace(inputs.Flows[last].Amount)) last--;
        var amounts = new List<double>();
        var counts = new List<int>();
        for (int j = 0; j <= last; j++)
        {
            var e = inputs.Flows[j];
            string name = "CF" + (j + 1).ToString(CultureInfo.InvariantCulture);
            var a = reader.Read(e.Amount, FieldRule.Amount(name));
            if (a.IsBlank) errors[AmountKey(j + 1)] = $"{name} is empty: type an amount (0 for nothing) or remove the row.";
            else if (!a.IsValid) errors[AmountKey(j + 1)] = a.Error!;
            var n = reader.Read(e.Count, FieldRule.Whole("the number of times " + name + " repeats", 1, 10_000));
            if (!n.IsValid && !n.IsBlank) errors[CountKey(j + 1)] = n.Error!;
            amounts.Add(a.IsValid ? a.Value : 0);
            counts.Add(n.IsValid ? (int)n.Value : 1);
        }

        var rate = reader.Read(inputs.Rate, FieldRule.Rate("the rate"));
        if (rate.IsBlank) errors[RateKey] = "The rate is empty: type the rate per period in percent, for example 8.";
        else if (!rate.IsValid) errors[RateKey] = rate.Error!;
        var fin = reader.Read(inputs.FinanceRate, FieldRule.Rate("the finance rate"));
        if (!fin.IsValid && !fin.IsBlank) errors[FinanceKey] = fin.Error!;
        var re = reader.Read(inputs.ReinvestRate, FieldRule.Rate("the reinvestment rate"));
        if (!re.IsValid && !re.IsBlank) errors[ReinvestKey] = re.Error!;

        if (errors.Count > 0)
            return new CashFlowOutcome(null, errors.Count == 1 ? "Fix the field marked in red to see the answers." : "Fix the fields marked in red to see the answers.", errors);
        if (amounts.Count == 0)
            return new CashFlowOutcome(null, "Add at least one cash flow after CF0.", errors);
        if (counts.Sum() > 100_000)
            return new CashFlowOutcome(null, "There are too many periods: keep the total of the frequencies under 100,000.", errors);

        double r = rate.Value;
        double fr = fin.IsBlank ? r : fin.Value;
        double rr = re.IsBlank ? r : re.Value;
        var freq = counts.Select(c => (double)c).ToArray();
        var flows = amounts.ToArray();
        try
        {
            double npv = CashFlow.Npv(r, cf0.Value, flows, freq);
            IReadOnlyList<double> all;
            string? irrNote = null;
            double? irr = null;
            try
            {
                all = CashFlow.IrrAll(cf0.Value, flows, freq);
            }
            catch (ArgumentException ex)
            {
                all = [];
                irrNote = ValueReader.Sentence(ex.Message);
            }
            if (all.Count == 0)
                irrNote ??= "No IRR: the flows need at least one change of sign (money out, then money in).";
            else
            {
                irr = CashFlow.Irr(cf0.Value, flows, freq);
                if (all.Count > 1)
                    irrNote = "Several rates give NPV 0: " + string.Join(", ", all.Select(x => Money.Percent(x, 2))) + ". Use MIRR instead.";
            }
            double? mirr = null;
            string? mirrNote = null;
            try
            {
                mirr = CashFlow.Mirr(fr, rr, cf0.Value, flows, freq);
            }
            catch (ArgumentException ex)
            {
                mirrNote = ValueReader.Sentence(ex.Message);
            }
            double? payback = CashFlow.PaybackPeriod(cf0.Value, flows, freq);
            double? discounted = CashFlow.DiscountedPaybackPeriod(r, cf0.Value, flows, freq);
            var result = new CashFlowResult(cf0.Value, amounts, counts, r, fr, rr, npv, irr, all, mirr, payback, discounted, irrNote, mirrNote);
            return new CashFlowOutcome(result, null, errors);
        }
        catch (ArgumentException ex)
        {
            return new CashFlowOutcome(null, ValueReader.Sentence(ex.Message), errors);
        }
    }

    /// <summary>The flows as CSV: period, flow, discounted flow and the running totals.</summary>
    public static string ToCsv(CashFlowResult r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var sb = new StringBuilder("Period,Cash flow,Discounted,Cumulative,Cumulative discounted\n");
        double cum = 0, cumD = 0, factor = 1 + r.Rate / 100;
        var flows = r.Expanded;
        for (int t = 0; t < flows.Count; t++)
        {
            double d = flows[t] / Math.Pow(factor, t);
            cum += flows[t];
            cumD += d;
            sb.Append(t.ToString(CultureInfo.InvariantCulture)).Append(',').Append(Money.Csv(flows[t])).Append(',')
              .Append(Money.Csv(d)).Append(',').Append(Money.Csv(cum)).Append(',').Append(Money.Csv(cumD)).Append('\n');
        }
        sb.Append("NPV,").Append(Money.Csv(r.Npv)).Append('\n');
        return sb.ToString();
    }
}

using System.Globalization;
using System.Text;
using Calcy.Statistics;

namespace Calcy.Modes.Finance.Logic;

/// <summary>Depreciation methods.</summary>
public enum DepreciationMethod
{
    /// <summary>Straight line (SL): the same amount every year.</summary>
    StraightLine,

    /// <summary>Sum of the years' digits (SYD): more in the early years.</summary>
    SumOfYearsDigits,

    /// <summary>Declining balance (DB): a fixed share of the book value each year.</summary>
    DecliningBalance,
}

/// <summary>The text typed into the Depreciation tab.</summary>
public sealed class DepreciationInputs
{
    /// <summary>The method.</summary>
    public DepreciationMethod Method { get; set; } = DepreciationMethod.StraightLine;

    /// <summary>What the asset cost.</summary>
    public string Cost { get; set; } = "10000";

    /// <summary>What it is worth at the end of its life.</summary>
    public string Salvage { get; set; } = "1000";

    /// <summary>Useful life in years.</summary>
    public string Life { get; set; } = "5";

    /// <summary>Declining balance rate in percent of the straight line rate (200 is double declining).</summary>
    public string Factor { get; set; } = "200";

    /// <summary>Switch to straight line when that gives more (declining balance only).</summary>
    public bool SwitchToStraightLine { get; set; } = true;
}

/// <summary>The numbers a depreciation schedule is built from (stored on cards to rebuild the CSV).</summary>
public sealed record DepreciationPlan(DepreciationMethod Method, double Cost, double Salvage, int Life, double FactorPercent, bool SwitchToStraightLine);

/// <summary>A depreciation schedule: one row per year with the depreciation, the total so far and the book value.</summary>
public sealed class DepreciationReport
{
    private DepreciationReport(DepreciationPlan plan, IReadOnlyList<DepreciationRow> rows)
    {
        Plan = plan;
        Rows = rows;
        Total = rows.Sum(r => r.Depreciation);
    }

    /// <summary>The inputs.</summary>
    public DepreciationPlan Plan { get; }

    /// <summary>One row per year.</summary>
    public IReadOnlyList<DepreciationRow> Rows { get; }

    /// <summary>Total depreciation over the life.</summary>
    public double Total { get; }

    /// <summary>Book value at the end.</summary>
    public double FinalBookValue => Rows.Count == 0 ? Plan.Cost : Rows[^1].BookValue;

    /// <summary>Short name of a method: SL, SYD, DB.</summary>
    public static string Short(DepreciationMethod m) => m switch
    {
        DepreciationMethod.StraightLine => "SL",
        DepreciationMethod.SumOfYearsDigits => "SYD",
        _ => "DB",
    };

    /// <summary>Full name of a method.</summary>
    public static string Name(DepreciationMethod m) => m switch
    {
        DepreciationMethod.StraightLine => "Straight line",
        DepreciationMethod.SumOfYearsDigits => "Sum of the years' digits",
        _ => "Declining balance",
    };

    /// <summary>Builds the schedule from a plan.</summary>
    public static DepreciationReport Build(DepreciationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var rows = plan.Method switch
        {
            DepreciationMethod.StraightLine => Depreciation.StraightLine(plan.Cost, plan.Salvage, plan.Life),
            DepreciationMethod.SumOfYearsDigits => Depreciation.SumOfYearsDigits(plan.Cost, plan.Salvage, plan.Life),
            _ => Depreciation.DecliningBalance(plan.Cost, plan.Salvage, plan.Life, plan.FactorPercent / 100, plan.SwitchToStraightLine),
        };
        return new DepreciationReport(plan, rows);
    }

    /// <summary>Reads the fields and builds the schedule.</summary>
    public static Answer<DepreciationReport> Evaluate(DepreciationInputs inputs, ValueReader reader)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(reader);
        var errors = new Dictionary<string, string>();
        var cost = InterestTools.Required(reader, inputs.Cost, FieldRule.PositiveAmount("the cost"), "cost", errors, "type what the asset cost");
        var salvage = InterestTools.Required(reader, inputs.Salvage, new FieldRule("the salvage value", Min: 0), "salvage", errors, "type what it is worth at the end (0 if nothing)");
        var life = InterestTools.Required(reader, inputs.Life, FieldRule.Whole("the life", 1, 1000), "life", errors, "type the useful life in years");
        double factor = 200;
        if (inputs.Method == DepreciationMethod.DecliningBalance)
            factor = InterestTools.Required(reader, inputs.Factor, new FieldRule("the rate", IsPercent: true, Min: 0, MinIncluded: false, Max: 100_000), "factor", errors, "type the declining balance rate, for example 200 for double declining");
        if (errors.Count == 0 && salvage > cost) errors["salvage"] = "The salvage value must not be more than the cost.";
        if (errors.Count > 0) return new(null, InterestTools.Fix(errors), errors);
        try
        {
            var plan = new DepreciationPlan(inputs.Method, cost, salvage, (int)life, factor, inputs.SwitchToStraightLine);
            return new(Build(plan), null, errors);
        }
        catch (ArgumentException ex)
        {
            return new(null, ValueReader.Sentence(ex.Message), errors);
        }
    }

    /// <summary>The schedule as CSV with a totals row.</summary>
    public string ToCsv()
    {
        var sb = new StringBuilder("Year,Depreciation,Accumulated,Book value\n");
        foreach (var r in Rows)
            sb.Append(r.Year.ToString(CultureInfo.InvariantCulture)).Append(',').Append(Money.Csv(r.Depreciation)).Append(',')
              .Append(Money.Csv(r.AccumulatedDepreciation)).Append(',').Append(Money.Csv(r.BookValue)).Append('\n');
        sb.Append("Total,").Append(Money.Csv(Total)).Append(",,").Append(Money.Csv(FinalBookValue)).Append('\n');
        return sb.ToString();
    }
}

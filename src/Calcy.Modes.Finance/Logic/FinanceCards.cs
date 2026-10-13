using System.Globalization;
using Calcy.Statistics;

namespace Calcy.Modes.Finance.Logic;

/// <summary>Makes result cards from answers, and rebuilds the CSV of schedule cards.</summary>
public static class FinanceCards
{
    private static FinanceCard Card(string title, string input, string result, string? note, params CardUse[] uses) => new()
    {
        Title = title,
        Input = input,
        Result = result,
        Note = string.IsNullOrWhiteSpace(note) ? null : note,
        Uses = [.. uses.Where(u => u.Expression.Length > 0)],
        Created = DateTime.Now,
    };

    private static string Num(double v, int decimals = 4) => Money.Number(v, decimals);

    /// <summary>"N = 360, I% = 8, PV = 100,000.00, FV = 0.00", leaving out the solved variable, plus P/Y, C/Y and BEGIN when unusual.</summary>
    public static string TvmInputsText(TvmValues v, TvmVariable? leaveOut)
    {
        ArgumentNullException.ThrowIfNull(v);
        var parts = new List<string>();
        foreach (var x in TvmWorksheet.Variables)
        {
            if (x == leaveOut) continue;
            double value = TvmWorksheet.Get(v, x);
            string text = x switch
            {
                TvmVariable.N => Num(value),
                TvmVariable.InterestRate => Num(value, 6),
                _ => Money.Plain(value),
            };
            parts.Add($"{TvmWorksheet.Key(x)} = {text}");
        }
        if (v.PaymentsPerYear != 12 || v.EffectiveCompoundingsPerYear != v.PaymentsPerYear)
            parts.Add($"P/Y = {Num(v.PaymentsPerYear)}, C/Y = {Num(v.EffectiveCompoundingsPerYear)}");
        if (v.Timing == PaymentTiming.Begin) parts.Add("BEGIN");
        return string.Join(", ", parts);
    }

    /// <summary>A card for a solved TVM problem.</summary>
    public static FinanceCard Tvm(TvmSolution s, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(s);
        string key = TvmWorksheet.Key(s.Solved);
        string value = s.Solved switch
        {
            TvmVariable.N => Num(s.Value),
            TvmVariable.InterestRate => Money.Number(s.Value, 4),
            _ => Money.Format(s.Value, culture),
        };
        return Card("TVM: " + key, TvmInputsText(s.Values, s.Solved), $"{key} = {value}", TvmWorksheet.Explain(s, culture), new CardUse(key, CalcyText.Tvm(s)));
    }

    /// <summary>A card for an amortization schedule with the TI ΣInt, ΣPrn and bal over a range of payments.</summary>
    public static FinanceCard Amortization(AmortizationReport report, AmortizationRange range, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(range);
        var v = report.Values;
        string result =
            $"Payments {range.From} to {range.To}:\n" +
            $"ΣInt = {Money.Format(range.Interest, culture)}\n" +
            $"ΣPrn = {Money.Format(range.Principal, culture)}\n" +
            $"Balance = {Money.Format(range.Balance, culture)}";
        string note = $"Whole schedule: {Periods.Count(report.Rows.Count, "payment")}, interest {Money.Format(report.TotalInterest, culture)}.";
        var card = Card("AMORTIZATION", TvmInputsText(v, null), result, note,
            new CardUse("ΣInt", CalcyText.SumInterest(v, range.From, range.To)),
            new CardUse("ΣPrn", CalcyText.SumPrincipal(v, range.From, range.To)),
            new CardUse("Balance", CalcyText.Balance(v, range.To)));
        card.Schedule = new ScheduleSource
        {
            Kind = ScheduleKind.Amortization,
            Numbers = [v.N, v.InterestRate, v.PresentValue, v.Payment, v.FutureValue, v.PaymentsPerYear, v.EffectiveCompoundingsPerYear, v.Timing == PaymentTiming.Begin ? 1 : 0],
        };
        return card;
    }

    /// <summary>"CF: -1000, 300, 400 ×3; rate 8 %".</summary>
    public static string CashFlowInputsText(CashFlowResult r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var flows = new List<string> { Num(r.Initial, 2) };
        for (int j = 0; j < r.Amounts.Count; j++)
            flows.Add(Num(r.Amounts[j], 2) + (r.Counts[j] > 1 ? " ×" + r.Counts[j].ToString(CultureInfo.InvariantCulture) : ""));
        return "CF: " + string.Join(", ", flows) + "\nRate " + Money.Percent(r.Rate, 2);
    }

    /// <summary>A card for the cash flow results.</summary>
    public static FinanceCard CashFlow(CashFlowResult r, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(r);
        var lines = new List<string>
        {
            "NPV = " + Money.Format(r.Npv, culture),
            "IRR = " + (r.Irr is { } irr ? Money.Percent(irr, 2) : "none"),
            "MIRR = " + (r.Mirr is { } mirr ? Money.Percent(mirr, 2) : "none"),
            "Payback = " + (r.Payback is { } pb ? Periods.Count(Math.Round(pb, 2), "period") : "never"),
        };
        var notes = new List<string>();
        if (r.DiscountedPayback is { } dp) notes.Add($"Discounted payback: {Periods.Count(Math.Round(dp, 2), "period")}.");
        if (r.IrrNote is { } n1) notes.Add(n1);
        var card = Card("CASH FLOWS", CashFlowInputsText(r), string.Join("\n", lines), string.Join(" ", notes),
            new CardUse("NPV", CalcyText.Npv(r)),
            new CardUse("IRR", CalcyText.Irr(r)),
            new CardUse("MIRR", CalcyText.Mirr(r)));
        var flows = new List<double>();
        for (int j = 0; j < r.Amounts.Count; j++)
        {
            flows.Add(r.Amounts[j]);
            flows.Add(r.Counts[j]);
        }
        card.Schedule = new ScheduleSource { Kind = ScheduleKind.CashFlow, Numbers = [r.Initial, r.Rate, r.FinanceRate, r.ReinvestRate], Flows = flows };
        return card;
    }

    /// <summary>A card for a rate conversion.</summary>
    public static FinanceCard Rate(RateConversion c)
    {
        ArgumentNullException.ThrowIfNull(c);
        bool toEffective = c.Direction == RateDirection.NominalToEffective;
        string input = $"{Money.Percent(c.Input, 4)} {(toEffective ? "nominal" : "effective")}, compounded {InterestTools.HowOften(c.Compoundings)}";
        string result = $"{Money.Percent(c.Output, 4)} {(toEffective ? "effective" : "nominal")}";
        return Card(toEffective ? "EFFECTIVE RATE" : "NOMINAL RATE", input, result, null, new CardUse(toEffective ? "Effective" : "Nominal", CalcyText.Rate(c)));
    }

    /// <summary>A card for simple interest.</summary>
    public static FinanceCard SimpleInterest(SimpleInterestAnswer a, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(a);
        string expr = CalcyText.SimpleInterest(a);
        return Card("SIMPLE INTEREST",
            $"{Money.Plain(a.Principal)} at {Money.Percent(a.Rate, 4)} for {Periods.Count(a.Days, "day")} ({a.DayBasis} day year)",
            $"Interest = {Money.Format(a.Interest, culture)}\nTotal = {Money.Format(a.Total, culture)}", null,
            new CardUse("Interest", expr),
            new CardUse("Total", $"{CalcyText.Number(a.Principal)} + {expr}"));
    }

    /// <summary>A card for compound interest.</summary>
    public static FinanceCard CompoundInterest(CompoundInterestAnswer a, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(a);
        string expr = CalcyText.CompoundValue(a);
        return Card("COMPOUND INTEREST",
            $"{Money.Plain(a.Principal)} at {Money.Percent(a.Rate, 4)}, compounded {InterestTools.HowOften(a.Compoundings)}, for {Periods.Count(a.Years, "year")}",
            $"Value = {Money.Format(a.FutureValue, culture)}\nInterest = {Money.Format(a.Interest, culture)}", null,
            new CardUse("Value", expr),
            new CardUse("Interest", $"{expr} - {(a.Principal < 0 ? "(" + CalcyText.Number(a.Principal) + ")" : CalcyText.Number(a.Principal))}"));
    }

    /// <summary>A card for a day count.</summary>
    public static FinanceCard DaysBetween(DayCount d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return Card("DAYS BETWEEN",
            $"{DateCalculator.Show(d.Start)} to {DateCalculator.Show(d.End)}",
            $"Actual: {Periods.Count(d.Actual, "day")}\n30/360 US: {Periods.Count(d.Thirty360US, "day")}\n30/360 EU: {Periods.Count(d.Thirty360European, "day")}",
            null,
            new CardUse("Actual", CalcyText.DaysBetween(d, DayCountConvention.Actual)),
            new CardUse("30/360 US", CalcyText.DaysBetween(d, DayCountConvention.Thirty360US)),
            new CardUse("30/360 EU", CalcyText.DaysBetween(d, DayCountConvention.Thirty360European)));
    }

    /// <summary>A card for a date plus days.</summary>
    public static FinanceCard AddDays(DateShift s)
    {
        ArgumentNullException.ThrowIfNull(s);
        string op = s.Days < 0 ? "-" : "+";
        return Card("ADD DAYS", $"{DateCalculator.Show(s.From)} {op} {Periods.Count(Math.Abs(s.Days), "day")}", DateCalculator.Show(s.Result), null);
    }

    /// <summary>A card for a depreciation schedule.</summary>
    public static FinanceCard Depreciation(DepreciationReport r, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(r);
        var p = r.Plan;
        string input = $"Cost {Money.Plain(p.Cost)}, salvage {Money.Plain(p.Salvage)}, life {Periods.Count(p.Life, "year")}";
        if (p.Method == DepreciationMethod.DecliningBalance)
            input += $", rate {Num(p.FactorPercent, 2)} %" + (p.SwitchToStraightLine ? ", switch to SL" : "");
        string first = r.Rows.Count > 0 ? Money.Format(r.Rows[0].Depreciation, culture) : Money.Format(0, culture);
        string result = $"Year 1: {first}\nTotal: {Money.Format(r.Total, culture)}\nBook value at the end: {Money.Format(r.FinalBookValue, culture)}";
        var card = Card("DEPRECIATION: " + DepreciationReport.Short(p.Method), input, result, DepreciationReport.Name(p.Method) + ".",
            new CardUse("Each year", CalcyText.Values(r.Rows.Select(x => x.Depreciation))),
            new CardUse("Book values", CalcyText.Values(r.Rows.Select(x => x.BookValue))));
        card.Schedule = new ScheduleSource
        {
            Kind = ScheduleKind.Depreciation,
            Numbers = [(double)p.Method, p.Cost, p.Salvage, p.Life, p.FactorPercent, p.SwitchToStraightLine ? 1 : 0],
        };
        return card;
    }

    /// <summary>A card for a bond price or yield.</summary>
    public static FinanceCard Bond(BondAnswer b)
    {
        ArgumentNullException.ThrowIfNull(b);
        string freq = b.Frequency switch { 1 => "1 coupon a year", _ => $"{b.Frequency} coupons a year" };
        string input = $"Coupon {Money.Percent(b.Coupon, 4)}, {(b.Solved == BondTarget.Price ? "yield " + Money.Percent(b.Yield, 4) : "price " + BondWorksheet.PriceText(b.CleanPrice))}\n" +
                       $"{b.Settlement.ToString(DateCalculator.Format, CultureInfo.InvariantCulture)} to {b.Maturity.ToString(DateCalculator.Format, CultureInfo.InvariantCulture)}, {freq}, {BondWorksheet.DayCountName(b.DayCount)}";
        string result = b.Solved == BondTarget.Price
            ? $"Price = {BondWorksheet.PriceText(b.CleanPrice)}\nAccrued = {BondWorksheet.PriceText(b.Accrued)}\nWith accrued = {BondWorksheet.PriceText(b.DirtyPrice)}"
            : $"Yield = {Money.Percent(b.Yield, 4)}\nAccrued = {BondWorksheet.PriceText(b.Accrued)}\nWith accrued = {BondWorksheet.PriceText(b.DirtyPrice)}";
        return Card(b.Solved == BondTarget.Price ? "BOND PRICE" : "BOND YIELD", input, result, "Per 100 of face value.",
            b.Solved == BondTarget.Price
                ? new CardUse("Price", CalcyText.Number(b.CleanPrice))
                : new CardUse("Yield", CalcyText.Number(b.Yield)),
            new CardUse("With accrued", CalcyText.Number(b.DirtyPrice)));
    }

    /// <summary>Rebuilds the CSV of a schedule card.</summary>
    public static string? Csv(ScheduleSource s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var n = s.Numbers;
        try
        {
            switch (s.Kind)
            {
                case ScheduleKind.Amortization when n.Count >= 8:
                {
                    var v = new TvmValues(n[0], n[1], n[2], n[3], n[4], n[5], n[6] == n[5] ? null : n[6], n[7] == 1 ? PaymentTiming.Begin : PaymentTiming.End);
                    return AmortizationReport.TryBuild(v, out _)?.ToCsv();
                }
                case ScheduleKind.Depreciation when n.Count >= 6:
                    return DepreciationReport.Build(new DepreciationPlan((DepreciationMethod)(int)n[0], n[1], n[2], (int)n[3], n[4], n[5] == 1)).ToCsv();
                case ScheduleKind.CashFlow when n.Count >= 4 && s.Flows.Count % 2 == 0:
                {
                    var amounts = new List<double>();
                    var counts = new List<int>();
                    for (int k = 0; k < s.Flows.Count; k += 2)
                    {
                        amounts.Add(s.Flows[k]);
                        counts.Add((int)s.Flows[k + 1]);
                    }
                    var r = new CashFlowResult(n[0], amounts, counts, n[1], n[2], n[3], 0, null, [], null, null, null, null, null);
                    double npv = Calcy.Statistics.CashFlow.Npv(n[1], n[0], amounts, [.. counts.Select(c => (double)c)]);
                    return CashFlowWorksheet.ToCsv(r with { Npv = npv });
                }
            }
        }
        catch (ArgumentException)
        {
            // A card saved by an older version with numbers that no longer build a schedule: nothing to export.
        }
        return null;
    }
}

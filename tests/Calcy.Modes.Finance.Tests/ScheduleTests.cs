using Calcy.Modes.Finance.Logic;
using Calcy.Statistics;
using static Calcy.Modes.Finance.Tests.TestSupport;

namespace Calcy.Modes.Finance.Tests;

/// <summary>Amortization and depreciation schedules: totals, yearly grouping, ranges and CSV.</summary>
public class ScheduleTests
{
    private static readonly TvmValues Mortgage = new(360, 8, 100000, Tvm.SolvePayment(360, 8, 100000, 0), 0);

    [Fact]
    public void Amortization_totals_equal_the_payments()
    {
        var r = AmortizationReport.TryBuild(Mortgage, out var problem)!;
        Assert.Null(problem);
        Assert.Equal(360, r.Rows.Count);
        Near(Mortgage.Payment * 360, r.TotalPayment, 1e-12);
        Near(r.TotalPayment, r.TotalInterest + r.TotalPrincipal, 1e-12);
        Near(-100000, r.TotalPrincipal, 1e-9);
        Assert.True(Math.Abs(r.FinalBalance) < 1e-6);
        foreach (var row in r.Rows) Near(row.Payment, row.Interest + row.Principal, 1e-12);
    }

    [Fact]
    public void First_payment_matches_the_hand_calculation()
    {
        var first = AmortizationReport.TryBuild(Mortgage, out _)!.Rows[0];
        Assert.Equal(-733.76, Money.Cents(first.Payment));
        Assert.Equal(-666.67, Money.Cents(first.Interest));
        Assert.Equal(-67.10, Money.Cents(first.Principal));
        Assert.Equal(99932.90, Money.Cents(first.Balance));
    }

    [Fact]
    public void Years_group_the_payments()
    {
        var r = AmortizationReport.TryBuild(Mortgage, out _)!;
        Assert.Equal(12, r.PaymentsPerYear);
        Assert.Equal(30, r.Years.Count);
        Assert.Equal((1, 1, 12), (r.Years[0].Year, r.Years[0].FirstPeriod, r.Years[0].LastPeriod));
        Assert.Equal((30, 349, 360), (r.Years[^1].Year, r.Years[^1].FirstPeriod, r.Years[^1].LastPeriod));
        Near(r.TotalInterest, r.Years.Sum(y => y.Interest), 1e-12);
        Near(r.TotalPrincipal, r.Years.Sum(y => y.Principal), 1e-12);
        Near(12 * Mortgage.Payment, r.Years[0].Payment, 1e-12);
        Assert.Equal(r.Rows[11].Balance, r.Years[0].EndBalance);
        // Interest falls and principal grows year by year on an ordinary loan.
        Assert.True(Math.Abs(r.Years[0].Interest) > Math.Abs(r.Years[^1].Interest));
        Assert.True(Math.Abs(r.Years[0].Principal) < Math.Abs(r.Years[^1].Principal));
    }

    [Fact]
    public void Range_gives_the_TI_sum_of_interest_principal_and_balance()
    {
        var r = AmortizationReport.TryBuild(Mortgage, out _)!;
        var year1 = r.Range(1, 12);
        Near(r.Years[0].Interest, year1.Interest, 1e-9);
        Near(r.Years[0].Principal, year1.Principal, 1e-9);
        Near(r.Years[0].EndBalance, year1.Balance, 1e-9);
        Assert.Equal(-7969.81, Money.Cents(year1.Interest));
        Assert.Throws<ArgumentOutOfRangeException>(() => r.Range(5, 4));
    }

    [Fact]
    public void Begin_mode_has_no_interest_in_the_first_payment()
    {
        var v = new TvmValues(12, 12, 1000, Tvm.SolvePayment(12, 12, 1000, 0, timing: PaymentTiming.Begin), 0, Timing: PaymentTiming.Begin);
        var r = AmortizationReport.TryBuild(v, out _)!;
        Assert.Equal(0, r.Rows[0].Interest);
        Assert.True(Math.Abs(r.FinalBalance) < 1e-9);
        Near(r.TotalPayment, r.TotalInterest + r.TotalPrincipal, 1e-12);
    }

    [Fact]
    public void Schedule_limits_have_messages()
    {
        Assert.Null(AmortizationReport.TryBuild(Mortgage with { N = 0.5 }, out var p1));
        Assert.Equal("A schedule needs at least one payment: N must be 1 or more.", p1);
        Assert.Null(AmortizationReport.TryBuild(Mortgage with { N = 20000 }, out var p2));
        Assert.Equal("The schedule is limited to 12,000 payments; N is 20,000.", p2);
        // A fractional N rounds up to a last (full) payment, as on the TI.
        Assert.Equal(48, AmortizationReport.TryBuild(Mortgage with { N = 47.3 }, out _)!.Rows.Count);
    }

    [Fact]
    public void Amortization_csv_has_a_header_every_payment_and_totals()
    {
        var csv = AmortizationReport.TryBuild(Mortgage, out _)!.ToCsv();
        var lines = csv.TrimEnd('\n').Split('\n');
        Assert.Equal(362, lines.Length);
        Assert.Equal("Payment,Year,Amount,Interest,Principal,Balance", lines[0]);
        Assert.Equal("1,1,-733.76,-666.67,-67.10,99932.90", lines[1]);
        Assert.StartsWith("360,30,-733.76,", lines[360]);
        Assert.StartsWith("Total,,-264155.25,-164155.25,-100000.00,", lines[361]);
    }

    [Fact]
    public void Depreciation_methods_match_the_textbook()
    {
        var reader = Reader();
        var sl = DepreciationReport.Evaluate(new DepreciationInputs { Method = DepreciationMethod.StraightLine, Cost = "10000", Salvage = "1000", Life = "5" }, reader).Value!;
        Assert.All(sl.Rows, r => Assert.Equal(1800, r.Depreciation, 9));
        Assert.Equal(9000, sl.Total, 9);
        Assert.Equal(1000, sl.FinalBookValue, 9);

        var syd = DepreciationReport.Evaluate(new DepreciationInputs { Method = DepreciationMethod.SumOfYearsDigits, Cost = "10000", Salvage = "1000", Life = "5" }, reader).Value!;
        Assert.Equal([3000, 2400, 1800, 1200, 600], syd.Rows.Select(r => Math.Round(r.Depreciation, 6)));

        var ddb = DepreciationReport.Evaluate(new DepreciationInputs { Method = DepreciationMethod.DecliningBalance, Cost = "10000", Salvage = "1000", Life = "5", Factor = "200", SwitchToStraightLine = true }, reader).Value!;
        Assert.Equal([4000, 2400, 1440, 864, 296], ddb.Rows.Select(r => Math.Round(r.Depreciation, 6)));
        Assert.Equal(1000, ddb.FinalBookValue, 9);

        var csv = ddb.ToCsv().TrimEnd('\n').Split('\n');
        Assert.Equal("Year,Depreciation,Accumulated,Book value", csv[0]);
        Assert.Equal("1,4000.00,4000.00,6000.00", csv[1]);
        Assert.Equal("Total,9000.00,,1000.00", csv[^1]);
    }

    [Fact]
    public void Depreciation_validation()
    {
        var a = DepreciationReport.Evaluate(new DepreciationInputs { Cost = "1000", Salvage = "2000", Life = "2.5" }, Reader());
        Assert.Null(a.Value);
        Assert.Equal("The life must be a whole number.", a.FieldErrors["life"]);
        var b = DepreciationReport.Evaluate(new DepreciationInputs { Cost = "1000", Salvage = "2000", Life = "3" }, Reader());
        Assert.Equal("The salvage value must not be more than the cost.", b.FieldErrors["salvage"]);
        var c = DepreciationReport.Evaluate(new DepreciationInputs { Cost = "", Salvage = "0", Life = "3" }, Reader());
        Assert.Equal("The cost is empty: type what the asset cost.", c.FieldErrors["cost"]);
    }
}

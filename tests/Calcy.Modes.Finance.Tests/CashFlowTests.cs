using Calcy.Modes.Finance.Logic;
using static Calcy.Modes.Finance.Tests.TestSupport;

namespace Calcy.Modes.Finance.Tests;

/// <summary>The cash flow worksheet: NPV, IRR, MIRR, payback and validation.</summary>
public class CashFlowTests
{
    private static CashFlowInputs Flows(string cf0, string rate, params string[] flows) => new()
    {
        Initial = cf0,
        Rate = rate,
        Flows = [.. flows.Select(f => f.Split('x') is [var a, var n] ? new CashFlowEntry { Amount = a, Count = n } : new CashFlowEntry { Amount = f })],
    };

    /// <summary>NPV written out by hand, as a reference.</summary>
    private static double Npv(double rate, params double[] flows) => flows.Select((c, t) => c / Math.Pow(1 + rate / 100, t)).Sum();

    [Fact]
    public void Standard_npv_and_irr()
    {
        // The IRR example from the textbooks: -123,400 then 36,200, 54,800 and 48,100 gives 5.96 %.
        var r = CashFlowWorksheet.Evaluate(Flows("-123400", "10", "36200", "54800", "48100"), Reader()).Result!;
        Assert.Equal(5.96, Math.Round(r.Irr!.Value, 2));
        Near(Npv(10, -123400, 36200, 54800, 48100), r.Npv, 1e-12);
        Near(0, Npv(r.Irr.Value, -123400, 36200, 54800, 48100), 1e-9);
        Assert.Null(r.IrrNote);
    }

    [Fact]
    public void Design_example_with_payback()
    {
        var r = CashFlowWorksheet.Evaluate(CashFlowInputs.Default(), Reader()).Result!;
        Near(Npv(8, -1000, 300, 400, 500, 200), r.Npv, 1e-12);
        Near(0, Npv(r.Irr!.Value, -1000, 300, 400, 500, 200), 1e-9);
        Near(2.6, r.Payback!.Value, 1e-12);
        Assert.True(r.DiscountedPayback > r.Payback);
        Assert.NotNull(r.Mirr);
        Assert.True(r.Mirr < r.Irr, "MIRR at 8 % reinvestment sits below an IRR above 8 %");
    }

    [Fact]
    public void Frequencies_repeat_flows()
    {
        var withCounts = CashFlowWorksheet.Evaluate(Flows("-1000", "8", "300x3", "500"), Reader()).Result!;
        var written = CashFlowWorksheet.Evaluate(Flows("-1000", "8", "300", "300", "300", "500"), Reader()).Result!;
        Near(written.Npv, withCounts.Npv, 1e-12);
        Near(written.Irr!.Value, withCounts.Irr!.Value, 1e-9);
        Assert.Equal([-1000.0, 300, 300, 300, 500], withCounts.Expanded);
        Assert.False(withCounts.AllSingle);
    }

    [Fact]
    public void Trailing_blank_rows_are_ignored_but_gaps_are_not()
    {
        var ok = CashFlowWorksheet.Evaluate(Flows("-100", "5", "60", "60", ""), Reader());
        Assert.NotNull(ok.Result);
        Assert.Equal(2, ok.Result!.Amounts.Count);

        var gap = CashFlowWorksheet.Evaluate(Flows("-100", "5", "60", "", "60"), Reader());
        Assert.Null(gap.Result);
        Assert.Equal("CF2 is empty: type an amount (0 for nothing) or remove the row.", gap.FieldErrors["cf2"]);
    }

    [Fact]
    public void Validation_messages()
    {
        var o = CashFlowWorksheet.Evaluate(Flows("", "", "100x2.5"), Reader());
        Assert.Equal("CF0 is empty: type the starting amount (an investment is negative).", o.FieldErrors[CashFlowWorksheet.InitialKey]);
        Assert.Equal("The rate is empty: type the rate per period in percent, for example 8.", o.FieldErrors[CashFlowWorksheet.RateKey]);
        Assert.Equal("The number of times CF1 repeats must be a whole number.", o.FieldErrors["n1"]);
        Assert.Equal("Fix the fields marked in red to see the answers.", o.Problem);

        var none = CashFlowWorksheet.Evaluate(Flows("-100", "5"), Reader());
        Assert.Equal("Add at least one cash flow after CF0.", none.Problem);
    }

    [Fact]
    public void No_sign_change_means_no_irr_and_never_pays_back()
    {
        var r = CashFlowWorksheet.Evaluate(Flows("-100", "5", "-10", "-10"), Reader()).Result!;
        Assert.Null(r.Irr);
        Assert.Null(r.Payback);
        Assert.Equal("No IRR: the flows need at least one change of sign (money out, then money in).", r.IrrNote);
        Assert.NotNull(r.MirrNote);
    }

    [Fact]
    public void Several_irrs_are_reported()
    {
        // -100, +230, -132 has IRRs of 10 % and 20 %.
        var r = CashFlowWorksheet.Evaluate(Flows("-100", "5", "230", "-132"), Reader()).Result!;
        Assert.Equal(2, r.AllIrr.Count);
        Assert.Equal("Several rates give NPV 0: 10.00 %, 20.00 %. Use MIRR instead.", r.IrrNote);
    }

    [Fact]
    public void Csv_lists_every_period()
    {
        var r = CashFlowWorksheet.Evaluate(Flows("-1000", "10", "300x2", "500"), Reader()).Result!;
        var lines = CashFlowWorksheet.ToCsv(r).TrimEnd('\n').Split('\n');
        Assert.Equal("Period,Cash flow,Discounted,Cumulative,Cumulative discounted", lines[0]);
        Assert.Equal("0,-1000.00,-1000.00,-1000.00,-1000.00", lines[1]);
        Assert.Equal("1,300.00,272.73,-700.00,-727.27", lines[2]);
        Assert.Equal(6, lines.Length);
        Assert.StartsWith("NPV,", lines[^1]);
    }
}

using Calcy.Modes.Finance.Logic;
using Calcy.Statistics;
using static Calcy.Modes.Finance.Tests.TestSupport;

namespace Calcy.Modes.Finance.Tests;

/// <summary>The TVM solver, checked against the TI-84 Plus guidebook examples.</summary>
public class TvmWorksheetTests
{
    private static TvmOutcome Solve(TvmInputs inputs, TvmVariable? requested = null, TvmVariable? last = null) =>
        TvmWorksheet.Solve(inputs, Reader(), requested, last);

    [Fact]
    public void Guidebook_car_loan_interest_rate_is_14_90_percent()
    {
        // A 9,000 car paid with 250 a month for four years: what APR?
        var o = Solve(new TvmInputs { N = "48", InterestRate = "", PresentValue = "9000", Payment = "-250", FutureValue = "0" });
        Assert.Null(o.Problem);
        Assert.Equal(TvmVariable.InterestRate, o.Target);
        Assert.Equal(14.90, Math.Round(o.Solution!.Value, 2));
        Assert.Equal("I% (PER YEAR)", TvmWorksheet.SolvedLabel(o.Solution));
    }

    [Fact]
    public void Guidebook_mortgage_payment_is_minus_733_7645739()
    {
        // A 100,000 mortgage over 30 years at 8 %.
        var o = Solve(new TvmInputs { N = "360", InterestRate = "8", PresentValue = "100000", Payment = "", FutureValue = "0" });
        var s = o.Solution!;
        Assert.Equal(TvmVariable.Payment, s.Solved);
        Assert.Equal("-733.7645739", Money.Field(s.Value));
        Assert.Equal("-$733.76", TvmWorksheet.ValueText(s, Us));
        Assert.Equal("PMT (PER MONTH)", TvmWorksheet.SolvedLabel(s));
        Assert.Equal("Negative: you pay $733.76 at the end of every month for 30 years.", TvmWorksheet.Explain(s, Us));
        Near(-733.7645739 * 360, s.TotalPayments, 1e-8);
        Near(100000 - 733.7645739 * 360, s.NetInterest, 1e-8);
    }

    [Fact]
    public void Guidebook_compound_interest_rate_with_monthly_compounding()
    {
        // At what rate, compounded monthly, do 1,250 grow to 2,000 in 7 years (one payment period a year)?
        var o = Solve(new TvmInputs
        {
            N = "7", InterestRate = "", PresentValue = "-1250", Payment = "0", FutureValue = "2000",
            PaymentsPerYear = "1", CompoundingsPerYear = "12",
        });
        Assert.Equal(6.73, Math.Round(o.Solution!.Value, 2));
    }

    [Fact]
    public void Savings_future_value_end_and_begin_mode()
    {
        var inputs = new TvmInputs { N = "120", InterestRate = "5", PresentValue = "0", Payment = "-100", FutureValue = "" };
        var end = Solve(inputs).Solution!;
        Assert.Equal(15528.23, Money.Cents(end.Value));
        Assert.Equal("Positive: you receive $15,528.23 after 10 years.", TvmWorksheet.Explain(end, Us));

        inputs.Timing = PaymentTiming.Begin;
        var begin = Solve(inputs).Solution!;
        Assert.Equal(15592.93, Money.Cents(begin.Value));
        // Paying at the start of each month earns one more month of interest on every payment.
        Near(end.Value * (1 + 0.05 / 12), begin.Value, 1e-12);
    }

    [Fact]
    public void Begin_mode_payment_is_smaller_by_one_period_of_interest()
    {
        var inputs = new TvmInputs { N = "360", InterestRate = "8", PresentValue = "100000", Payment = "", FutureValue = "0", Timing = PaymentTiming.Begin };
        var s = Solve(inputs).Solution!;
        Near(-733.7645739 / (1 + 0.08 / 12), s.Value, 1e-9);
        Assert.Contains("at the start of every month", TvmWorksheet.Explain(s, Us));
    }

    [Fact]
    public void Every_variable_round_trips()
    {
        var full = new TvmValues(360, 6.5, 250000, Tvm.SolvePayment(360, 6.5, 250000, 0), 0);
        foreach (var v in TvmWorksheet.Variables)
        {
            var inputs = new TvmInputs
            {
                N = Money.Exact(full.N), InterestRate = Money.Exact(full.InterestRate), PresentValue = Money.Exact(full.PresentValue),
                Payment = Money.Exact(full.Payment), FutureValue = Money.Exact(full.FutureValue),
            };
            inputs.Set(v, "");
            var s = Solve(inputs).Solution;
            Assert.NotNull(s);
            Near(TvmWorksheet.Get(full, v), s!.Value, 1e-7);
        }
    }

    [Fact]
    public void Fields_take_expressions_and_percent_signs()
    {
        var o = Solve(new TvmInputs { N = "30*12", InterestRate = "8%", PresentValue = "250000 - 150000", Payment = "", FutureValue = "0" });
        Assert.Equal("-733.7645739", Money.Field(o.Solution!.Value));
        var o2 = Solve(new TvmInputs { N = "360", InterestRate = "96%/12", PresentValue = "100,000", Payment = "", FutureValue = "0" });
        Assert.Equal("-733.7645739", Money.Field(o2.Solution!.Value));
    }

    [Fact]
    public void Variables_from_the_shared_session_can_be_used()
    {
        var session = Session();
        session.Evaluate("price = 100000");
        var o = TvmWorksheet.Solve(new TvmInputs { N = "360", InterestRate = "8", PresentValue = "price", Payment = "", FutureValue = "0" }, Reader(session));
        Assert.Equal("-733.7645739", Money.Field(o.Solution!.Value));
    }

    [Fact]
    public void Picks_the_blank_field_or_the_requested_one_or_the_last_solved()
    {
        var inputs = new TvmInputs { N = "360", InterestRate = "8", PresentValue = "100000", Payment = "-733.7645739", FutureValue = "0" };
        var none = Solve(inputs);
        Assert.Null(none.Target);
        Assert.Equal("Every field is filled in. Clear the one you want to work out, or press SOLVE next to it.", none.Problem);

        Assert.Equal(TvmVariable.Payment, Solve(inputs, last: TvmVariable.Payment).Target);
        var pv = Solve(inputs, requested: TvmVariable.PresentValue);
        Assert.Equal(TvmVariable.PresentValue, pv.Target);
        Near(100000, pv.Solution!.Value, 1e-9);

        inputs.N = "";
        inputs.FutureValue = " ";
        var two = Solve(inputs);
        Assert.Null(two.Target);
        Assert.Equal("Leave only one field blank: N and FV are empty.", two.Problem);
    }

    [Fact]
    public void Validation_messages_are_plain_language()
    {
        var o = Solve(new TvmInputs { N = "-5", InterestRate = "-150", PresentValue = "$1,200", Payment = "", FutureValue = "x+1" });
        Assert.Null(o.Solution);
        Assert.Equal("Fix the fields marked in red to see the answer.", o.Problem);
        Assert.Equal("N must be more than 0.", o.FieldErrors["N"]);
        Assert.Equal("I% must be more than -100 %.", o.FieldErrors["I%"]);
        Assert.Equal("Leave out the currency sign: type 1200, not $1,200.", o.FieldErrors["PV"]);
        Assert.Equal("'x' has no value yet: type a number, or define x in Advanced first.", o.FieldErrors["FV"]);

        var py = Solve(new TvmInputs { N = "12", InterestRate = "5", PresentValue = "1000", Payment = "", FutureValue = "0", PaymentsPerYear = "0" });
        Assert.Equal("P/Y must be more than 0.", py.FieldErrors[TvmWorksheet.PaymentsPerYearKey]);
        Assert.Equal("Fix the field marked in red to see the answer.", py.Problem);
    }

    [Fact]
    public void Impossible_signs_explain_the_convention()
    {
        // Receiving both the loan and the payments: no interest rate can balance that.
        var o = Solve(new TvmInputs { N = "10", InterestRate = "", PresentValue = "1000", Payment = "100", FutureValue = "0" });
        Assert.Null(o.Solution);
        Assert.Contains("check the signs", o.Problem);
        Assert.Contains("PMT negative (money you pay)", o.Problem);
    }

    [Fact]
    public void Explanations_cover_each_variable()
    {
        var n = Solve(new TvmInputs { N = "", InterestRate = "6", PresentValue = "20000", Payment = "-500", FutureValue = "0" }).Solution!;
        Assert.Matches(@"^\d+(\.\d+)? payments, one every month: about 3\.\d+ years\.$", TvmWorksheet.Explain(n, Us));

        var pv = Solve(new TvmInputs { N = "60", InterestRate = "4.5", PresentValue = "", Payment = "-372.86", FutureValue = "0" }).Solution!;
        Assert.Matches(@"^Positive: you receive \$(19,999\.9\d|20,000\.0\d) at the start, like a loan you take out\.$", TvmWorksheet.Explain(pv, Us));

        var i = Solve(new TvmInputs { N = "60", InterestRate = "", PresentValue = "20000", Payment = "-372.86", FutureValue = "0" }).Solution!;
        Assert.Matches(@"^0\.37\d\d % per month, effective 4\.59\d\d % a year\.$", TvmWorksheet.Explain(i, Us));
    }

    [Fact]
    public void Presets_solve_their_blank_field()
    {
        foreach (var p in TvmPresets.All)
        {
            var o = Solve(TvmPresets.Fill(p));
            Assert.Null(o.Problem);
            Assert.Equal(p.Solves, o.Target);
        }
        var mortgage = Solve(TvmPresets.Fill(TvmPresets.Find("mortgage")!)).Solution!;
        Assert.Equal(-1896.20, Money.Cents(mortgage.Value));
        var retirement = Solve(TvmPresets.Fill(TvmPresets.Find("retirement")!)).Solution!;
        Assert.True(retirement.Value > 0, "a retirement income is money received");
    }
}

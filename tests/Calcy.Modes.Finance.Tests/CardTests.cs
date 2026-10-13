using System.Text.Json;
using Calcy.Modes.Finance.Logic;
using Calcy.Statistics;
using Calcy.UI.Services;
using static Calcy.Modes.Finance.Tests.TestSupport;

namespace Calcy.Modes.Finance.Tests;

/// <summary>Cards: their text, the USE expressions (which must evaluate to the same answers in Advanced) and persistence.</summary>
public class CardTests
{
    public static TheoryData<string, PaymentTiming, string> TvmCases()
    {
        var data = new TheoryData<string, PaymentTiming, string>();
        foreach (var timing in new[] { PaymentTiming.End, PaymentTiming.Begin })
            foreach (var cy in new[] { "12", "4" })
                foreach (var v in new[] { "N", "I%", "PV", "PMT", "FV" })
                    data.Add(v, timing, cy);
        return data;
    }

    [Theory]
    [MemberData(nameof(TvmCases))]
    public void Tvm_use_expression_evaluates_to_the_solved_value(string variable, PaymentTiming timing, string cy)
    {
        var target = TvmWorksheet.Variables.First(v => TvmWorksheet.Key(v) == variable);
        var full = new TvmValues(240, 5.25, 180000, 0, -20000, 12, cy == "12" ? null : 4, timing);
        full = Tvm.SolveFor(TvmVariable.Payment, full);
        var inputs = new TvmInputs
        {
            N = Money.Exact(full.N), InterestRate = Money.Exact(full.InterestRate), PresentValue = Money.Exact(full.PresentValue),
            Payment = Money.Exact(full.Payment), FutureValue = Money.Exact(full.FutureValue), CompoundingsPerYear = cy, Timing = timing,
        };
        inputs.Set(target, "");
        var session = Session();
        var s = TvmWorksheet.Solve(inputs, Reader(session)).Solution!;
        var card = FinanceCards.Tvm(s, Us);
        var use = Assert.Single(card.Uses);
        // Always a call to the engine's TVM function (never a bare number), with "begin" only for begin mode.
        Assert.Matches(@"^tvm_(pmt|n|i|pv|fv)\(", use.Expression);
        Assert.Equal(timing == PaymentTiming.Begin, use.Expression.EndsWith(", \"begin\")", StringComparison.Ordinal));
        Near(s.Value, Eval(session, use.Expression), 1e-9);
    }

    [Fact]
    public void Tvm_card_text_matches_the_design()
    {
        var s = TvmWorksheet.Solve(TvmInputs.Default(), Reader()).Solution!;
        var card = FinanceCards.Tvm(s, Us);
        Assert.Equal("TVM: PMT", card.Title);
        Assert.Equal("N = 60, I% = 4.5, PV = 20,000.00, FV = 0.00", card.Input);
        Assert.Equal("PMT = -$372.86", card.Result);
        Assert.Equal("tvm_pmt(60, 4.5, 20000, 0, 12, 12)", card.Uses[0].Expression);
        Assert.Equal("TVM: PMT\nN = 60, I% = 4.5, PV = 20,000.00, FV = 0.00\nPMT = -$372.86\n" + card.Note, card.CopyText());
        Assert.Null(card.Csv());
    }

    [Fact]
    public void Amortization_card_uses_sumint_sumprn_and_bal()
    {
        var v = new TvmValues(360, 8, 100000, Tvm.SolvePayment(360, 8, 100000, 0), 0);
        var report = AmortizationReport.TryBuild(v, out _)!;
        var range = report.Range(13, 24);
        var card = FinanceCards.Amortization(report, range, Us);
        var session = Session();
        Assert.Equal(["ΣInt", "ΣPrn", "Balance"], card.Uses.Select(u => u.Label));
        Assert.StartsWith("sumint(13, 24, 360, 8, 100000, ", card.Uses[0].Expression);
        Near(range.Interest, Eval(session, card.Uses[0].Expression), 1e-9);
        Near(range.Principal, Eval(session, card.Uses[1].Expression), 1e-9);
        Near(range.Balance, Eval(session, card.Uses[2].Expression), 1e-9);
        Assert.Equal(report.ToCsv(), card.Csv());
    }

    [Theory]
    [InlineData(PaymentTiming.End, 4.0)]
    [InlineData(PaymentTiming.Begin, 12.0)]
    [InlineData(PaymentTiming.Begin, 2.0)]
    public void Amortization_card_passes_cy_and_timing_to_the_engine(PaymentTiming timing, double cy)
    {
        var v = new TvmValues(120, 6, 50000, 0, 1000, 12, cy, timing);
        v = Tvm.SolveFor(TvmVariable.Payment, v);
        var report = AmortizationReport.TryBuild(v, out _)!;
        var range = report.Range(25, 36);
        var card = FinanceCards.Amortization(report, range, Us);
        var session = Session();
        Assert.All(card.Uses, u => Assert.Matches(@"^(sumint|sumprn|bal)\(", u.Expression));
        Assert.All(card.Uses, u => Assert.Equal(timing == PaymentTiming.Begin, u.Expression.EndsWith(", \"begin\")", StringComparison.Ordinal)));
        Near(range.Interest, Eval(session, card.Uses[0].Expression), 1e-9);
        Near(range.Principal, Eval(session, card.Uses[1].Expression), 1e-9);
        Near(range.Balance, Eval(session, card.Uses[2].Expression), 1e-9);
    }

    [Fact]
    public void Cash_flow_card_uses_npv_irr_and_mirr()
    {
        var inputs = new CashFlowInputs { Initial = "-1000", Rate = "8", FinanceRate = "6", ReinvestRate = "10", Flows = [new() { Amount = "300", Count = "2" }, new() { Amount = "500" }] };
        var r = CashFlowWorksheet.Evaluate(inputs, Reader()).Result!;
        var card = FinanceCards.CashFlow(r, Us);
        Assert.Equal("CF: -1,000, 300 ×2, 500\nRate 8.00 %", card.Input);
        Assert.Equal("npv(8, -1000, {300, 500}, {2, 1})", card.Uses[0].Expression);
        Assert.Equal("irr(-1000, {300, 500}, {2, 1})", card.Uses[1].Expression);
        Assert.Equal("mirr(6, 10, -1000, {300, 500}, {2, 1})", card.Uses[2].Expression);
        var session = Session();
        Near(r.Npv, Eval(session, card.Uses[0].Expression), 1e-9);
        Near(r.Irr!.Value, Eval(session, card.Uses[1].Expression), 1e-9);
        Near(r.Mirr!.Value, Eval(session, card.Uses[2].Expression), 1e-9);
        Assert.Equal(CashFlowWorksheet.ToCsv(r), card.Csv());
    }

    [Fact]
    public void Interest_and_date_cards_evaluate_in_advanced()
    {
        var session = Session();
        var reader = Reader(session);
        var eff = InterestTools.Convert(new InterestInputs { Rate = "6", Compoundings = "12" }, reader).Value!;
        var effCard = FinanceCards.Rate(eff);
        Assert.Equal("eff(6, 12)", effCard.Uses[0].Expression);
        Near(eff.Output, Eval(session, effCard.Uses[0].Expression));
        Assert.Equal("6.1678 % effective", effCard.Result);

        var cont = InterestTools.Convert(new InterestInputs { Direction = RateDirection.EffectiveToNominal, Rate = "7", Compoundings = "inf" }, reader).Value!;
        Near(cont.Output, Eval(session, FinanceCards.Rate(cont).Uses[0].Expression));

        var simple = InterestTools.Simple(new InterestInputs { Principal = "10000", AnnualRate = "5", Days = "90" }, reader).Value!;
        var sc = FinanceCards.SimpleInterest(simple, Us);
        Near(simple.Interest, Eval(session, sc.Uses[0].Expression));
        Near(simple.Total, Eval(session, sc.Uses[1].Expression));

        foreach (var m in new[] { "12", "continuous" })
        {
            var comp = InterestTools.Compound(new InterestInputs { Principal = "-2500", AnnualRate = "4", Years = "7", YearCompoundings = m }, reader).Value!;
            var cc = FinanceCards.CompoundInterest(comp, Us);
            Near(comp.FutureValue, Eval(session, cc.Uses[0].Expression), 1e-12);
            Near(comp.Interest, Eval(session, cc.Uses[1].Expression), 1e-9);
        }

        var days = DateCalculator.Count(new DateInputs { Start = "2026-10-13", End = "2027-01-01" }).Value!;
        var dc = FinanceCards.DaysBetween(days);
        Assert.Equal("dbd(10.1326, 1.0127)", dc.Uses[0].Expression);
        Assert.Equal(days.Actual, Eval(session, dc.Uses[0].Expression));
        Assert.Equal(days.Thirty360US, Eval(session, dc.Uses[1].Expression));

        var add = FinanceCards.AddDays(DateCalculator.Add(new DateInputs { From = "2026-10-13", Days = "-13" }, reader).Value!);
        Assert.Equal("2026-10-13 (Tue) - 13 days", add.Input);
        Assert.Empty(add.Uses);
    }

    [Fact]
    public void Depreciation_and_bond_cards()
    {
        var session = Session();
        var reader = Reader(session);
        var dep = DepreciationReport.Evaluate(new DepreciationInputs { Method = DepreciationMethod.SumOfYearsDigits }, reader).Value!;
        var card = FinanceCards.Depreciation(dep, Us);
        Assert.Equal("DEPRECIATION: SYD", card.Title);
        Assert.Equal("{3000, 2400, 1800, 1200, 600}", card.Uses[0].Expression);
        Assert.Equal(dep.ToCsv(), card.Csv());

        var bond = BondWorksheet.Solve(new BondInputs(), reader).Value!;
        var bc = FinanceCards.Bond(bond);
        Assert.Equal("BOND PRICE", bc.Title);
        Near(bond.CleanPrice, Eval(session, bc.Uses[0].Expression), 1e-15);
    }

    [Fact]
    public void State_numbers_cards_and_survives_a_round_trip_through_preferences()
    {
        var state = new FinanceState();
        for (int i = 0; i < FinanceState.MaxCards + 5; i++) state.AddCard(new FinanceCard { Title = "T" + i });
        Assert.Equal(FinanceState.MaxCards, state.Cards.Count);
        Assert.Equal(FinanceState.MaxCards + 5, state.Cards[0].Number);
        Assert.Equal("T" + (FinanceState.MaxCards + 4), state.Cards[0].Title);

        var v = new TvmValues(360, 8, 100000, Tvm.SolvePayment(360, 8, 100000, 0), 0);
        var report = AmortizationReport.TryBuild(v, out _)!;
        state.AddCard(FinanceCards.Amortization(report, report.Range(1, 12), Us));
        state.Tab = FinanceTab.Bonds;
        state.Solved = TvmVariable.Payment;
        state.Tvm.Timing = PaymentTiming.Begin;

        var prefs = new Preferences();
        prefs.SetModeState("finance", state);
        var json = JsonSerializer.Serialize(prefs);
        var back = JsonSerializer.Deserialize<Preferences>(json)!.GetModeState<FinanceState>("finance")!.Normalize();
        Assert.Equal(FinanceTab.Bonds, back.Tab);
        Assert.Equal(TvmVariable.Payment, back.Solved);
        Assert.Equal(PaymentTiming.Begin, back.Tvm.Timing);
        Assert.Equal(state.Cards.Count, back.Cards.Count);
        Assert.Equal(report.ToCsv(), back.Cards[0].Csv());
        Assert.Equal(state.NextCardNumber, back.NextCardNumber);
    }

    [Fact]
    public void Normalize_repairs_missing_parts()
    {
        var s = JsonSerializer.Deserialize<FinanceState>("""{"Tvm":null,"Cards":[null,{"Number":7,"Title":null}],"NextCardNumber":2,"Tab":99}""")!.Normalize();
        Assert.NotNull(s.Tvm);
        Assert.Single(s.Cards);
        Assert.Equal("", s.Cards[0].Title);
        Assert.Equal(8, s.NextCardNumber);
        Assert.Equal(FinanceTab.Tvm, s.Tab);
    }
}

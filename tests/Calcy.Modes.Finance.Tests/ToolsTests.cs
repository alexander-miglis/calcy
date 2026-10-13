using System.Globalization;
using Calcy.Modes.Finance.Logic;
using Calcy.Statistics;
using static Calcy.Modes.Finance.Tests.TestSupport;

namespace Calcy.Modes.Finance.Tests;

/// <summary>Money formatting, field reading, interest, dates and bonds.</summary>
public class ToolsTests
{
    [Fact]
    public void Money_has_grouping_two_decimals_and_a_display_only_symbol()
    {
        Assert.Equal("-$1,234,567.89", Money.Format(-1234567.891, Us));
        Assert.Equal("$0.00", Money.Format(-0.004, Us));
        Assert.Equal("-1,234.50 €", Money.Format(-1234.5, CultureInfo.GetCultureInfo("de-DE")));
        Assert.Equal("1,234.50", Money.Format(1234.5, CultureInfo.InvariantCulture));
        Assert.Equal("-1,234.50", Money.Plain(-1234.5));
        Assert.Equal("-1234.50", Money.Csv(-1234.5));
        Assert.Equal("?", Money.Plain(double.NaN));
        Assert.Equal("6.1678 %", Money.Percent(6.16778118644995));
        Assert.Equal("47.25", Money.Number(47.25));
        Assert.Equal("1,200", Money.Number(1200));
        Assert.Equal("-733.7645739", Money.Field(-733.764573879378));
        Assert.Equal("1.234567891e20", Money.Field(1.2345678912e20));
        Assert.Equal("0", Money.Field(-1e-320 * 0));
    }

    [Fact]
    public void Reader_accepts_expressions_and_grouped_numbers()
    {
        var r = Reader();
        Assert.Equal(360, r.Read("30*12", FieldRule.Amount("N")).Value);
        Assert.Equal(1234567.5, r.Read("1,234,567.5", FieldRule.Amount("PV")).Value);
        Assert.Equal(5.5, r.Read("5.5%", FieldRule.Rate("I%")).Value, 12);
        Assert.Equal(5.5, r.Read("5.5", FieldRule.Rate("I%")).Value, 12);
        Assert.Equal(0.5, r.Read("6%/12", FieldRule.Rate("I%")).Value, 12);
        Assert.Equal(200000, r.Read("250000*80%", FieldRule.Amount("PV")).Value, 9);
        Assert.True(r.Read("   ", FieldRule.Amount("PV")).IsBlank);
        Assert.True(double.IsPositiveInfinity(r.Read("continuous", new FieldRule("m", AllowInfinity: true)).Value));
        Assert.True(double.IsPositiveInfinity(r.Read("inf", new FieldRule("m", AllowInfinity: true)).Value));
    }

    [Fact]
    public void Reader_messages()
    {
        var r = Reader();
        Assert.Equal("Leave out the currency sign: type 1200, not $1,200.", r.Read("€ 5", FieldRule.Amount("PV")).Error);
        Assert.Equal("The life must be a whole number.", r.Read("2.5", FieldRule.Whole("the life", 1, 100)).Error);
        Assert.Equal("The life must be at most 100.", r.Read("101", FieldRule.Whole("the life", 1, 100)).Error);
        Assert.Equal("PV must be a real number, not a complex one.", r.Read("sqrt(-4)", FieldRule.Amount("PV")).Error);
        Assert.Equal("PV is too large.", r.Read("inf", FieldRule.Amount("PV")).Error);
        Assert.Equal("PV needs a single number.", r.Read("{1, 2}", FieldRule.Amount("PV")).Error);
        var parse = r.Read("3*(4", FieldRule.Amount("PV"));
        Assert.False(parse.IsValid);
        Assert.EndsWith(".", parse.Error);
    }

    [Fact]
    public void Currency_words_next_to_numbers_are_pointed_out()
    {
        var danish = new ValueReader(Session(), CultureInfo.GetCultureInfo("da-DK"));
        Assert.Equal("Leave out the currency sign: type 1200, not $1,200.", danish.Read("1200 kr", FieldRule.Amount("PV")).Error);
        Assert.Equal("Leave out the currency sign: type 1200, not $1,200.", danish.Read("kr 1200", FieldRule.Amount("PV")).Error);
        Assert.Equal(1200, danish.Read("1200", FieldRule.Amount("PV")).Value);
    }

    [Fact]
    public void Nominal_and_effective_rates()
    {
        var r = Reader();
        var eff = InterestTools.Convert(new InterestInputs { Rate = "6", Compoundings = "12" }, r).Value!;
        Assert.Equal("6.1678 %", Money.Percent(eff.Output));
        var nom = InterestTools.Convert(new InterestInputs { Direction = RateDirection.EffectiveToNominal, Rate = Money.Exact(eff.Output), Compoundings = "12" }, r).Value!;
        Near(6, nom.Output, 1e-12);
        var cont = InterestTools.Convert(new InterestInputs { Rate = "6", Compoundings = "continuous" }, r).Value!;
        Near(100 * (Math.Exp(0.06) - 1), cont.Output, 1e-12);
        Assert.Equal("continuously", InterestTools.HowOften(cont.Compoundings));
        var bad = InterestTools.Convert(new InterestInputs { Rate = "6", Compoundings = "0" }, r);
        Assert.Equal("The compounding periods must be more than 0.", bad.FieldErrors["compoundings"]);
    }

    [Fact]
    public void Simple_and_compound_interest()
    {
        var r = Reader();
        var s = InterestTools.Simple(new InterestInputs { Principal = "10000", AnnualRate = "5", Days = "90", DayBasis = 365 }, r).Value!;
        Assert.Equal(123.29, Money.Cents(s.Interest));
        Assert.Equal(10123.29, Money.Cents(s.Total));
        var s360 = InterestTools.Simple(new InterestInputs { Principal = "10000", AnnualRate = "5", Days = "90", DayBasis = 360 }, r).Value!;
        Assert.Equal(125, s360.Interest, 9);
        var c = InterestTools.Compound(new InterestInputs { Principal = "10000", AnnualRate = "5", Years = "3", YearCompoundings = "12" }, r).Value!;
        Assert.Equal(11614.72, Money.Cents(c.FutureValue));
        Assert.Equal(1614.72, Money.Cents(c.Interest));
    }

    [Fact]
    public void Day_counts_actual_and_30_360()
    {
        var d = DateCalculator.Count(new DateInputs { Start = "2026-01-15", End = "2026-12-31" }).Value!;
        Assert.Equal(350, d.Actual);
        Assert.Equal(346, d.Thirty360US);
        Assert.Equal(345, d.Thirty360European);
        var back = DateCalculator.Count(new DateInputs { Start = "2026-12-31", End = "2026-01-15" }).Value!;
        Assert.Equal(-350, back.Actual);
    }

    [Fact]
    public void Dates_parse_and_add()
    {
        var today = new DateOnly(2026, 10, 13);
        Assert.True(DateCalculator.TryParse("today", out var t, out _, today));
        Assert.Equal(today, t);
        Assert.True(DateCalculator.TryParse("2026/3/5", out var iso, out _, today));
        Assert.Equal(new DateOnly(2026, 3, 5), iso);
        Assert.True(DateCalculator.TryParse("3/5/2026", out var us, out _, today, Us));
        Assert.Equal(new DateOnly(2026, 3, 5), us);
        Assert.False(DateCalculator.TryParse("2026-02-30", out _, out var error, today));
        Assert.Equal("'2026-02-30' is not a date: type it like 2026-10-13.", error);

        var add = DateCalculator.Add(new DateInputs { From = "2026-10-13", Days = "90" }, Reader(), today).Value!;
        Assert.Equal(new DateOnly(2027, 1, 11), add.Result);
        Assert.Equal("2027-01-11 (Mon)", DateCalculator.Show(add.Result));
        var minus = DateCalculator.Add(new DateInputs { From = "2026-10-13", Days = "-13" }, Reader(), today).Value!;
        Assert.Equal(new DateOnly(2026, 9, 30), minus.Result);
        var far = DateCalculator.Add(new DateInputs { From = "9999-12-01", Days = "100" }, Reader(), today);
        Assert.Equal("That date is outside the calendar (years 1 to 9999).", far.Problem);
        Assert.Equal("10.1326", DateCalculator.TiDate(today));
        Assert.Null(DateCalculator.TiDate(new DateOnly(2050, 1, 1)));
    }

    [Fact]
    public void Bond_price_matches_the_spreadsheet_example_and_yield_round_trips()
    {
        // Excel PRICE("2008-02-15", "2017-11-15", 5.75 %, 6.5 %, 100, 2, 30/360) = 94.6343616
        var inputs = new BondInputs { Settlement = "2008-02-15", Maturity = "2017-11-15", Coupon = "5.75", Yield = "6.5", Price = "", Frequency = 2, DayCount = DayCountConvention.Thirty360US };
        var p = BondWorksheet.Solve(inputs, Reader()).Value!;
        Assert.Equal(BondTarget.Price, p.Solved);
        Assert.Equal(94.6343616, Math.Round(p.CleanPrice, 7));
        Assert.Equal(p.CleanPrice + p.Accrued, p.DirtyPrice, 12);

        inputs.Price = Money.Exact(p.CleanPrice);
        inputs.Yield = "";
        var y = BondWorksheet.Solve(inputs, Reader()).Value!;
        Assert.Equal(BondTarget.Yield, y.Solved);
        Near(6.5, y.Yield, 1e-9);

        inputs.Maturity = "2001-01-01";
        Assert.Equal("The maturity date must be after the settlement date.", BondWorksheet.Solve(inputs, Reader()).FieldErrors["maturity"]);
        inputs.Price = "";
        Assert.Equal("Type either the yield or the price; the other one is worked out.", BondWorksheet.Solve(inputs, Reader()).Problem);
    }
}

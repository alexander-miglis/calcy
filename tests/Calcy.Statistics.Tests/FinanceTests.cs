using Calcy.Statistics;

namespace Calcy.Statistics.Tests;

public class FinanceTests
{
    [Fact]
    public void TiGuidebookCarLoanRate()
    {
        // TI-84 guidebook: a $9,000 car with payments of $250 per month for 4 years, I% = 14.90.
        double rate = Tvm.SolveInterestRate(48, 9000, -250, 0, 12);
        Assert.Equal(14.90, Math.Round(rate, 2));
        Tol.Rel(14.89544588272734, rate, 1e-12);
        Tol.Rel(-250, Tvm.SolvePayment(48, rate, 9000, 0, 12), 1e-12);
    }

    [Fact]
    public void TiGuidebookCompoundInterestRate()
    {
        // At what annual rate, compounded monthly, does $1,250 grow to $2,000 in 7 years? I% = 6.73.
        double rate = Tvm.SolveInterestRate(7, -1250, 0, 2000, paymentsPerYear: 1, compoundingsPerYear: 12);
        Assert.Equal(6.73, Math.Round(rate, 2));
        Tol.Rel(2000, Tvm.SolveFutureValue(7, rate, -1250, 0, 1, 12), 1e-12);
    }

    [Fact]
    public void MortgagePaymentAndSolverRoundTrips()
    {
        double pmt = Tvm.SolvePayment(360, 8, 100000, 0, 12);
        Tol.Rel(-733.76457387937611, pmt, 1e-14);
        Tol.Digits(-733.7645739, pmt);
        var v = new TvmValues(360, 8, 100000, pmt, 0, 12);
        Tol.Rel(360, Tvm.Solve(TvmVariable.N, v), 1e-12);
        Tol.Rel(8, Tvm.Solve(TvmVariable.InterestRate, v), 1e-11);
        Tol.Rel(100000, Tvm.Solve(TvmVariable.PresentValue, v), 1e-12);
        Tol.Abs(0, Tvm.Solve(TvmVariable.FutureValue, v), 1e-7);
        Assert.Equal(pmt, Tvm.SolveFor(TvmVariable.Payment, v with { Payment = 0 }).Payment, 9);
    }

    [Fact]
    public void BeginModeAndCompoundingDifferentFromPayments()
    {
        double end = Tvm.SolvePayment(360, 8, 100000, 0, 12);
        double begin = Tvm.SolvePayment(360, 8, 100000, 0, 12, timing: PaymentTiming.Begin);
        Tol.Rel(end / (1 + 0.08 / 12), begin, 1e-13);
        double i = Tvm.PeriodicRate(6, 12, 4);
        Tol.Rel(Math.Pow(1.015, 1.0 / 3) - 1, i, 1e-13);
        Tol.Rel(6, Tvm.AnnualRate(i, 12, 4), 1e-13);
        double rate = Tvm.SolveInterestRate(60, 20000, -390, 0, 12, 4, PaymentTiming.Begin);
        Tol.Abs(0, Tvm.SolveFutureValue(60, rate, 20000, -390, 12, 4, PaymentTiming.Begin), 1e-6);
    }

    [Fact]
    public void FutureValueAndZeroRate()
    {
        Tol.Rel(1628.8946267774414, Tvm.SolveFutureValue(10, 5, -1000, 0, 1), 1e-14);
        Assert.Equal(-500, Tvm.SolvePayment(10, 0, 5000, 0), 12);
        Assert.Equal(10, Tvm.SolveN(0, 5000, -500, 0), 12);
        Assert.Equal(0, Tvm.SolveInterestRate(10, 5000, -500, 0), 9);
    }

    [Fact]
    public void ImpossibleTvmProblemsThrow()
    {
        Assert.Throws<ArgumentException>(() => Tvm.SolveInterestRate(10, 1000, 100, 1000));
        // A payment of 5 never covers the monthly interest of 10 on 1000 at 12%.
        Assert.Throws<ArgumentException>(() => Tvm.SolveN(12, 1000, -5, 0));
        Assert.Throws<ArgumentException>(() => Tvm.SolvePayment(0, 5, 1000, 0));
        Assert.Throws<ArgumentException>(() => Tvm.PeriodicRate(5, 0));
    }

    [Fact]
    public void AmortizationMatchesTiFunctions()
    {
        var tvm = new TvmValues(360, 8, 100000, Tvm.SolvePayment(360, 8, 100000, 0, 12), 0, 12);
        Tol.Rel(99164.6360190293, Amortization.Balance(tvm, 12), 1e-12);
        Tol.Rel(-7969.810905581819, Amortization.SumInterest(tvm, 1, 12), 1e-11);
        Tol.Rel(-835.3639809706947, Amortization.SumPrincipal(tvm, 1, 12), 1e-10);
        Tol.Abs(0, Amortization.Balance(tvm, 360), 1e-6);
        Tol.Rel(-100000, Amortization.SumPrincipal(tvm, 1, 360), 1e-12);
        double totalInterest = Amortization.SumInterest(tvm, 1, 360);
        Tol.Rel(360 * tvm.Payment + 100000, totalInterest, 1e-11);
        var rows = Amortization.Schedule(tvm);
        Assert.Equal(360, rows.Count);
        Tol.Rel(-666.6666666666666, rows[0].Interest, 1e-12);
        Tol.Rel(rows[0].Payment - rows[0].Interest, rows[0].Principal, 1e-14);
        Tol.Rel(Amortization.Balance(tvm, 100), rows[99].Balance, 1e-13);
        Tol.Rel(rows.Skip(12).Take(12).Sum(r => r.Interest), Amortization.SumInterest(tvm, 13, 24), 1e-11);
    }

    [Fact]
    public void AmortizationWithRoundingAndBeginMode()
    {
        var tvm = new TvmValues(360, 8, 100000, -733.76, 0, 12);
        double b = Amortization.Balance(tvm, 1, roundDigits: 2);
        Assert.Equal(100000 - 733.76 + 666.67, b, 9);
        var begin = new TvmValues(24, 6, 5000, Tvm.SolvePayment(24, 6, 5000, 0, 12, timing: PaymentTiming.Begin), 0, 12, Timing: PaymentTiming.Begin);
        Tol.Abs(0, Amortization.Balance(begin, 24), 1e-9);
        var rows = Amortization.Schedule(begin);
        Assert.Equal(0, rows[0].Interest);
        Tol.Rel(5000 + begin.Payment, rows[0].Balance, 1e-14);
    }

    [Fact]
    public void NetPresentValue()
    {
        Tol.Rel(1307.2877535687453, CashFlow.Npv(10, -10000, [3000, 4200, 6800]), 1e-14);
        Tol.Rel(19.486536500654719, CashFlow.Npv(8, -1000, [200, 300], [2, 3]), 1e-12);
        Assert.Equal(-100 + 60 + 60, CashFlow.Npv(0, -100, [60, 60]), 12);
    }

    [Fact]
    public void InternalRateOfReturn()
    {
        Tol.Rel(-2.1244848273410991, CashFlow.Irr(-70000, [12000, 15000, 18000, 21000]), 1e-10);
        Tol.Rel(8.6630948036531614, CashFlow.Irr(-70000, [12000, 15000, 18000, 21000, 26000]), 1e-11);
        Tol.Rel(8.8963394693349935, CashFlow.Irr(-1000, [300, 400, 500]), 1e-11);
        var all = CashFlow.IrrAll(-100, [230, -132]);
        Assert.Equal(2, all.Count);
        Tol.Rel(10, all[0], 1e-10);
        Tol.Rel(20, all[1], 1e-10);
        Tol.Rel(20, CashFlow.Irr(-100, [230, -132], guessPercent: 25), 1e-10);
        Tol.Abs(0, CashFlow.Npv(CashFlow.Irr(-5000, [1000, 2000], [2, 2]), -5000, [1000, 2000], [2, 2]), 1e-9);
        Assert.Throws<ArgumentException>(() => CashFlow.Irr(100, [100, 100]));
    }

    [Fact]
    public void ModifiedIrrAndPayback()
    {
        Tol.Rel(12.609413036590515, CashFlow.Mirr(10, 12, -120000, [39000, 30000, 21000, 37000, 46000]), 1e-12);
        Assert.Equal(2.6, CashFlow.PaybackPeriod(-1000, [300, 400, 500])!.Value, 12);
        Assert.Null(CashFlow.PaybackPeriod(-1000, [100, 100]));
        double dpp = CashFlow.DiscountedPaybackPeriod(10, -1000, [500, 500, 500])!.Value;
        Assert.InRange(dpp, 2, 3);
        Assert.Throws<ArgumentException>(() => CashFlow.Mirr(10, 10, 100, [100]));
    }

    [Fact]
    public void NominalAndEffectiveRates()
    {
        Tol.Rel(12.682503013196972, Finance.EffectiveRate(12, 12), 1e-14);
        Tol.Rel(12, Finance.NominalRate(12.682503013196972, 12), 1e-13);
        Tol.Rel(10.517091807564762, Finance.EffectiveRate(10, double.PositiveInfinity), 1e-14);
        Tol.Rel(10, Finance.NominalRate(10.517091807564762, double.PositiveInfinity), 1e-14);
        Assert.Equal(5, Finance.EffectiveRate(5, 1), 13);
    }

    [Theory]
    [InlineData(2020, 1, 1, 2021, 1, 1, DayCountConvention.Actual, 366)]
    [InlineData(2021, 3, 15, 2021, 2, 1, DayCountConvention.Actual, -42)]
    [InlineData(2020, 1, 31, 2020, 3, 31, DayCountConvention.Thirty360US, 60)]
    [InlineData(2020, 1, 15, 2020, 3, 31, DayCountConvention.Thirty360US, 76)]
    [InlineData(2020, 1, 15, 2020, 3, 31, DayCountConvention.Thirty360European, 75)]
    [InlineData(2019, 2, 28, 2020, 2, 28, DayCountConvention.Thirty360US, 360)]
    public void DayCounts(int y1, int m1, int d1, int y2, int m2, int d2, DayCountConvention c, int expected)
    {
        Assert.Equal(expected, Finance.DaysBetweenDates(new DateOnly(y1, m1, d1), new DateOnly(y2, m2, d2), c));
    }

    [Fact]
    public void TiDateFormats()
    {
        Assert.Equal(new DateOnly(1993, 6, 1), Finance.ParseTiDate(6.0193));
        Assert.Equal(new DateOnly(1993, 12, 25), Finance.ParseTiDate(2512.93));
        Assert.Equal(new DateOnly(2007, 7, 4), Finance.ParseTiDate(7.0407));
        Assert.Equal(207, Finance.DaysBetweenDates(6.0193, 12.2593));
        Assert.Equal(207, Finance.DaysBetweenDates(106.93, 2512.93));
        Assert.Throws<ArgumentException>(() => Finance.ParseTiDate(2.3093));
    }

    [Fact]
    public void SimpleAndCompoundInterest()
    {
        var si = Finance.SimpleInterest(1000, 5, 90);
        Tol.Rel(12.328767123287671, si.Interest, 1e-15);
        Tol.Rel(1012.3287671232877, si.FutureValue, 1e-15);
        Assert.Equal(12.5, Finance.SimpleInterest(1000, 5, 90, 360).Interest, 12);
        Tol.Rel(1819.3967340323132, Finance.CompoundInterest(1000, 6, 10, 12), 1e-14);
        Tol.Rel(1822.118800390509, Finance.CompoundInterest(1000, 6, 10, double.PositiveInfinity), 1e-14);
    }

    [Fact]
    public void DepreciationSchedules()
    {
        var sl = Depreciation.StraightLine(10000, 1000, 5);
        Assert.All(sl, r => Assert.Equal(1800, r.Depreciation, 12));
        Assert.Equal(1000, sl[^1].BookValue, 9);
        var syd = Depreciation.SumOfYearsDigits(10000, 1000, 5);
        Assert.Equal([3000.0, 2400, 1800, 1200, 600], syd.Select(r => Math.Round(r.Depreciation, 9)));
        Assert.Equal(9000, syd[^1].AccumulatedDepreciation, 9);
        var ddb = Depreciation.DecliningBalance(10000, 1000, 5);
        Assert.Equal([4000.0, 2400, 1440, 864, 296], ddb.Select(r => Math.Round(r.Depreciation, 9)));
        Assert.Equal(1000, ddb[^1].BookValue, 9);
        var noSwitch = Depreciation.DecliningBalance(10000, 0, 5);
        Assert.Equal(777.6, noSwitch[^1].BookValue, 9);
        var withSwitch = Depreciation.DecliningBalance(10000, 0, 5, switchToStraightLine: true);
        Assert.Equal([4000.0, 2400, 1440, 1080, 1080], withSwitch.Select(r => Math.Round(r.Depreciation, 9)));
        Assert.Equal(0, withSwitch[^1].BookValue, 9);
        Assert.Throws<ArgumentException>(() => Depreciation.StraightLine(1000, 2000, 5));
        Assert.Throws<ArgumentException>(() => Depreciation.StraightLine(1000, 0, 0));
    }

    [Fact]
    public void BondPriceAndYieldExcelExamples()
    {
        var p = Bonds.BondPrice(new DateOnly(2008, 2, 15), new DateOnly(2017, 11, 15), 5.75, 6.5);
        Tol.Rel(94.63436162132, p.CleanPrice, 1e-11);
        Assert.Equal(1.4375, p.AccruedInterest, 12);
        Tol.Rel(p.CleanPrice + p.AccruedInterest, p.DirtyPrice, 1e-15);
        Assert.Equal(new DateOnly(2008, 5, 15), p.NextCouponDate);
        Assert.Equal(20, p.CouponsRemaining);
        double y = Bonds.BondYield(new DateOnly(2008, 2, 15), new DateOnly(2016, 11, 15), 5.75, 95.04287);
        Tol.Rel(6.5, y, 2e-7);
    }

    [Fact]
    public void BondOnCouponDate()
    {
        Tol.Rel(92.561262569772247, Bonds.BondPrice(5, 6, 20, 2), 1e-14);
        Tol.Rel(100, Bonds.BondPrice(7, 7, 30, 2), 1e-14);
        Tol.Rel(6, Bonds.BondYield(5, 92.561262569772247, 20, 2), 1e-11);
        Tol.Rel(100 / Math.Pow(1.05, 10), Bonds.BondPrice(0, 5, 10, 1), 1e-14);
        var annual = Bonds.BondPrice(new DateOnly(2020, 6, 1), new DateOnly(2025, 6, 1), 4, 4, 100, 1, DayCountConvention.Actual);
        Tol.Rel(100, annual.CleanPrice, 1e-12);
        Assert.Equal(0, annual.AccruedInterest);
    }

    [Fact]
    public void BreakEvenAndPricingHelpers()
    {
        Assert.Equal(500, Finance.BreakEvenQuantity(10000, 50, 30), 12);
        Assert.Equal(600, Finance.BreakEvenQuantity(10000, 50, 30, 2000), 12);
        Assert.Equal(50, Finance.BreakEvenPrice(10000, 500, 30), 12);
        Assert.Equal(0, Finance.Profit(10000, 50, 30, 500), 12);
        Assert.Equal(100, Finance.SellingPrice(75, 25), 12);
        Assert.Equal(75, Finance.CostFromSellingPrice(100, 25), 12);
        Assert.Equal(25, Finance.Margin(75, 100), 12);
        Tol.Rel(100.0 / 3, Finance.Markup(75, 100), 1e-14);
        Assert.Equal(100, Finance.SellingPriceFromMarkup(75, 100.0 / 3), 12);
        Assert.Throws<ArgumentException>(() => Finance.BreakEvenQuantity(100, 10, 12));
        Assert.Throws<ArgumentException>(() => Finance.SellingPrice(10, 100));
    }
}

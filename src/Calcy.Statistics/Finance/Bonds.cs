using Calcy.Statistics.Internal;

namespace Calcy.Statistics;

/// <summary>Bond price per 100 of face value with accrued interest.</summary>
public sealed record BondPriceResult(double CleanPrice, double AccruedInterest, double DirtyPrice, int CouponsRemaining, DateOnly PreviousCouponDate, DateOnly NextCouponDate);

/// <summary>
/// Bond price and yield (TI BA II Plus and Excel PRICE / YIELD conventions) with annual or semiannual coupons.
/// Prices are per 100 of face value; rates are annual percentages.
/// </summary>
public static class Bonds
{
    private static void CheckFrequency(int couponsPerYear)
    {
        if (couponsPerYear is not (1 or 2 or 4 or 12)) throw new ArgumentException("coupons per year must be 1, 2, 4 or 12", nameof(couponsPerYear));
    }

    private static DateOnly AddMonthsEom(DateOnly d, int months, bool endOfMonth)
    {
        var r = d.AddMonths(months);
        if (endOfMonth) r = new DateOnly(r.Year, r.Month, DateTime.DaysInMonth(r.Year, r.Month));
        return r;
    }

    /// <summary>Price on any settlement date before maturity, with accrued interest.</summary>
    public static BondPriceResult BondPrice(DateOnly settlement, DateOnly maturity, double couponRatePercent, double yieldPercent,
        double redemption = 100, int couponsPerYear = 2, DayCountConvention dayCount = DayCountConvention.Thirty360US)
    {
        Guard.Finite(yieldPercent, "the yield", nameof(yieldPercent));
        var s = Schedule(settlement, maturity, couponRatePercent, redemption, couponsPerYear, dayCount);
        double clean = CleanPrice(s, yieldPercent, couponsPerYear);
        return new BondPriceResult(clean, s.Accrued, clean + s.Accrued, s.N, s.Pcd, s.Ncd);
    }

    /// <summary>Yield to maturity (percent) for a clean price on a settlement date.</summary>
    public static double BondYield(DateOnly settlement, DateOnly maturity, double couponRatePercent, double cleanPrice,
        double redemption = 100, int couponsPerYear = 2, DayCountConvention dayCount = DayCountConvention.Thirty360US)
    {
        Guard.PositiveFinite(cleanPrice, "the price", nameof(cleanPrice));
        var s = Schedule(settlement, maturity, couponRatePercent, redemption, couponsPerYear, dayCount);
        return SolveYield(y => CleanPrice(s, y, couponsPerYear) - cleanPrice, couponsPerYear);
    }

    /// <summary>Price on a coupon date with the given number of remaining coupon periods (no accrued interest).</summary>
    public static double BondPrice(double couponRatePercent, double yieldPercent, int periods, int couponsPerYear = 2, double redemption = 100)
    {
        CheckFrequency(couponsPerYear);
        if (periods < 1) throw new ArgumentException("the number of coupon periods must be at least 1", nameof(periods));
        Guard.Finite(couponRatePercent, "the coupon rate", nameof(couponRatePercent));
        Guard.Finite(yieldPercent, "the yield", nameof(yieldPercent));
        double c = couponRatePercent / couponsPerYear;
        double y = yieldPercent / (100 * couponsPerYear);
        if (!(y > -1)) throw new ArgumentException("the yield per period must be greater than -100%", nameof(yieldPercent));
        if (y == 0) return redemption + c * periods;
        double l = Num.Log1p(y);
        double v = Math.Exp(-periods * l);
        return c * (-Num.Expm1(-periods * l)) / y + redemption * v;
    }

    /// <summary>Yield (percent) of a bond priced on a coupon date with the given number of remaining periods.</summary>
    public static double BondYield(double couponRatePercent, double price, int periods, int couponsPerYear = 2, double redemption = 100)
    {
        Guard.PositiveFinite(price, "the price", nameof(price));
        CheckFrequency(couponsPerYear);
        return SolveYield(y => BondPrice(couponRatePercent, y, periods, couponsPerYear, redemption) - price, couponsPerYear);
    }

    private static double SolveYield(Func<double, double> f, int couponsPerYear)
    {
        double lo = -99.0 * couponsPerYear, hi = 1e4;
        double flo = f(lo), fhi = f(hi);
        if (Math.Sign(flo) == Math.Sign(fhi)) throw new ArgumentException("no yield between -99% and 10000% per period gives this price");
        return Roots.Brent(f, lo, hi, flo, fhi, 1e-13);
    }

    private sealed record BondSchedule(double Coupon, double Redemption, int N, double A, double E, double Dsc, double Accrued, DateOnly Pcd, DateOnly Ncd);

    private static BondSchedule Schedule(DateOnly settlement, DateOnly maturity, double couponRatePercent, double redemption, int couponsPerYear, DayCountConvention dayCount)
    {
        CheckFrequency(couponsPerYear);
        Guard.NonNegative(couponRatePercent, "the coupon rate", nameof(couponRatePercent));
        Guard.PositiveFinite(redemption, "the redemption value", nameof(redemption));
        if (settlement >= maturity) throw new ArgumentException("the settlement date must be before the maturity date", nameof(settlement));
        int months = 12 / couponsPerYear;
        bool eom = maturity.Day == DateTime.DaysInMonth(maturity.Year, maturity.Month);
        int k = 0;
        DateOnly ncd = maturity, pcd;
        while (true)
        {
            pcd = AddMonthsEom(maturity, -(k + 1) * months, eom);
            if (pcd <= settlement) break;
            ncd = pcd;
            k++;
        }
        int n = k + 1;
        double e = dayCount == DayCountConvention.Actual ? ncd.DayNumber - pcd.DayNumber : 360.0 / couponsPerYear;
        double a = Finance.DaysBetweenDates(pcd, settlement, dayCount);
        double dsc = dayCount == DayCountConvention.Actual ? ncd.DayNumber - settlement.DayNumber : e - a;
        double coupon = 100 * couponRatePercent / 100 / couponsPerYear;
        return new BondSchedule(coupon, redemption, n, a, e, dsc, coupon * a / e, pcd, ncd);
    }

    private static double CleanPrice(BondSchedule s, double yieldPercent, int couponsPerYear)
    {
        double y = yieldPercent / (100 * couponsPerYear);
        if (!(y > -1)) return double.PositiveInfinity;
        double frac = s.Dsc / s.E;
        if (s.N == 1) return (s.Redemption + s.Coupon) / (1 + frac * y) - s.Coupon * s.A / s.E;
        double l = Num.Log1p(y);
        double price = s.Redemption * Math.Exp(-(s.N - 1 + frac) * l);
        for (int k = 1; k <= s.N; k++) price += s.Coupon * Math.Exp(-(k - 1 + frac) * l);
        return price - s.Coupon * s.A / s.E;
    }
}

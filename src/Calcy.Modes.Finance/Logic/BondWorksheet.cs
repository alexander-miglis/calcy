using System.Globalization;
using Calcy.Statistics;

namespace Calcy.Modes.Finance.Logic;

/// <summary>Which bond value to work out.</summary>
public enum BondTarget
{
    /// <summary>The clean price from the yield.</summary>
    Price,

    /// <summary>The yield to maturity from the clean price.</summary>
    Yield,
}

/// <summary>The text typed into the Bonds tab.</summary>
public sealed class BondInputs
{
    /// <summary>Settlement date (when you buy).</summary>
    public string Settlement { get; set; } = "2026-10-13";

    /// <summary>Maturity date (when the bond is repaid).</summary>
    public string Maturity { get; set; } = "2036-06-15";

    /// <summary>Annual coupon rate in percent.</summary>
    public string Coupon { get; set; } = "4";

    /// <summary>Yield to maturity in percent (blank to solve it).</summary>
    public string Yield { get; set; } = "5";

    /// <summary>Clean price per 100 of face value (blank to solve it).</summary>
    public string Price { get; set; } = "";

    /// <summary>Redemption value per 100 of face value.</summary>
    public string Redemption { get; set; } = "100";

    /// <summary>Coupons per year: 1, 2, 4 or 12.</summary>
    public int Frequency { get; set; } = 2;

    /// <summary>Day count: actual or 30/360 (US).</summary>
    public DayCountConvention DayCount { get; set; } = DayCountConvention.Thirty360US;
}

/// <summary>A priced bond (per 100 of face value).</summary>
public sealed record BondAnswer(
    BondTarget Solved,
    DateOnly Settlement,
    DateOnly Maturity,
    double Coupon,
    double Yield,
    double CleanPrice,
    double Accrued,
    double DirtyPrice,
    int CouponsRemaining,
    DateOnly PreviousCoupon,
    DateOnly NextCoupon,
    double Redemption,
    int Frequency,
    DayCountConvention DayCount);

/// <summary>Bond price from yield, or yield from price, with accrued interest (<see cref="Bonds"/>).</summary>
public static class BondWorksheet
{
    /// <summary>Short name of a day count.</summary>
    public static string DayCountName(DayCountConvention d) => d switch
    {
        DayCountConvention.Actual => "ACT",
        DayCountConvention.Thirty360US => "30/360",
        _ => "30E/360",
    };

    /// <summary>Which value to solve: the requested one, else the blank one, else the last solved one; null with a message otherwise.</summary>
    public static BondTarget? ChooseTarget(BondInputs inputs, BondTarget? requested, BondTarget? lastSolved, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        problem = null;
        if (requested is { } r) return r;
        bool noPrice = string.IsNullOrWhiteSpace(inputs.Price), noYield = string.IsNullOrWhiteSpace(inputs.Yield);
        if (noPrice && noYield)
        {
            problem = "Type either the yield or the price; the other one is worked out.";
            return null;
        }
        if (noPrice) return BondTarget.Price;
        if (noYield) return BondTarget.Yield;
        if (lastSolved is { } last) return last;
        problem = "Both the yield and the price are filled in. Clear the one you want to work out, or press SOLVE next to it.";
        return null;
    }

    /// <summary>Reads the fields and prices the bond.</summary>
    public static Answer<BondAnswer> Solve(BondInputs inputs, ValueReader reader, BondTarget? requested = null, BondTarget? lastSolved = null, DateOnly? today = null, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(reader);
        var errors = new Dictionary<string, string>();
        var target = ChooseTarget(inputs, requested, lastSolved, out var problem);
        if (!DateCalculator.TryParse(inputs.Settlement, out var settle, out var e1, today, culture)) errors["settlement"] = e1!;
        if (!DateCalculator.TryParse(inputs.Maturity, out var maturity, out var e2, today, culture)) errors["maturity"] = e2!;
        var coupon = InterestTools.Required(reader, inputs.Coupon, new FieldRule("the coupon", IsPercent: true, Min: 0, Max: 1000), "coupon", errors, "type the yearly coupon in percent, for example 4");
        var redemption = InterestTools.Required(reader, inputs.Redemption, FieldRule.PositiveAmount("the redemption value"), "redemption", errors, "type 100 unless the bond repays more or less than its face value");
        double y = double.NaN, price = double.NaN;
        if (target == BondTarget.Price)
            y = InterestTools.Required(reader, inputs.Yield, new FieldRule("the yield", IsPercent: true, Min: -99, Max: 10_000), "yield", errors, "type the yield in percent");
        if (target == BondTarget.Yield)
            price = InterestTools.Required(reader, inputs.Price, FieldRule.PositiveAmount("the price"), "price", errors, "type the clean price per 100");
        if (errors.Count == 0 && settle >= maturity) errors["maturity"] = "The maturity date must be after the settlement date.";
        if (target is null) return new(null, problem, errors);
        if (errors.Count > 0) return new(null, InterestTools.Fix(errors), errors);
        try
        {
            if (target == BondTarget.Yield)
                y = Bonds.BondYield(settle, maturity, coupon, price, redemption, inputs.Frequency, inputs.DayCount);
            var p = Bonds.BondPrice(settle, maturity, coupon, y, redemption, inputs.Frequency, inputs.DayCount);
            return new(new BondAnswer(target.Value, settle, maturity, coupon, y, p.CleanPrice, p.AccruedInterest, p.DirtyPrice,
                p.CouponsRemaining, p.PreviousCouponDate, p.NextCouponDate, redemption, inputs.Frequency, inputs.DayCount), null, errors);
        }
        catch (ArgumentException ex)
        {
            return new(null, ValueReader.Sentence(ex.Message), errors);
        }
    }

    /// <summary>A price per 100 with four decimals ("98.1234").</summary>
    public static string PriceText(double price) => double.IsFinite(price) ? price.ToString("0.0000", CultureInfo.InvariantCulture) : Money.NotANumber;
}

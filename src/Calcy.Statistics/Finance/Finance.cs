using Calcy.Statistics.Internal;

namespace Calcy.Statistics;

/// <summary>Day count conventions.</summary>
public enum DayCountConvention
{
    /// <summary>Actual number of days (TI dbd, Casio 365 day mode).</summary>
    Actual,
    /// <summary>30/360 US (NASD, TI BA II Plus 360): day 31 becomes 30; an end day of 31 becomes 30 when the start day is 30 or 31.</summary>
    Thirty360US,
    /// <summary>30/360 European: any day 31 becomes 30.</summary>
    Thirty360European,
}

/// <summary>Simple interest result: Interest = P r t and FutureValue = P + Interest (natural signs).</summary>
public sealed record SimpleInterestResult(double Interest, double FutureValue, double Days, double DaysInYear);

/// <summary>Interest rate conversion, day counts, simple and compound interest, break-even and pricing helpers.</summary>
public static class Finance
{
    /// <summary>TI Eff(nominal, C/Y): effective annual rate (percent) of a nominal rate compounded C/Y times a year (infinity = continuous).</summary>
    public static double EffectiveRate(double nominalPercent, double compoundingsPerYear)
    {
        Guard.Finite(nominalPercent, "the nominal rate", nameof(nominalPercent));
        Guard.Positive(compoundingsPerYear, "the number of compounding periods", nameof(compoundingsPerYear));
        if (double.IsPositiveInfinity(compoundingsPerYear)) return 100 * Num.Expm1(nominalPercent / 100);
        double j = nominalPercent / (100 * compoundingsPerYear);
        if (!(j > -1)) throw new ArgumentException("the nominal rate per period must be greater than -100%", nameof(nominalPercent));
        return 100 * Num.Expm1(compoundingsPerYear * Num.Log1p(j));
    }

    /// <summary>TI Nom(effective, C/Y): nominal annual rate (percent) compounded C/Y times a year (infinity = continuous).</summary>
    public static double NominalRate(double effectivePercent, double compoundingsPerYear)
    {
        Guard.Finite(effectivePercent, "the effective rate", nameof(effectivePercent));
        Guard.Positive(compoundingsPerYear, "the number of compounding periods", nameof(compoundingsPerYear));
        if (!(effectivePercent > -100)) throw new ArgumentException("the effective rate must be greater than -100%", nameof(effectivePercent));
        double l = Num.Log1p(effectivePercent / 100);
        if (double.IsPositiveInfinity(compoundingsPerYear)) return 100 * l;
        return 100 * compoundingsPerYear * Num.Expm1(l / compoundingsPerYear);
    }

    /// <summary>Number of days from start to end (negative when end is earlier) under the given convention.</summary>
    public static int DaysBetweenDates(DateOnly start, DateOnly end, DayCountConvention convention = DayCountConvention.Actual)
    {
        if (convention == DayCountConvention.Actual) return end.DayNumber - start.DayNumber;
        int d1 = start.Day, d2 = end.Day;
        if (convention == DayCountConvention.Thirty360US)
        {
            if (d1 == 31) d1 = 30;
            if (d2 == 31 && d1 >= 30) d2 = 30;
        }
        else if (convention == DayCountConvention.Thirty360European)
        {
            if (d1 == 31) d1 = 30;
            if (d2 == 31) d2 = 30;
        }
        else
        {
            throw new ArgumentException("unknown day count convention", nameof(convention));
        }
        return 360 * (end.Year - start.Year) + 30 * (end.Month - start.Month) + (d2 - d1);
    }

    /// <summary>
    /// TI dbd(date1, date2) with dates typed in the TI formats MM.DDYY (US) or DDMM.YY (Europe). Two digit years 50 to
    /// 99 are 1950 to 1999 and 00 to 49 are 2000 to 2049, as on the TI-84.
    /// </summary>
    public static int DaysBetweenDates(double tiDate1, double tiDate2) => DaysBetweenDates(ParseTiDate(tiDate1), ParseTiDate(tiDate2));

    /// <summary>Parses a TI-84 date number (MM.DDYY when the integer part is a month 1 to 12, otherwise DDMM.YY).</summary>
    public static DateOnly ParseTiDate(double value)
    {
        if (!(value > 0) || !double.IsFinite(value)) throw new ArgumentException("the date must be a positive number in MM.DDYY or DDMM.YY form", nameof(value));
        int day, month, yy;
        double ip = Math.Floor(value);
        if (ip < 13)
        {
            month = (int)ip;
            int rest = (int)Math.Round((value - ip) * 10000);
            day = rest / 100;
            yy = rest % 100;
        }
        else
        {
            int ddmm = (int)ip;
            day = ddmm / 100;
            month = ddmm % 100;
            yy = (int)Math.Round((value - ip) * 100);
        }
        int year = yy >= 50 ? 1900 + yy : 2000 + yy;
        if (month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
            throw new ArgumentException($"{value} is not a valid MM.DDYY or DDMM.YY date", nameof(value));
        return new DateOnly(year, month, day);
    }

    /// <summary>Simple interest on a principal for a number of days (Casio SINT, 365 or 360 day year).</summary>
    public static SimpleInterestResult SimpleInterest(double principal, double annualRatePercent, double days, double daysInYear = 365)
    {
        Guard.Finite(principal, "the principal", nameof(principal));
        Guard.Finite(annualRatePercent, "the interest rate", nameof(annualRatePercent));
        Guard.Finite(days, "the number of days", nameof(days));
        Guard.PositiveFinite(daysInYear, "the number of days in the year", nameof(daysInYear));
        double interest = principal * annualRatePercent / 100 * days / daysInYear;
        return new SimpleInterestResult(interest, principal + interest, days, daysInYear);
    }

    /// <summary>Future value of a principal at a nominal annual rate compounded m times a year (infinity = continuous).</summary>
    public static double CompoundInterest(double principal, double annualRatePercent, double years, double compoundingsPerYear = 1)
    {
        Guard.Finite(principal, "the principal", nameof(principal));
        Guard.Finite(annualRatePercent, "the interest rate", nameof(annualRatePercent));
        Guard.Finite(years, "the time in years", nameof(years));
        Guard.Positive(compoundingsPerYear, "the number of compounding periods", nameof(compoundingsPerYear));
        if (double.IsPositiveInfinity(compoundingsPerYear)) return principal * Math.Exp(annualRatePercent / 100 * years);
        double j = annualRatePercent / (100 * compoundingsPerYear);
        if (!(j > -1)) throw new ArgumentException("the rate per period must be greater than -100%", nameof(annualRatePercent));
        return principal * Math.Exp(compoundingsPerYear * years * Num.Log1p(j));
    }

    /// <summary>Break-even quantity (fixed costs + target profit) / (price - variable cost per unit).</summary>
    public static double BreakEvenQuantity(double fixedCosts, double pricePerUnit, double variableCostPerUnit, double targetProfit = 0)
    {
        Guard.Finite(fixedCosts, "the fixed costs", nameof(fixedCosts));
        Guard.Finite(targetProfit, "the target profit", nameof(targetProfit));
        double margin = pricePerUnit - variableCostPerUnit;
        if (!(margin > 0)) throw new ArgumentException("the price per unit must exceed the variable cost per unit", nameof(pricePerUnit));
        return (fixedCosts + targetProfit) / margin;
    }

    /// <summary>Break-even unit price for a given quantity: variable cost + (fixed costs + target profit) / quantity.</summary>
    public static double BreakEvenPrice(double fixedCosts, double quantity, double variableCostPerUnit, double targetProfit = 0)
    {
        Guard.PositiveFinite(quantity, "the quantity", nameof(quantity));
        Guard.Finite(variableCostPerUnit, "the variable cost", nameof(variableCostPerUnit));
        return variableCostPerUnit + (fixedCosts + targetProfit) / quantity;
    }

    /// <summary>Profit = quantity (price - variable cost) - fixed costs.</summary>
    public static double Profit(double fixedCosts, double pricePerUnit, double variableCostPerUnit, double quantity)
        => quantity * (pricePerUnit - variableCostPerUnit) - fixedCosts;

    /// <summary>Selling price from cost and gross margin percent: cost / (1 - margin/100).</summary>
    public static double SellingPrice(double cost, double marginPercent)
    {
        Guard.Finite(cost, "the cost", nameof(cost));
        if (!(marginPercent < 100)) throw new ArgumentException("the margin must be less than 100%", nameof(marginPercent));
        return cost / (1 - marginPercent / 100);
    }

    /// <summary>Cost from selling price and gross margin percent: sell (1 - margin/100).</summary>
    public static double CostFromSellingPrice(double sellingPrice, double marginPercent)
    {
        Guard.Finite(sellingPrice, "the selling price", nameof(sellingPrice));
        Guard.Finite(marginPercent, "the margin", nameof(marginPercent));
        return sellingPrice * (1 - marginPercent / 100);
    }

    /// <summary>Gross margin percent from cost and selling price: 100 (1 - cost / sell).</summary>
    public static double Margin(double cost, double sellingPrice)
    {
        if (sellingPrice == 0) throw new ArgumentException("the selling price must not be zero", nameof(sellingPrice));
        return 100 * (1 - cost / sellingPrice);
    }

    /// <summary>Markup percent on cost: 100 (sell / cost - 1).</summary>
    public static double Markup(double cost, double sellingPrice)
    {
        if (cost == 0) throw new ArgumentException("the cost must not be zero", nameof(cost));
        return 100 * (sellingPrice / cost - 1);
    }

    /// <summary>Selling price from cost and markup percent: cost (1 + markup/100).</summary>
    public static double SellingPriceFromMarkup(double cost, double markupPercent) => cost * (1 + markupPercent / 100);
}

/// <summary>One year of a depreciation schedule.</summary>
public sealed record DepreciationRow(int Year, double Depreciation, double AccumulatedDepreciation, double BookValue);

/// <summary>Depreciation schedules (straight line, sum of the years' digits, declining balance).</summary>
public static class Depreciation
{
    private static void Check(double cost, double salvage, int life)
    {
        Guard.PositiveFinite(cost, "the cost", nameof(cost));
        if (!(salvage >= 0 && salvage <= cost)) throw new ArgumentException("the salvage value must be between 0 and the cost", nameof(salvage));
        if (life < 1) throw new ArgumentException("the useful life must be at least one year", nameof(life));
    }

    private static IReadOnlyList<DepreciationRow> Build(double cost, int life, Func<int, double, double> depreciationForYear)
    {
        var rows = new List<DepreciationRow>(life);
        double book = cost, acc = 0;
        for (int y = 1; y <= life; y++)
        {
            double d = depreciationForYear(y, book);
            acc += d;
            book = cost - acc;
            rows.Add(new DepreciationRow(y, d, acc, book));
        }
        return rows;
    }

    /// <summary>Straight line: (cost - salvage) / life every year.</summary>
    public static IReadOnlyList<DepreciationRow> StraightLine(double cost, double salvage, int life)
    {
        Check(cost, salvage, life);
        double d = (cost - salvage) / life;
        return Build(cost, life, (_, _) => d);
    }

    /// <summary>Sum of the years' digits: (cost - salvage) (life - y + 1) / (life (life + 1) / 2).</summary>
    public static IReadOnlyList<DepreciationRow> SumOfYearsDigits(double cost, double salvage, int life)
    {
        Check(cost, salvage, life);
        double sum = life * (life + 1.0) / 2;
        return Build(cost, life, (y, _) => (cost - salvage) * (life - y + 1) / sum);
    }

    /// <summary>
    /// Declining balance at rate factor / life of the current book value, never going below salvage (factor 2 is
    /// double declining balance). With switchToStraightLine the straight line amount on the remaining value is used
    /// whenever it is larger, which fully depreciates the asset to salvage by the end of its life.
    /// </summary>
    public static IReadOnlyList<DepreciationRow> DecliningBalance(double cost, double salvage, int life, double factor = 2, bool switchToStraightLine = false)
    {
        Check(cost, salvage, life);
        Guard.PositiveFinite(factor, "the declining balance factor", nameof(factor));
        double rate = Math.Min(1.0, factor / life);
        return Build(cost, life, (y, book) =>
        {
            double remaining = Math.Max(0, book - salvage);
            double d = Math.Min(book * rate, remaining);
            if (switchToStraightLine) d = Math.Max(d, remaining / (life - y + 1));
            return Math.Min(d, remaining);
        });
    }
}

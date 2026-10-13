using System.Globalization;

namespace Calcy.Modes.Finance.Logic;

/// <summary>Plain words for payment periods and loan lengths ("month", "30 years", "5 years 6 months").</summary>
public static class Periods
{
    /// <summary>The name of one period for a number of periods per year: 12 is "month", 4 is "quarter", 1 is "year".</summary>
    public static string Name(double perYear) => perYear switch
    {
        1 => "year",
        2 => "half year",
        4 => "quarter",
        6 => "two months",
        12 => "month",
        24 => "half month",
        26 => "two weeks",
        52 => "week",
        365 => "day",
        _ => "period",
    };

    /// <summary>"1 year", "2 years".</summary>
    public static string Count(double n, string unit)
    {
        var text = Money.Number(n, 2);
        return text + " " + (text == "1" ? unit : Plural(unit));
    }

    private static string Plural(string unit) => unit switch
    {
        "half year" => "half years",
        "two months" => "two-month periods",
        "two weeks" => "two-week periods",
        "half month" => "half months",
        _ => unit + "s",
    };

    /// <summary>How long a number of periods lasts: "30 years", "5 years 6 months", "18 weeks".</summary>
    public static string Duration(double n, double perYear)
    {
        if (!double.IsFinite(n) || !double.IsFinite(perYear) || perYear <= 0) return Count(n, "period");
        double years = n / perYear;
        if (IsWhole(years)) return Count(years, "year");
        if (perYear == 12 && IsWhole(n))
        {
            int total = (int)Math.Round(n);
            int y = total / 12, m = total % 12;
            return y == 0 ? Count(m, "month") : Count(y, "year") + " " + Count(m, "month");
        }
        if (IsWhole(n) && Name(perYear) != "period" && years < 1) return Count(n, Name(perYear));
        return "about " + Count(Math.Round(years, 2), "year");
    }

    private static bool IsWhole(double v) => Math.Abs(v - Math.Round(v)) < 1e-9;

    /// <summary>Formats a whole count with grouping ("1,200"), for labels.</summary>
    public static string Whole(int n) => n.ToString("#,##0", CultureInfo.InvariantCulture);
}

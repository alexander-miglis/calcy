using System.Globalization;
using Calcy.Statistics;
using FinanceMath = Calcy.Statistics.Finance;

namespace Calcy.Modes.Finance.Logic;

/// <summary>The text typed into the Dates tab.</summary>
public sealed class DateInputs
{
    /// <summary>First date of the day count.</summary>
    public string Start { get; set; } = "2026-01-15";

    /// <summary>Second date of the day count.</summary>
    public string End { get; set; } = "2026-12-31";

    /// <summary>Date to add days to.</summary>
    public string From { get; set; } = "2026-10-13";

    /// <summary>Days to add (negative to go back).</summary>
    public string Days { get; set; } = "90";
}

/// <summary>Days between two dates under the three day count conventions.</summary>
public sealed record DayCount(DateOnly Start, DateOnly End, int Actual, int Thirty360US, int Thirty360European)
{
    /// <summary>The actual days as a fraction of a 365 day year.</summary>
    public double Years365 => Actual / 365.0;
}

/// <summary>A date plus a number of days.</summary>
public sealed record DateShift(DateOnly From, int Days, DateOnly Result);

/// <summary>Date parsing, day counts (actual and 30/360) and date arithmetic for the Dates tab.</summary>
public static class DateCalculator
{
    /// <summary>The date format used on screen and in cards.</summary>
    public const string Format = "yyyy-MM-dd";

    private static readonly string[] IsoFormats = ["yyyy-MM-dd", "yyyy-M-d", "yyyy/MM/dd", "yyyy/M/d", "yyyy.MM.dd", "yyyyMMdd"];

    /// <summary>Formats a date for display: "2026-10-13 (Tue)".</summary>
    public static string Show(DateOnly d) =>
        d.ToString(Format, CultureInfo.InvariantCulture) + " (" + CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedDayName(d.DayOfWeek) + ")";

    /// <summary>
    /// Reads a date: "today", ISO (2026-10-13) or the culture's short date. Returns false with a plain message otherwise.
    /// </summary>
    public static bool TryParse(string? text, out DateOnly date, out string? error, DateOnly? today = null, CultureInfo? culture = null)
    {
        error = null;
        date = default;
        var t = (text ?? "").Trim();
        if (t.Length == 0)
        {
            error = "Type a date like 2026-10-13.";
            return false;
        }
        var now = today ?? DateOnly.FromDateTime(DateTime.Today);
        switch (t.ToLowerInvariant())
        {
            case "today": date = now; return true;
            case "tomorrow": date = now.AddDays(1); return true;
            case "yesterday": date = now.AddDays(-1); return true;
        }
        if (DateOnly.TryParseExact(t, IsoFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
        culture ??= CultureInfo.CurrentCulture;
        if (DateOnly.TryParse(t, culture, DateTimeStyles.None, out date) && date.Year >= 1) return true;
        error = $"'{t}' is not a date: type it like 2026-10-13.";
        return false;
    }

    /// <summary>Days between two dates: actual, 30/360 US and 30/360 European.</summary>
    public static DayCount Between(DateOnly start, DateOnly end) => new(start, end,
        FinanceMath.DaysBetweenDates(start, end, DayCountConvention.Actual),
        FinanceMath.DaysBetweenDates(start, end, DayCountConvention.Thirty360US),
        FinanceMath.DaysBetweenDates(start, end, DayCountConvention.Thirty360European));

    /// <summary>Reads both dates of the day count.</summary>
    public static Answer<DayCount> Count(DateInputs inputs, DateOnly? today = null, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var errors = new Dictionary<string, string>();
        if (!TryParse(inputs.Start, out var a, out var e1, today, culture)) errors["start"] = e1!;
        if (!TryParse(inputs.End, out var b, out var e2, today, culture)) errors["end"] = e2!;
        if (errors.Count > 0) return new(null, InterestTools.Fix(errors), errors);
        return new(Between(a, b), null, errors);
    }

    /// <summary>Adds (or with a negative number, subtracts) days.</summary>
    public static Answer<DateShift> Add(DateInputs inputs, ValueReader reader, DateOnly? today = null, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(reader);
        var errors = new Dictionary<string, string>();
        if (!TryParse(inputs.From, out var from, out var e1, today, culture)) errors["from"] = e1!;
        var days = InterestTools.Required(reader, inputs.Days, FieldRule.Whole("the days", -3_000_000, 3_000_000), "days", errors, "type how many days to add (negative to go back)");
        if (errors.Count > 0) return new(null, InterestTools.Fix(errors), errors);
        int n = (int)days;
        long target = (long)from.DayNumber + n;
        if (target < DateOnly.MinValue.DayNumber || target > DateOnly.MaxValue.DayNumber)
            return new(null, "That date is outside the calendar (years 1 to 9999).", errors);
        return new(new DateShift(from, n, DateOnly.FromDayNumber((int)target)), null, errors);
    }

    /// <summary>The TI-84 date number MM.DDYY for dates from 1950 to 2049 (what dbd takes), or null outside that range.</summary>
    public static string? TiDate(DateOnly d)
    {
        if (d.Year is < 1950 or > 2049) return null;
        return d.Month.ToString(CultureInfo.InvariantCulture) + "." + d.Day.ToString("00", CultureInfo.InvariantCulture) + (d.Year % 100).ToString("00", CultureInfo.InvariantCulture);
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Calcy.Units;

/// <summary>A calendar span split into whole years, months and days (always non-negative parts with a sign).</summary>
public readonly record struct DateSpan(int Years, int Months, int Days, bool IsNegative)
{
    /// <summary>"1 y 2 mo 3 d" (with a leading minus for a negative span).</summary>
    public override string ToString() => (IsNegative ? "-" : "") + $"{Years} y {Months} mo {Days} d";
}

/// <summary>Units for adding to a date.</summary>
public enum DateStep
{
    /// <summary>Calendar days.</summary>
    Days,

    /// <summary>Weeks of 7 days.</summary>
    Weeks,

    /// <summary>Calendar months (the day is clamped to the end of a shorter month).</summary>
    Months,

    /// <summary>Calendar years (29 February becomes 28 February in a common year).</summary>
    Years,
}

/// <summary>
/// Date and time-span arithmetic, culture-invariant and on the proleptic Gregorian calendar of <see cref="DateOnly"/>:
/// days between dates, weeks and days, years months and days, weekdays, adding with month-end handling, ISO weeks,
/// and h:mm:ss durations.
/// </summary>
public static class DateTools
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "yyyy-M-d", "yyyy/MM/dd", "yyyy/M/d", "yyyyMMdd", "yyyy.MM.dd", "d MMM yyyy", "d MMMM yyyy", "MMM d yyyy", "MMMM d yyyy"];

    /// <summary>Trims, folds white space (including no-break and thin spaces) and turns the minus sign into a hyphen.</summary>
    private static string Clean(string text) =>
        string.Join(' ', text.Replace('−', '-').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Signed number of days from <paramref name="from"/> to <paramref name="to"/> (negative when <paramref name="to"/> is earlier).</summary>
    public static int DaysBetween(DateOnly from, DateOnly to) => to.DayNumber - from.DayNumber;

    /// <summary>A signed day count as whole weeks and remaining days, both carrying the sign (-10 gives -1 w -3 d).</summary>
    public static (int Weeks, int Days) WeeksAndDays(int days) => (days / 7, days % 7);

    /// <summary>
    /// Mon to Fri days in the half-open range [from, to): the start date counts, the end date does not, so the count
    /// never exceeds <see cref="DaysBetween"/>. Negative when <paramref name="to"/> is earlier (then [to, from) is counted).
    /// </summary>
    public static int Weekdays(DateOnly from, DateOnly to)
    {
        if (to < from) return -Weekdays(to, from);
        int days = DaysBetween(from, to);
        int full = days / 7;
        int count = full * 5;
        // DayNumber 0 (0001-01-01) is a Monday, so DayNumber % 7 gives 0 = Monday .. 6 = Sunday.
        int start = from.DayNumber % 7;
        for (int k = 0; k < days % 7; k++)
            if ((start + k) % 7 < 5) count++;
        return count;
    }

    /// <summary>
    /// The span from <paramref name="from"/> to <paramref name="to"/> in whole years, months and days, so that adding the years
    /// and months (with month-end clamping) and then the days to the earlier date gives the later one.
    /// </summary>
    public static DateSpan YearsMonthsDays(DateOnly from, DateOnly to)
    {
        bool negative = to < from;
        if (negative) (from, to) = (to, from);
        int months = (to.Year - from.Year) * 12 + to.Month - from.Month;
        if (months > 0 && from.AddMonths(months) > to) months--;
        var anchor = from.AddMonths(months);
        int days = DaysBetween(anchor, to);
        return new DateSpan(months / 12, months % 12, days, negative);
    }

    /// <summary>Adds (or with a negative amount, subtracts) days, weeks, months or years. Months and years clamp to the month end.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The result is outside 0001-01-01 to 9999-12-31.</exception>
    public static DateOnly Add(DateOnly date, int amount, DateStep step) => step switch
    {
        DateStep.Weeks => date.AddDays(checked(amount * 7)),
        DateStep.Months => date.AddMonths(amount),
        DateStep.Years => date.AddYears(amount),
        _ => date.AddDays(amount),
    };

    /// <summary>Adds years and months first (as one month count, so the day is clamped once) and then days.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The result is outside the supported range.</exception>
    public static DateOnly Add(DateOnly date, int years, int months, int days) =>
        date.AddMonths(checked(years * 12 + months)).AddDays(days);

    /// <summary>Adds safely; returns false when the result would be outside the supported range.</summary>
    public static bool TryAdd(DateOnly date, int amount, DateStep step, out DateOnly result)
    {
        try
        {
            result = Add(date, amount, step);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
        {
            result = default;
            return false;
        }
    }

    /// <summary>The ISO 8601 week-numbering year and week (1 to 53). 2027-01-01 is in week 53 of 2026.</summary>
    public static (int Year, int Week) IsoWeek(DateOnly date)
    {
        var dt = date.ToDateTime(TimeOnly.MinValue);
        return (ISOWeek.GetYear(dt), ISOWeek.GetWeekOfYear(dt));
    }

    /// <summary>"2026-W42".</summary>
    public static string IsoWeekText(DateOnly date)
    {
        var (y, w) = IsoWeek(date);
        return $"{y:D4}-W{w:D2}";
    }

    /// <summary>English day name, for example "Tuesday" (culture-invariant).</summary>
    public static string DayName(DateOnly date) => CultureInfo.InvariantCulture.DateTimeFormat.GetDayName(date.DayOfWeek);

    /// <summary>Day of the year, 1 to 366.</summary>
    public static int DayOfYear(DateOnly date) => date.DayOfYear;

    /// <summary>True for Gregorian leap years (divisible by 4, except centuries not divisible by 400).</summary>
    public static bool IsLeapYear(int year) => DateTime.IsLeapYear(year);

    /// <summary>ISO 8601 text "yyyy-MM-dd".</summary>
    public static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads a date: ISO forms (2026-10-13, 2026/10/13, 20261013), English month names (13 Oct 2026, October 13 2026),
    /// or "today" relative to <paramref name="today"/>.
    /// </summary>
    public static bool TryParseDate(string? text, out DateOnly date, DateOnly? today = null)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = Clean(text).Replace(",", "", StringComparison.Ordinal);
        if (s.Equals("today", StringComparison.OrdinalIgnoreCase))
        {
            date = today ?? DateOnly.FromDateTime(DateTime.Today);
            return true;
        }
        return DateOnly.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date);
    }

    // ---- Durations ----

    /// <summary>
    /// Reads a duration: "h:mm" or "h:mm:ss(.fff)" (hours may exceed 24, a leading minus is allowed), "d.hh:mm:ss",
    /// or units such as "1h 30m 15s", "2 d 3 h", "90 min", "1.5 h".
    /// </summary>
    public static bool TryParseDuration(string? text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = Clean(text);
        bool negative = false;
        if (s.StartsWith('-') || s.StartsWith('+'))
        {
            negative = s[0] == '-';
            s = s[1..].TrimStart();
        }
        if (s.Length == 0) return false;
        bool ok = s.Contains(':') ? TryParseClock(s, out duration) : TryParseUnits(s, out duration);
        if (!ok) return false;
        if (negative) duration = -duration;
        return true;
    }

    private static bool TryParseClock(string s, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        long days = 0;
        int dot = s.IndexOf('.');
        int colon = s.IndexOf(':');
        if (dot >= 0 && dot < colon)
        {
            if (!long.TryParse(s[..dot], NumberStyles.None, CultureInfo.InvariantCulture, out days)) return false;
            s = s[(dot + 1)..];
        }
        var parts = s.Split(':');
        if (parts.Length is < 2 or > 3) return false;
        if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long hours)) return false;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes) || minutes > 59) return false;
        decimal seconds = 0;
        if (parts.Length == 3 &&
            (!decimal.TryParse(parts[2], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out seconds) || seconds >= 60))
            return false;
        try
        {
            decimal total = ((days * 24 + hours) * 60 + minutes) * 60m + seconds;
            duration = TimeSpan.FromTicks((long)decimal.Round(total * TimeSpan.TicksPerSecond));
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TryParseUnits(string s, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        // In a duration "30m" means minutes, not metres. A plain number would be ambiguous, so a unit is required.
        s = System.Text.RegularExpressions.Regex.Replace(s, @"(\d)\s*m(?![a-zA-Zµ])", "$1 min");
        var catalog = UnitCatalog.Default;
        if (!catalog.TryParse(s, out var query, out _) || query.Dimension != new Dimension(Time: 1)) return false;
        try
        {
            var result = catalog.Evaluate(query, [catalog["s"]]);
            decimal seconds = (decimal)result.Value.ToDouble();
            duration = TimeSpan.FromTicks((long)decimal.Round(seconds * TimeSpan.TicksPerSecond));
            return true;
        }
        catch (Exception ex) when (ex is OverflowException or ConversionException)
        {
            return false;
        }
    }

    /// <summary>"h:mm:ss" with total hours (26:30:00), a minus for negative spans and up to three decimals of a second when present.</summary>
    public static string FormatDuration(TimeSpan d)
    {
        bool negative = d < TimeSpan.Zero;
        var a = negative ? d.Negate() : d;
        long hours = (long)Math.Floor(a.TotalHours);
        string frac = a.Milliseconds == 0 && a.Ticks % TimeSpan.TicksPerMillisecond == 0 ? "" : "." + (a.Ticks % TimeSpan.TicksPerSecond / 10000).ToString("D3", CultureInfo.InvariantCulture).TrimEnd('0');
        if (frac == ".") frac = "";
        return $"{(negative ? "-" : "")}{hours.ToString(CultureInfo.InvariantCulture)}:{a.Minutes:D2}:{a.Seconds:D2}{frac}";
    }

    /// <summary>Words for a duration, for example "1 d 2 h 30 min 5 s" (or "0 s").</summary>
    public static string DescribeDuration(TimeSpan d)
    {
        bool negative = d < TimeSpan.Zero;
        var a = negative ? d.Negate() : d;
        var parts = new List<string>();
        if (a.Days != 0) parts.Add($"{a.Days.ToString(CultureInfo.InvariantCulture)} d");
        if (a.Hours != 0) parts.Add($"{a.Hours} h");
        if (a.Minutes != 0) parts.Add($"{a.Minutes} min");
        if (a.Seconds != 0 || a.Milliseconds != 0 || parts.Count == 0)
            parts.Add(a.Milliseconds == 0 ? $"{a.Seconds} s" : $"{a.Seconds}.{a.Milliseconds.ToString("D3", CultureInfo.InvariantCulture).TrimEnd('0')} s");
        return (negative ? "-" : "") + string.Join(" ", parts);
    }

    /// <summary>Adds two durations (or subtracts the second when <paramref name="subtract"/> is true).</summary>
    public static TimeSpan Combine(TimeSpan a, TimeSpan b, bool subtract) => subtract ? a - b : a + b;

    /// <summary>
    /// Reads a date request: "2026-10-13 to 2026-12-25" (days between), "2026-10-13 + 100 days" or
    /// "2026-10-13 - 2 months" (add or subtract), and "1:30 + 0:45" (durations).
    /// </summary>
    public static bool TryParseRequest(string text, [NotNullWhen(true)] out DateRequest? request, DateOnly? today = null)
    {
        request = null;
        var s = Clean(text);
        foreach (var sep in new[] { " to ", " until ", " - ", " -> " })
        {
            int at = s.IndexOf(sep, StringComparison.OrdinalIgnoreCase);
            if (at <= 0) continue;
            if (TryParseDate(s[..at], out var a, today) && TryParseDate(s[(at + sep.Length)..], out var b, today))
            {
                request = new DateRequest.Between(a, b);
                return true;
            }
        }
        foreach (var op in new[] { '+', '-' })
        {
            for (int at = s.LastIndexOf(op); at > 0; at = at > 0 ? s.LastIndexOf(op, at - 1) : -1)
            {
                var left = s[..at].Trim();
                var right = s[(at + 1)..].Trim();
                if (TryParseDate(left, out var date, today) && TryParseAmount(right, out int amount, out var step))
                {
                    request = new DateRequest.Shift(date, op == '-' ? -amount : amount, step);
                    return true;
                }
                if (left.Contains(':') && TryParseDuration(left, out var d1) && TryParseDuration(right, out var d2))
                {
                    request = new DateRequest.Durations(d1, d2, op == '-');
                    return true;
                }
                if (at == 0) break;
            }
        }
        return false;
    }

    /// <summary>Reads "100 days", "2 weeks", "3 months", "1 year", "10 d" or a bare number of days.</summary>
    public static bool TryParseAmount(string text, out int amount, out DateStep step)
    {
        step = DateStep.Days;
        amount = 0;
        var parts = Clean(text).Split(' ', 2);
        if (!int.TryParse(parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount)) return false;
        if (parts.Length == 1) return true;
        var unit = parts[1].Trim().ToLowerInvariant();
        switch (unit)
        {
            case "d" or "day" or "days": step = DateStep.Days; return true;
            case "w" or "wk" or "wks" or "week" or "weeks": step = DateStep.Weeks; return true;
            case "m" or "mo" or "mos" or "month" or "months": step = DateStep.Months; return true;
            case "y" or "yr" or "yrs" or "year" or "years": step = DateStep.Years; return true;
            default: return false;
        }
    }
}

/// <summary>A parsed date request (see <see cref="DateTools.TryParseRequest"/>).</summary>
public abstract record DateRequest
{
    private DateRequest() { }

    /// <summary>Days between two dates.</summary>
    public sealed record Between(DateOnly From, DateOnly To) : DateRequest;

    /// <summary>A date plus or minus an amount.</summary>
    public sealed record Shift(DateOnly Date, int Amount, DateStep Step) : DateRequest;

    /// <summary>Two durations added or subtracted.</summary>
    public sealed record Durations(TimeSpan First, TimeSpan Second, bool Subtract) : DateRequest;
}

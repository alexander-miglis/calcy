using Calcy.Units;

namespace Calcy.Modes.Convert.Tests;

public class DateToolsTests
{
    private static DateOnly D(string iso) => DateOnly.ParseExact(iso, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("2026-10-13", "2026-12-25", 73)]
    [InlineData("2026-12-25", "2026-10-13", -73)]
    [InlineData("2026-10-13", "2026-10-13", 0)]
    [InlineData("2024-02-28", "2024-03-01", 2)]
    [InlineData("2023-02-28", "2023-03-01", 1)]
    [InlineData("2000-01-01", "2001-01-01", 366)]
    [InlineData("1900-01-01", "1901-01-01", 365)]
    [InlineData("2100-02-28", "2100-03-01", 1)]
    [InlineData("0001-01-01", "9999-12-31", 3652058)]
    public void DaysBetween(string from, string to, int days) => Assert.Equal(days, DateTools.DaysBetween(D(from), D(to)));

    [Theory]
    [InlineData(73, 10, 3)]
    [InlineData(-73, -10, -3)]
    [InlineData(6, 0, 6)]
    [InlineData(14, 2, 0)]
    public void WeeksAndDays(int days, int weeks, int rest) => Assert.Equal((weeks, rest), DateTools.WeeksAndDays(days));

    [Theory]
    [InlineData("2026-10-13", "2026-12-25", 53)] // Tue to Fri: 10 full weeks plus Tue, Wed, Thu
    [InlineData("2026-12-25", "2026-10-13", -53)]
    [InlineData("2026-10-17", "2026-10-19", 0)] // Sat, Sun
    [InlineData("2026-10-16", "2026-10-19", 1)] // Fri only (the end date does not count)
    [InlineData("2026-10-12", "2026-10-19", 5)] // one full week
    [InlineData("2026-10-13", "2026-10-13", 0)]
    [InlineData("2024-02-26", "2024-03-04", 5)] // across a leap day
    public void Weekdays(string from, string to, int count) => Assert.Equal(count, DateTools.Weekdays(D(from), D(to)));

    [Fact]
    public void WeekdaysMatchesCountingDayByDay()
    {
        var start = D("2026-01-01");
        for (int span = -40; span <= 40; span++)
        {
            var end = start.AddDays(span);
            int expected = 0;
            var (a, b) = span >= 0 ? (start, end) : (end, start);
            for (var d = a; d < b; d = d.AddDays(1))
                if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) expected++;
            Assert.Equal(span >= 0 ? expected : -expected, DateTools.Weekdays(start, end));
        }
    }

    [Theory]
    [InlineData("2026-01-31", "2026-02-28", 0, 1, 0, false)]
    [InlineData("2026-01-31", "2026-03-01", 0, 1, 1, false)]
    [InlineData("2024-01-31", "2024-02-29", 0, 1, 0, false)]
    [InlineData("2000-02-29", "2026-10-13", 26, 7, 14, false)]
    [InlineData("2026-03-01", "2026-01-31", 0, 1, 1, true)]
    [InlineData("2026-10-13", "2026-12-25", 0, 2, 12, false)]
    [InlineData("2024-02-29", "2025-02-28", 1, 0, 0, false)]
    public void YearsMonthsDays(string from, string to, int y, int m, int d, bool negative)
    {
        var span = DateTools.YearsMonthsDays(D(from), D(to));
        Assert.Equal(new DateSpan(y, m, d, negative), span);
        // Adding the span to the earlier date gives the later one.
        var (a, b) = negative ? (D(to), D(from)) : (D(from), D(to));
        Assert.Equal(b, DateTools.Add(a, span.Years, span.Months, span.Days));
    }

    [Theory]
    [InlineData("2026-01-31", 1, DateStep.Months, "2026-02-28")]
    [InlineData("2024-01-31", 1, DateStep.Months, "2024-02-29")]
    [InlineData("2026-03-31", -1, DateStep.Months, "2026-02-28")]
    [InlineData("2026-05-31", 1, DateStep.Months, "2026-06-30")]
    [InlineData("2024-02-29", 1, DateStep.Years, "2025-02-28")]
    [InlineData("2024-02-29", 4, DateStep.Years, "2028-02-29")]
    [InlineData("2024-02-29", -100, DateStep.Years, "1924-02-29")]
    [InlineData("2026-10-13", 100, DateStep.Days, "2027-01-21")]
    [InlineData("2026-10-13", -100, DateStep.Days, "2026-07-05")]
    [InlineData("2026-10-13", 2, DateStep.Weeks, "2026-10-27")]
    [InlineData("2026-12-31", 1, DateStep.Days, "2027-01-01")]
    public void AddWithMonthEnds(string date, int amount, DateStep step, string expected) =>
        Assert.Equal(D(expected), DateTools.Add(D(date), amount, step));

    [Fact]
    public void AddYearsMonthsThenDays()
    {
        Assert.Equal(D("2026-03-01"), DateTools.Add(D("2026-01-31"), 0, 1, 1));
        Assert.Equal(D("2027-02-28"), DateTools.Add(D("2026-01-31"), 1, 1, 0));
    }

    [Fact]
    public void AddOutsideTheCalendarFails()
    {
        Assert.False(DateTools.TryAdd(DateOnly.MaxValue, 1, DateStep.Days, out _));
        Assert.False(DateTools.TryAdd(DateOnly.MinValue, -1, DateStep.Months, out _));
        Assert.False(DateTools.TryAdd(D("2026-01-01"), int.MaxValue, DateStep.Weeks, out _));
        Assert.True(DateTools.TryAdd(D("9999-12-30"), 1, DateStep.Days, out var last));
        Assert.Equal(DateOnly.MaxValue, last);
    }

    [Theory]
    [InlineData("2026-10-13", 2026, 42)]
    [InlineData("2027-01-01", 2026, 53)]
    [InlineData("2021-01-03", 2020, 53)]
    [InlineData("2024-12-30", 2025, 1)]
    [InlineData("2026-01-01", 2026, 1)]
    public void IsoWeeks(string date, int year, int week) => Assert.Equal((year, week), DateTools.IsoWeek(D(date)));

    [Fact]
    public void DayNamesAndLeapYears()
    {
        Assert.Equal("Tuesday", DateTools.DayName(D("2026-10-13")));
        Assert.Equal("Friday", DateTools.DayName(D("2026-12-25")));
        Assert.Equal("Thursday", DateTools.DayName(D("2027-01-21")));
        Assert.Equal("2026-W42", DateTools.IsoWeekText(D("2026-10-13")));
        Assert.True(DateTools.IsLeapYear(2000));
        Assert.True(DateTools.IsLeapYear(2024));
        Assert.False(DateTools.IsLeapYear(1900));
        Assert.False(DateTools.IsLeapYear(2100));
        Assert.Equal(366, DateTools.DayOfYear(D("2024-12-31")));
    }

    [Theory]
    [InlineData("2026-10-13")]
    [InlineData("2026/10/13")]
    [InlineData("20261013")]
    [InlineData("2026-10-13 ")]
    [InlineData("13 Oct 2026")]
    [InlineData("13 October 2026")]
    [InlineData("Oct 13, 2026")]
    public void ParsesDates(string text)
    {
        Assert.True(DateTools.TryParseDate(text, out var d));
        Assert.Equal(D("2026-10-13"), d);
    }

    [Theory]
    [InlineData("2026-02-30")]
    [InlineData("2026-13-01")]
    [InlineData("13/10/2026")]
    [InlineData("")]
    [InlineData("soon")]
    public void RejectsBadDates(string text) => Assert.False(DateTools.TryParseDate(text, out _));

    [Fact]
    public void TodayIsRelative()
    {
        Assert.True(DateTools.TryParseDate("Today", out var d, D("2030-05-05")));
        Assert.Equal(D("2030-05-05"), d);
    }

    // ---- Durations ----

    [Theory]
    [InlineData("1:30", "1:30:00")]
    [InlineData("1:30:15", "1:30:15")]
    [InlineData("26:30:00", "26:30:00")]
    [InlineData("1.02:00:00", "26:00:00")]
    [InlineData("-0:30", "-0:30:00")]
    [InlineData("0:00:01.5", "0:00:01.5")]
    [InlineData("1h 30m 15s", "1:30:15")]
    [InlineData("90 min", "1:30:00")]
    [InlineData("1.5 h", "1:30:00")]
    [InlineData("2 d 3 h", "51:00:00")]
    [InlineData("45 s", "0:00:45")]
    public void ParsesDurations(string text, string formatted)
    {
        Assert.True(DateTools.TryParseDuration(text, out var d), text);
        Assert.Equal(formatted, DateTools.FormatDuration(d));
    }

    [Theory]
    [InlineData("1:75")]
    [InlineData("1:30:60")]
    [InlineData("90")]
    [InlineData("5 km")]
    [InlineData("")]
    public void RejectsBadDurations(string text) => Assert.False(DateTools.TryParseDuration(text, out _));

    [Fact]
    public void DurationArithmetic()
    {
        DateTools.TryParseDuration("1:30:00", out var a);
        DateTools.TryParseDuration("0:45:30", out var b);
        Assert.Equal("2:15:30", DateTools.FormatDuration(DateTools.Combine(a, b, subtract: false)));
        Assert.Equal("0:44:30", DateTools.FormatDuration(DateTools.Combine(a, b, subtract: true)));
        Assert.Equal("-0:44:30", DateTools.FormatDuration(DateTools.Combine(b, a, subtract: true)));
        Assert.Equal("2 h 15 min 30 s", DateTools.DescribeDuration(a + b));
        Assert.Equal("1 d 2 h 30 min 5 s", DateTools.DescribeDuration(new TimeSpan(1, 2, 30, 5)));
        Assert.Equal("0 s", DateTools.DescribeDuration(TimeSpan.Zero));
    }

    // ---- Requests ----

    [Fact]
    public void ParsesRequests()
    {
        Assert.True(DateTools.TryParseRequest("2026-10-13 to 2026-12-25", out var r));
        Assert.Equal(new DateRequest.Between(D("2026-10-13"), D("2026-12-25")), r);
        Assert.True(DateTools.TryParseRequest("2026-10-13 + 100 days", out r));
        Assert.Equal(new DateRequest.Shift(D("2026-10-13"), 100, DateStep.Days), r);
        Assert.True(DateTools.TryParseRequest("2026-10-13 - 2 months", out r));
        Assert.Equal(new DateRequest.Shift(D("2026-10-13"), -2, DateStep.Months), r);
        Assert.True(DateTools.TryParseRequest("2026-10-13 + 3 weeks", out r));
        Assert.Equal(new DateRequest.Shift(D("2026-10-13"), 3, DateStep.Weeks), r);
        Assert.True(DateTools.TryParseRequest("1:30 + 0:45", out r));
        Assert.Equal(new DateRequest.Durations(TimeSpan.FromMinutes(90), TimeSpan.FromMinutes(45), false), r);
        Assert.False(DateTools.TryParseRequest("5 km to mi", out _));
        Assert.False(DateTools.TryParseRequest("2026-10-13", out _));
    }
}

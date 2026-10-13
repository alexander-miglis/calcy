using System.Globalization;
using System.Text;
using Calcy.Statistics;

namespace Calcy.Modes.Finance.Logic;

/// <summary>Sums of one year of an amortization schedule.</summary>
/// <param name="Year">Year number, starting at 1.</param>
/// <param name="FirstPeriod">First payment number in the year.</param>
/// <param name="LastPeriod">Last payment number in the year.</param>
/// <param name="Payment">Sum of the payments.</param>
/// <param name="Interest">Sum of the interest.</param>
/// <param name="Principal">Sum of the principal.</param>
/// <param name="EndBalance">Balance after the year's last payment.</param>
public sealed record AmortizationYear(int Year, int FirstPeriod, int LastPeriod, double Payment, double Interest, double Principal, double EndBalance);

/// <summary>Interest, principal and balance over a range of payments (the TI ΣInt, ΣPrn and bal).</summary>
public sealed record AmortizationRange(int From, int To, double Interest, double Principal, double Balance);

/// <summary>
/// A full amortization schedule built with <see cref="Amortization"/>: every payment, yearly sums and totals, CSV export.
/// TI signs: for a loan the payments, interest and principal are negative (paid out) and the balance positive.
/// </summary>
public sealed class AmortizationReport
{
    /// <summary>The largest schedule shown or exported.</summary>
    public const int MaxRows = 12_000;

    private AmortizationReport(TvmValues values, IReadOnlyList<AmortizationRow> rows, int perYear)
    {
        Values = values;
        Rows = rows;
        PaymentsPerYear = perYear;
        var years = new List<AmortizationYear>();
        for (int start = 0; start < rows.Count; start += perYear)
        {
            int end = Math.Min(rows.Count, start + perYear);
            double p = 0, i = 0, pr = 0;
            for (int k = start; k < end; k++)
            {
                p += rows[k].Payment;
                i += rows[k].Interest;
                pr += rows[k].Principal;
            }
            years.Add(new AmortizationYear(start / perYear + 1, rows[start].Period, rows[end - 1].Period, p, i, pr, rows[end - 1].Balance));
        }
        Years = years;
        foreach (var r in rows)
        {
            TotalPayment += r.Payment;
            TotalInterest += r.Interest;
            TotalPrincipal += r.Principal;
        }
    }

    /// <summary>The TVM values the schedule is built from.</summary>
    public TvmValues Values { get; }

    /// <summary>Every payment.</summary>
    public IReadOnlyList<AmortizationRow> Rows { get; }

    /// <summary>Payments per year used for the yearly grouping (P/Y rounded, at least 1).</summary>
    public int PaymentsPerYear { get; }

    /// <summary>Yearly sums.</summary>
    public IReadOnlyList<AmortizationYear> Years { get; }

    /// <summary>Sum of every payment.</summary>
    public double TotalPayment { get; }

    /// <summary>Sum of every interest amount.</summary>
    public double TotalInterest { get; }

    /// <summary>Sum of every principal amount.</summary>
    public double TotalPrincipal { get; }

    /// <summary>Balance after the last payment.</summary>
    public double FinalBalance => Rows.Count == 0 ? Values.PresentValue : Rows[^1].Balance;

    /// <summary>Builds the schedule, or returns a plain-language reason why it cannot be built.</summary>
    public static AmortizationReport? TryBuild(TvmValues values, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(values);
        problem = null;
        if (!(values.N >= 1))
        {
            problem = "A schedule needs at least one payment: N must be 1 or more.";
            return null;
        }
        int count = (int)Math.Ceiling(values.N - 1e-9);
        if (count > MaxRows)
        {
            problem = $"The schedule is limited to {Periods.Whole(MaxRows)} payments; N is {Money.Number(values.N, 2)}.";
            return null;
        }
        try
        {
            var rows = Amortization.Schedule(values, count);
            int perYear = Math.Max(1, (int)Math.Round(values.PaymentsPerYear));
            return new AmortizationReport(values, rows, perYear);
        }
        catch (ArgumentException ex)
        {
            problem = ValueReader.Sentence(ex.Message);
            return null;
        }
    }

    /// <summary>The TI ΣInt, ΣPrn and bal for payments <paramref name="from"/> to <paramref name="to"/>.</summary>
    public AmortizationRange Range(int from, int to)
    {
        if (from < 1) throw new ArgumentOutOfRangeException(nameof(from), "the first payment must be 1 or more");
        if (to < from) throw new ArgumentOutOfRangeException(nameof(to), "the last payment must not be before the first");
        return new AmortizationRange(from, to,
            Amortization.SumInterest(Values, from, to),
            Amortization.SumPrincipal(Values, from, to),
            Amortization.Balance(Values, to));
    }

    /// <summary>The schedule as CSV (invariant numbers with two decimals), with a header row and a totals row.</summary>
    public string ToCsv()
    {
        var sb = new StringBuilder();
        sb.Append("Payment,Year,Amount,Interest,Principal,Balance\n");
        foreach (var r in Rows)
        {
            int year = (r.Period - 1) / PaymentsPerYear + 1;
            sb.Append(r.Period.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(year.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(Money.Csv(r.Payment)).Append(',')
              .Append(Money.Csv(r.Interest)).Append(',')
              .Append(Money.Csv(r.Principal)).Append(',')
              .Append(Money.Csv(r.Balance)).Append('\n');
        }
        sb.Append("Total,,").Append(Money.Csv(TotalPayment)).Append(',').Append(Money.Csv(TotalInterest)).Append(',')
          .Append(Money.Csv(TotalPrincipal)).Append(',').Append(Money.Csv(FinalBalance)).Append('\n');
        return sb.ToString();
    }
}

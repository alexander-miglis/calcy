using Calcy.Statistics.Internal;

namespace Calcy.Statistics;

/// <summary>One payment of an amortization schedule (TI sign convention: interest paid on a loan is negative).</summary>
public sealed record AmortizationRow(int Period, double Payment, double Interest, double Principal, double Balance);

/// <summary>
/// Amortization functions of the TI-84 Finance app: bal(), SumPrn (the TI sigma Prn) and SumInt (sigma Int).
/// They use the TVM values I%, PV, PMT, P/Y, C/Y and Begin/End. When roundDigits is given every interest amount,
/// the payment and the opening balance are rounded to that many decimals, as the TI roundvalue argument does;
/// otherwise full precision is used.
/// </summary>
public static class Amortization
{
    private static double Rnd(double v, int? digits) => digits is null ? v : Math.Round(v, digits.Value, MidpointRounding.AwayFromZero);

    private static void Check(TvmValues tvm, int? roundDigits)
    {
        Guard.NotNull(tvm, nameof(tvm));
        Guard.Finite(tvm.PresentValue, "PV", nameof(tvm));
        Guard.Finite(tvm.Payment, "PMT", nameof(tvm));
        if (roundDigits is < 0 or > 15) throw new ArgumentException("the rounding must be between 0 and 15 decimals", nameof(roundDigits));
    }

    /// <summary>Walks the schedule up to the given period, calling back for every row.</summary>
    private static double Walk(TvmValues tvm, int periods, int? roundDigits, Action<AmortizationRow>? onRow)
    {
        Check(tvm, roundDigits);
        double i = Tvm.PeriodicRate(tvm.InterestRate, tvm.PaymentsPerYear, tvm.CompoundingsPerYear);
        double pmt = Rnd(tvm.Payment, roundDigits);
        double bal = Rnd(tvm.PresentValue, roundDigits);
        for (int m = 1; m <= periods; m++)
        {
            double interest;
            double next;
            if (tvm.Timing == PaymentTiming.Begin && m == 1)
            {
                interest = 0;
                next = Rnd(bal + pmt, roundDigits);
            }
            else
            {
                interest = -Rnd(i * bal, roundDigits);
                next = bal - interest + pmt;
            }
            onRow?.Invoke(new AmortizationRow(m, pmt, interest, pmt - interest, next));
            bal = next;
        }
        return bal;
    }

    /// <summary>bal(n): the balance after payment n.</summary>
    public static double Balance(TvmValues tvm, int period, int? roundDigits = null)
    {
        if (period < 0) throw new ArgumentException("the payment number must not be negative", nameof(period));
        return Walk(tvm, period, roundDigits, null);
    }

    /// <summary>The TI sigma Prn(p1, p2): principal paid from payment p1 through p2 (bal(p2) - bal(p1 - 1)).</summary>
    public static double SumPrincipal(TvmValues tvm, int fromPeriod, int toPeriod, int? roundDigits = null)
    {
        CheckRange(fromPeriod, toPeriod);
        return Balance(tvm, toPeriod, roundDigits) - Balance(tvm, fromPeriod - 1, roundDigits);
    }

    /// <summary>The TI sigma Int(p1, p2): interest paid from payment p1 through p2 ((p2 - p1 + 1) PMT - sigma Prn).</summary>
    public static double SumInterest(TvmValues tvm, int fromPeriod, int toPeriod, int? roundDigits = null)
    {
        CheckRange(fromPeriod, toPeriod);
        double pmt = Rnd(tvm.Payment, roundDigits);
        return (toPeriod - fromPeriod + 1) * pmt - SumPrincipal(tvm, fromPeriod, toPeriod, roundDigits);
    }

    /// <summary>Full schedule; the number of rows defaults to N rounded up.</summary>
    public static IReadOnlyList<AmortizationRow> Schedule(TvmValues tvm, int? periods = null, int? roundDigits = null)
    {
        Guard.NotNull(tvm, nameof(tvm));
        int count = periods ?? (int)Math.Ceiling(tvm.N - 1e-9);
        if (count < 0 || count > 1_000_000) throw new ArgumentException("the number of schedule rows must be between 0 and 1000000", nameof(periods));
        var rows = new List<AmortizationRow>(count);
        Walk(tvm, count, roundDigits, rows.Add);
        return rows;
    }

    private static void CheckRange(int p1, int p2)
    {
        if (p1 < 1) throw new ArgumentException("the first payment number must be at least 1", nameof(p1));
        if (p2 < p1) throw new ArgumentException("the last payment number must not be less than the first", nameof(p2));
    }
}

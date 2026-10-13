using Calcy.Statistics.Internal;

namespace Calcy.Statistics;

/// <summary>When payments are made within each period.</summary>
public enum PaymentTiming
{
    /// <summary>Payments at the end of each period (ordinary annuity, TI END).</summary>
    End,
    /// <summary>Payments at the beginning of each period (annuity due, TI BEGIN).</summary>
    Begin,
}

/// <summary>The time value of money variable to solve for.</summary>
public enum TvmVariable
{
    /// <summary>Number of payment periods N.</summary>
    N,
    /// <summary>Annual interest rate I% (percent).</summary>
    InterestRate,
    /// <summary>Present value PV.</summary>
    PresentValue,
    /// <summary>Payment PMT.</summary>
    Payment,
    /// <summary>Future value FV.</summary>
    FutureValue,
}

/// <summary>
/// The TI-84 TVM Solver variables. Cash paid out is negative and cash received is positive (TI sign convention).
/// InterestRate is the annual nominal rate in percent. CompoundingsPerYear defaults to PaymentsPerYear, and both
/// default to 12 as on the TI-84 TVM Solver.
/// </summary>
public sealed record TvmValues(
    double N,
    double InterestRate,
    double PresentValue,
    double Payment,
    double FutureValue,
    double PaymentsPerYear = 12,
    double? CompoundingsPerYear = null,
    PaymentTiming Timing = PaymentTiming.End)
{
    /// <summary>C/Y, which equals P/Y unless set explicitly.</summary>
    public double EffectiveCompoundingsPerYear => CompoundingsPerYear ?? PaymentsPerYear;

    /// <summary>Returns a copy with the given variable replaced.</summary>
    public TvmValues With(TvmVariable variable, double value) => variable switch
    {
        TvmVariable.N => this with { N = value },
        TvmVariable.InterestRate => this with { InterestRate = value },
        TvmVariable.PresentValue => this with { PresentValue = value },
        TvmVariable.Payment => this with { Payment = value },
        TvmVariable.FutureValue => this with { FutureValue = value },
        _ => throw new ArgumentException("unknown TVM variable", nameof(variable)),
    };
}

/// <summary>Time value of money solver with TI-84 Finance app semantics (tvm_N, tvm_I%, tvm_PV, tvm_Pmt, tvm_FV).</summary>
public static class Tvm
{
    /// <summary>Solves for one variable; the value currently stored for that variable is ignored.</summary>
    public static double Solve(TvmVariable unknown, TvmValues values)
    {
        Guard.NotNull(values, nameof(values));
        return unknown switch
        {
            TvmVariable.N => SolveN(values.InterestRate, values.PresentValue, values.Payment, values.FutureValue, values.PaymentsPerYear, values.CompoundingsPerYear, values.Timing),
            TvmVariable.InterestRate => SolveInterestRate(values.N, values.PresentValue, values.Payment, values.FutureValue, values.PaymentsPerYear, values.CompoundingsPerYear, values.Timing),
            TvmVariable.PresentValue => SolvePresentValue(values.N, values.InterestRate, values.Payment, values.FutureValue, values.PaymentsPerYear, values.CompoundingsPerYear, values.Timing),
            TvmVariable.Payment => SolvePayment(values.N, values.InterestRate, values.PresentValue, values.FutureValue, values.PaymentsPerYear, values.CompoundingsPerYear, values.Timing),
            TvmVariable.FutureValue => SolveFutureValue(values.N, values.InterestRate, values.PresentValue, values.Payment, values.PaymentsPerYear, values.CompoundingsPerYear, values.Timing),
            _ => throw new ArgumentException("unknown TVM variable", nameof(unknown)),
        };
    }

    /// <summary>Solves for one variable and returns the completed set of values.</summary>
    public static TvmValues SolveFor(TvmVariable unknown, TvmValues values) => values.With(unknown, Solve(unknown, values));

    /// <summary>Interest rate per payment period: (1 + I%/(100 C/Y))^(C/Y / P/Y) - 1.</summary>
    public static double PeriodicRate(double interestRate, double paymentsPerYear = 12, double? compoundingsPerYear = null)
    {
        Guard.Finite(interestRate, "the interest rate", nameof(interestRate));
        Guard.PositiveFinite(paymentsPerYear, "the payments per year", nameof(paymentsPerYear));
        double cy = compoundingsPerYear ?? paymentsPerYear;
        Guard.PositiveFinite(cy, "the compounding periods per year", nameof(compoundingsPerYear));
        if (cy == paymentsPerYear) return interestRate / (100 * paymentsPerYear);
        double j = interestRate / (100 * cy);
        if (!(j > -1)) throw new ArgumentException("the interest rate per compounding period must be greater than -100%", nameof(interestRate));
        return Num.Expm1(cy / paymentsPerYear * Num.Log1p(j));
    }

    /// <summary>Converts a rate per payment period back to the annual I% for the given P/Y and C/Y.</summary>
    public static double AnnualRate(double periodicRate, double paymentsPerYear = 12, double? compoundingsPerYear = null)
    {
        double cy = compoundingsPerYear ?? paymentsPerYear;
        if (cy == paymentsPerYear) return 100 * paymentsPerYear * periodicRate;
        return 100 * cy * Num.Expm1(paymentsPerYear / cy * Num.Log1p(periodicRate));
    }

    private static double Timing(PaymentTiming t) => t == PaymentTiming.Begin ? 1.0 : 0.0;

    private static void CheckN(double n)
    {
        Guard.Finite(n, "N", nameof(n));
    }

    /// <summary>tvm_N: number of payment periods.</summary>
    public static double SolveN(double interestRate, double presentValue, double payment, double futureValue, double paymentsPerYear = 12, double? compoundingsPerYear = null, PaymentTiming timing = PaymentTiming.End)
    {
        double i = PeriodicRate(interestRate, paymentsPerYear, compoundingsPerYear);
        Guard.Finite(presentValue, "PV", nameof(presentValue));
        Guard.Finite(payment, "PMT", nameof(payment));
        Guard.Finite(futureValue, "FV", nameof(futureValue));
        if (i == 0)
        {
            if (payment == 0) throw new ArgumentException("N is undefined when both the interest rate and the payment are zero");
            return -(presentValue + futureValue) / payment;
        }
        double g = 1 + i * Timing(timing);
        double num = payment * g - futureValue * i;
        double den = payment * g + presentValue * i;
        double ratio = num / den;
        if (!(ratio > 0) || !double.IsFinite(ratio)) throw new ArgumentException("no number of periods satisfies these values (check the signs of PV, PMT and FV)");
        return Math.Log(ratio) / Num.Log1p(i);
    }

    /// <summary>tvm_PV: present value.</summary>
    public static double SolvePresentValue(double n, double interestRate, double payment, double futureValue, double paymentsPerYear = 12, double? compoundingsPerYear = null, PaymentTiming timing = PaymentTiming.End)
    {
        CheckN(n);
        double i = PeriodicRate(interestRate, paymentsPerYear, compoundingsPerYear);
        if (i == 0) return -(payment * n + futureValue);
        double g = 1 + i * Timing(timing);
        double l = Num.Log1p(i);
        double v = Math.Exp(-n * l);                // (1+i)^-N
        double annuity = -Num.Expm1(-n * l) / i;    // (1 - (1+i)^-N) / i
        return -(payment * g * annuity + futureValue * v);
    }

    /// <summary>tvm_Pmt: payment per period.</summary>
    public static double SolvePayment(double n, double interestRate, double presentValue, double futureValue, double paymentsPerYear = 12, double? compoundingsPerYear = null, PaymentTiming timing = PaymentTiming.End)
    {
        CheckN(n);
        if (n == 0) throw new ArgumentException("the payment is undefined when N is zero", nameof(n));
        double i = PeriodicRate(interestRate, paymentsPerYear, compoundingsPerYear);
        if (i == 0) return -(presentValue + futureValue) / n;
        double g = 1 + i * Timing(timing);
        double l = Num.Log1p(i);
        double em1 = Num.Expm1(n * l);              // (1+i)^N - 1
        return -i / g * (presentValue + (presentValue + futureValue) / em1);
    }

    /// <summary>tvm_FV: future value.</summary>
    public static double SolveFutureValue(double n, double interestRate, double presentValue, double payment, double paymentsPerYear = 12, double? compoundingsPerYear = null, PaymentTiming timing = PaymentTiming.End)
    {
        CheckN(n);
        double i = PeriodicRate(interestRate, paymentsPerYear, compoundingsPerYear);
        if (i == 0) return -(presentValue + payment * n);
        double g = 1 + i * Timing(timing);
        double l = Num.Log1p(i);
        double growth = Math.Exp(n * l);
        double em1 = Num.Expm1(n * l);
        return -(presentValue * growth + payment * g * em1 / i);
    }

    /// <summary>
    /// tvm_I%: annual interest rate in percent. The equation is scanned over per-period rates from -99.99% to
    /// 10000% for sign changes and each bracket is refined with Brent's method; when several rates satisfy the
    /// equation the one closest to zero is returned.
    /// </summary>
    public static double SolveInterestRate(double n, double presentValue, double payment, double futureValue, double paymentsPerYear = 12, double? compoundingsPerYear = null, PaymentTiming timing = PaymentTiming.End)
    {
        CheckN(n);
        Guard.Finite(presentValue, "PV", nameof(presentValue));
        Guard.Finite(payment, "PMT", nameof(payment));
        Guard.Finite(futureValue, "FV", nameof(futureValue));
        Guard.PositiveFinite(paymentsPerYear, "the payments per year", nameof(paymentsPerYear));
        if (n == 0) throw new ArgumentException("the interest rate is undefined when N is zero", nameof(n));
        double k = Timing(timing);
        double scale = Math.Abs(presentValue) + Math.Abs(payment) * Math.Abs(n) + Math.Abs(futureValue);
        if (scale == 0) throw new ArgumentException("the interest rate is undefined when PV, PMT and FV are all zero");

        // f(r) with i = e^r - 1, scaled to stay bounded: for r >= 0 the TI form PV + PMT(1+ik)(1-v^N)/i + FV v^N,
        // for r < 0 the same multiplied by (1+i)^N. Both have the sign of the TVM equation.
        double F(double r)
        {
            double i = Num.Expm1(r);
            double g = 1 + i * k;
            double nr = n * r;
            if (r == 0) return (presentValue + payment * n + futureValue) / scale;
            double annuityFactor;
            if (r > 0 == n > 0)
            {
                annuityFactor = -Num.Expm1(-nr) / i;
                return (presentValue + payment * g * annuityFactor + futureValue * Math.Exp(-nr)) / scale;
            }
            annuityFactor = Num.Expm1(nr) / i;
            return (presentValue * Math.Exp(nr) + payment * g * annuityFactor + futureValue) / scale;
        }

        double rMin = Math.Log(1e-4), rMax = Math.Log(101.0);
        var roots = Roots.AllRoots(F, rMin, rMax, 4000);
        if (roots.Count == 0) throw new ArgumentException("no interest rate satisfies these values (check the signs of PV, PMT and FV)");
        double best = roots[0];
        foreach (var r in roots) if (Math.Abs(r) < Math.Abs(best)) best = r;
        double rate = Num.Expm1(best);
        return AnnualRate(rate, paymentsPerYear, compoundingsPerYear);
    }
}

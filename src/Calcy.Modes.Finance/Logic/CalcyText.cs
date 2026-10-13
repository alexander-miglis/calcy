using Calcy.Statistics;

namespace Calcy.Modes.Finance.Logic;

/// <summary>
/// Builds Calcy expressions that evaluate to the same answers in Advanced mode (the USE button), using the finance
/// functions of the engine (tvm_pmt, tvm_n, tvm_i, tvm_pv, tvm_fv, npv, irr, mirr, eff, nom, dbd, bal, sumprn, sumint).
/// Only answers the engine has no function for (30/360 day counts, dates outside 1950 to 2049, bonds) are sent as numbers.
/// </summary>
public static class CalcyText
{
    /// <summary>The engine's timing argument for payments at the beginning of each period.</summary>
    private const string Begin = "\"begin\"";

    private static string N(double v) => double.IsPositiveInfinity(v) ? "inf" : Money.Exact(v);

    private static string List(IEnumerable<double> values) => "{" + string.Join(", ", values.Select(N)) + "}";

    /// <summary>
    /// The trailing P/Y and C/Y arguments, plus "begin" for payments at the beginning of each period (end is the
    /// engine's default, so it is left out).
    /// </summary>
    private static string TvmTail(TvmValues v) =>
        $"{N(v.PaymentsPerYear)}, {N(v.EffectiveCompoundingsPerYear)}" + (v.Timing == PaymentTiming.Begin ? ", " + Begin : "");

    /// <summary>The expression for a solved TVM variable, for example tvm_pmt(360, 8, 100000, 0, 12, 12).</summary>
    public static string Tvm(TvmSolution s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var v = s.Values;
        string tail = TvmTail(v);
        return s.Solved switch
        {
            TvmVariable.Payment => $"tvm_pmt({N(v.N)}, {N(v.InterestRate)}, {N(v.PresentValue)}, {N(v.FutureValue)}, {tail})",
            TvmVariable.PresentValue => $"tvm_pv({N(v.N)}, {N(v.InterestRate)}, {N(v.Payment)}, {N(v.FutureValue)}, {tail})",
            TvmVariable.FutureValue => $"tvm_fv({N(v.N)}, {N(v.InterestRate)}, {N(v.PresentValue)}, {N(v.Payment)}, {tail})",
            TvmVariable.N => $"tvm_n({N(v.InterestRate)}, {N(v.PresentValue)}, {N(v.Payment)}, {N(v.FutureValue)}, {tail})",
            TvmVariable.InterestRate => $"tvm_i({N(v.N)}, {N(v.PresentValue)}, {N(v.Payment)}, {N(v.FutureValue)}, {tail})",
            _ => throw new ArgumentOutOfRangeException(nameof(s), s.Solved, "unknown TVM variable"),
        };
    }

    /// <summary>
    /// Arguments N, I%, PV, PMT, FV, P/Y for bal, sumprn and sumint, followed by C/Y and timing when they differ from
    /// the defaults (C/Y equal to P/Y, payments at the end).
    /// </summary>
    private static string AmortizationArgs(TvmValues v)
    {
        var args = string.Join(", ", new[] { v.N, v.InterestRate, v.PresentValue, v.Payment, v.FutureValue, v.PaymentsPerYear }.Select(N));
        bool begin = v.Timing == PaymentTiming.Begin;
        if (begin || v.EffectiveCompoundingsPerYear != v.PaymentsPerYear) args += ", " + N(v.EffectiveCompoundingsPerYear);
        return begin ? args + ", " + Begin : args;
    }

    /// <summary>The TI ΣInt over a range: sumint(from, to, N, I%, PV, PMT, FV, P/Y, C/Y, timing).</summary>
    public static string SumInterest(TvmValues v, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(v);
        return $"sumint({from}, {to}, {AmortizationArgs(v)})";
    }

    /// <summary>The TI ΣPrn over a range: sumprn(from, to, N, I%, PV, PMT, FV, P/Y, C/Y, timing).</summary>
    public static string SumPrincipal(TvmValues v, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(v);
        return $"sumprn({from}, {to}, {AmortizationArgs(v)})";
    }

    /// <summary>The balance after a payment: bal(k, N, I%, PV, PMT, FV, P/Y, C/Y, timing).</summary>
    public static string Balance(TvmValues v, int period)
    {
        ArgumentNullException.ThrowIfNull(v);
        return $"bal({period}, {AmortizationArgs(v)})";
    }

    /// <summary>The frequency list argument, or nothing when every flow happens once.</summary>
    private static string Counts(CashFlowResult r) => r.AllSingle ? "" : ", " + List(r.Counts.Select(c => (double)c));

    /// <summary>npv(rate, CF0, {CF1, ...}, {n1, ...}).</summary>
    public static string Npv(CashFlowResult r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return $"npv({N(r.Rate)}, {N(r.Initial)}, {List(r.Amounts)}{Counts(r)})";
    }

    /// <summary>irr(CF0, {CF1, ...}, {n1, ...}), or an empty string when there is no IRR.</summary>
    public static string Irr(CashFlowResult r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return r.Irr is null ? "" : $"irr({N(r.Initial)}, {List(r.Amounts)}{Counts(r)})";
    }

    /// <summary>mirr(finance, reinvest, CF0, {CF1, ...}, {n1, ...}), or an empty string when there is no MIRR.</summary>
    public static string Mirr(CashFlowResult r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return r.Mirr is null ? "" : $"mirr({N(r.FinanceRate)}, {N(r.ReinvestRate)}, {N(r.Initial)}, {List(r.Amounts)}{Counts(r)})";
    }

    /// <summary>eff(nominal, m) or nom(effective, m).</summary>
    public static string Rate(RateConversion c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return (c.Direction == RateDirection.NominalToEffective ? "eff(" : "nom(") + N(c.Input) + ", " + N(c.Compoundings) + ")";
    }

    /// <summary>Simple interest: P*r/100*days/basis.</summary>
    public static string SimpleInterest(SimpleInterestAnswer a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return $"{Paren(a.Principal)}*{Paren(a.Rate)}/100*{N(a.Days)}/{a.DayBasis}";
    }

    /// <summary>Compound growth: P*(1 + r/(100 m))^(m t), or P*e^(r/100*t) when continuous.</summary>
    public static string CompoundValue(CompoundInterestAnswer a)
    {
        ArgumentNullException.ThrowIfNull(a);
        if (double.IsPositiveInfinity(a.Compoundings)) return $"{Paren(a.Principal)}*e^({Paren(a.Rate)}/100*{N(a.Years)})";
        return $"{Paren(a.Principal)}*(1 + {Paren(a.Rate)}/{N(100 * a.Compoundings)})^({N(a.Compoundings)}*{N(a.Years)})";
    }

    /// <summary>Days between two dates: dbd(MM.DDYY, MM.DDYY) for actual days between 1950 and 2049, otherwise the number.</summary>
    public static string DaysBetween(DayCount d, DayCountConvention convention)
    {
        ArgumentNullException.ThrowIfNull(d);
        int days = convention switch
        {
            DayCountConvention.Actual => d.Actual,
            DayCountConvention.Thirty360US => d.Thirty360US,
            _ => d.Thirty360European,
        };
        if (convention == DayCountConvention.Actual && DateCalculator.TiDate(d.Start) is { } a && DateCalculator.TiDate(d.End) is { } b)
            return $"dbd({a}, {b})";
        return days.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A list of numbers: {1800, 1800, 1800}.</summary>
    public static string Values(IEnumerable<double> values) => List(values);

    /// <summary>A single number at full precision.</summary>
    public static string Number(double value) => N(value);

    private static string Paren(double v) => v < 0 ? "(" + N(v) + ")" : N(v);
}

using Calcy.Statistics.Internal;

namespace Calcy.Statistics;

/// <summary>Uneven cash flow analysis (TI-84 npv and irr, plus MIRR and payback periods). Rates are percent per period.</summary>
public static class CashFlow
{
    /// <summary>Expands the flows into (time, amount) pairs; CF0 occurs at time 0 and each listed flow repeats its frequency.</summary>
    private static List<(double T, double Amount)> Expand(double cf0, IReadOnlyList<double> cashFlows, IReadOnlyList<double>? frequencies)
    {
        Guard.Finite(cf0, "CF0", nameof(cf0));
        Guard.AllFinite(cashFlows, nameof(cashFlows));
        if (frequencies is not null)
        {
            Guard.SameLength(cashFlows.Count, frequencies.Count, "the cash flows and their frequencies");
            foreach (var f in frequencies)
                if (!(f >= 1) || !Num.IsInteger(f) || f > 1_000_000) throw new ArgumentException("cash flow frequencies must be positive integers", nameof(frequencies));
        }
        var list = new List<(double, double)> { (0, cf0) };
        double t = 0;
        for (int j = 0; j < cashFlows.Count; j++)
        {
            int count = frequencies is null ? 1 : (int)frequencies[j];
            for (int k = 0; k < count; k++)
            {
                t += 1;
                list.Add((t, cashFlows[j]));
            }
        }
        return list;
    }

    /// <summary>npv(rate, CF0, CFList, CFFreq): net present value at the given rate per period (percent).</summary>
    public static double Npv(double ratePercent, double cf0, IReadOnlyList<double> cashFlows, IReadOnlyList<double>? frequencies = null)
    {
        Guard.Finite(ratePercent, "the rate", nameof(ratePercent));
        if (!(ratePercent > -100)) throw new ArgumentException("the rate must be greater than -100%", nameof(ratePercent));
        var flows = Expand(cf0, cashFlows, frequencies);
        double l = Num.Log1p(ratePercent / 100);
        var acc = new Num.Accumulator();
        foreach (var (t, a) in flows) acc.Add(a * Math.Exp(-t * l));
        return acc.Value;
    }

    /// <summary>Every internal rate of return (percent per period) between -99.99% and 10000%, ascending.</summary>
    public static IReadOnlyList<double> IrrAll(double cf0, IReadOnlyList<double> cashFlows, IReadOnlyList<double>? frequencies = null)
    {
        var flows = Expand(cf0, cashFlows, frequencies);
        double T = flows[^1].T;
        double scale = 0;
        foreach (var (_, a) in flows) scale += Math.Abs(a);
        if (scale == 0) throw new ArgumentException("every cash flow is zero, so the IRR is undefined");
        double F(double r)
        {
            var acc = new Num.Accumulator();
            if (r >= 0) foreach (var (t, a) in flows) acc.Add(a * Math.Exp(-r * t));
            else foreach (var (t, a) in flows) acc.Add(a * Math.Exp(r * (T - t)));
            return acc.Value / scale;
        }
        var roots = Roots.AllRoots(F, Math.Log(1e-4), Math.Log(101.0), 4000);
        var result = new List<double>();
        foreach (var r in roots)
        {
            double pct = 100 * Num.Expm1(r);
            if (result.Count == 0 || Math.Abs(pct - result[^1]) > 1e-9 * Math.Max(1, Math.Abs(pct))) result.Add(pct);
        }
        return result;
    }

    /// <summary>
    /// irr(CF0, CFList, CFFreq): internal rate of return in percent per period. All rates are located by scanning for
    /// sign changes, so cash flows with several sign changes are handled; the root closest to the guess is returned.
    /// </summary>
    public static double Irr(double cf0, IReadOnlyList<double> cashFlows, IReadOnlyList<double>? frequencies = null, double guessPercent = 10)
    {
        var all = IrrAll(cf0, cashFlows, frequencies);
        if (all.Count == 0) throw new ArgumentException("no internal rate of return exists for these cash flows");
        double best = all[0];
        foreach (var r in all) if (Math.Abs(r - guessPercent) < Math.Abs(best - guessPercent)) best = r;
        return best;
    }

    /// <summary>
    /// Modified internal rate of return in percent: negative flows are discounted at the finance rate, positive flows
    /// are compounded to the last period at the reinvestment rate.
    /// </summary>
    public static double Mirr(double financeRatePercent, double reinvestRatePercent, double cf0, IReadOnlyList<double> cashFlows, IReadOnlyList<double>? frequencies = null)
    {
        if (!(financeRatePercent > -100) || !(reinvestRatePercent > -100)) throw new ArgumentException("the rates must be greater than -100%");
        var flows = Expand(cf0, cashFlows, frequencies);
        double T = flows[^1].T;
        if (T < 1) throw new ArgumentException("at least one cash flow after CF0 is required", nameof(cashFlows));
        double lf = Num.Log1p(financeRatePercent / 100), lr = Num.Log1p(reinvestRatePercent / 100);
        var pvNeg = new Num.Accumulator();
        var fvPos = new Num.Accumulator();
        foreach (var (t, a) in flows)
        {
            if (a < 0) pvNeg.Add(a * Math.Exp(-t * lf));
            else if (a > 0) fvPos.Add(a * Math.Exp((T - t) * lr));
        }
        if (!(pvNeg.Value < 0) || !(fvPos.Value > 0)) throw new ArgumentException("MIRR requires at least one negative and one positive cash flow");
        return 100 * Num.Expm1(Math.Log(fvPos.Value / -pvNeg.Value) / T);
    }

    /// <summary>Payback period in periods (linear within the period); null when the investment is never recovered.</summary>
    public static double? PaybackPeriod(double cf0, IReadOnlyList<double> cashFlows, IReadOnlyList<double>? frequencies = null)
        => Payback(Expand(cf0, cashFlows, frequencies), 0);

    /// <summary>Discounted payback period at the given rate per period (percent); null when never recovered.</summary>
    public static double? DiscountedPaybackPeriod(double ratePercent, double cf0, IReadOnlyList<double> cashFlows, IReadOnlyList<double>? frequencies = null)
    {
        if (!(ratePercent > -100)) throw new ArgumentException("the rate must be greater than -100%", nameof(ratePercent));
        return Payback(Expand(cf0, cashFlows, frequencies), Num.Log1p(ratePercent / 100));
    }

    private static double? Payback(List<(double T, double Amount)> flows, double logRate)
    {
        double cum = 0;
        for (int k = 0; k < flows.Count; k++)
        {
            var (t, a) = flows[k];
            double v = a * Math.Exp(-t * logRate);
            double prev = cum;
            cum += v;
            if (k == 0)
            {
                if (cum >= 0) return 0.0;
                continue;
            }
            if (cum >= 0 && prev < 0) return t - 1 + (-prev) / v;
        }
        return null;
    }
}

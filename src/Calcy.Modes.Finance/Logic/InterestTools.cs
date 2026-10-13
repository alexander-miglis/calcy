using FinanceMath = Calcy.Statistics.Finance;

namespace Calcy.Modes.Finance.Logic;

/// <summary>Which way the rate converter works.</summary>
public enum RateDirection
{
    /// <summary>From a nominal rate to the effective annual rate (TI Eff).</summary>
    NominalToEffective,

    /// <summary>From an effective annual rate to the nominal rate (TI Nom).</summary>
    EffectiveToNominal,
}

/// <summary>The text typed into the Interest tab.</summary>
public sealed class InterestInputs
{
    /// <summary>Conversion direction.</summary>
    public RateDirection Direction { get; set; } = RateDirection.NominalToEffective;

    /// <summary>The rate to convert, in percent.</summary>
    public string Rate { get; set; } = "6";

    /// <summary>Compounding periods per year (inf or "continuous" for continuous compounding).</summary>
    public string Compoundings { get; set; } = "12";

    /// <summary>Principal for simple and compound interest.</summary>
    public string Principal { get; set; } = "10000";

    /// <summary>Annual rate for simple and compound interest, in percent.</summary>
    public string AnnualRate { get; set; } = "5";

    /// <summary>Days for simple interest.</summary>
    public string Days { get; set; } = "90";

    /// <summary>Days in a year for simple interest: 365 or 360.</summary>
    public int DayBasis { get; set; } = 365;

    /// <summary>Years for compound interest.</summary>
    public string Years { get; set; } = "3";

    /// <summary>Compounding periods per year for compound interest.</summary>
    public string YearCompoundings { get; set; } = "12";
}

/// <summary>A converted rate.</summary>
/// <param name="Input">The rate typed, in percent.</param>
/// <param name="Compoundings">Compounding periods per year (infinity for continuous).</param>
/// <param name="Output">The converted rate, in percent.</param>
/// <param name="Direction">Which way it was converted.</param>
public sealed record RateConversion(double Input, double Compoundings, double Output, RateDirection Direction);

/// <summary>Simple interest on a principal for a number of days.</summary>
public sealed record SimpleInterestAnswer(double Principal, double Rate, double Days, int DayBasis, double Interest, double Total);

/// <summary>Compound interest: what a principal grows to.</summary>
public sealed record CompoundInterestAnswer(double Principal, double Rate, double Years, double Compoundings, double FutureValue, double Interest);

/// <summary>A value or the reasons it cannot be worked out.</summary>
public sealed record Answer<T>(T? Value, string? Problem, IReadOnlyDictionary<string, string> FieldErrors) where T : class;

/// <summary>Rate conversion, simple interest and compound interest for the Interest tab.</summary>
public static class InterestTools
{
    /// <summary>Converts a nominal rate to effective or back.</summary>
    public static Answer<RateConversion> Convert(InterestInputs inputs, ValueReader reader)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(reader);
        var errors = new Dictionary<string, string>();
        var rate = Required(reader, inputs.Rate, FieldRule.Rate("the rate"), "rate", errors, "type a rate in percent, for example 6");
        var m = Required(reader, inputs.Compoundings, new FieldRule("the compounding periods", Min: 0, MinIncluded: false, AllowInfinity: true), "compoundings", errors, "type how often interest is added each year, for example 12");
        if (errors.Count > 0) return new(null, Fix(errors), errors);
        try
        {
            double output = inputs.Direction == RateDirection.NominalToEffective
                ? FinanceMath.EffectiveRate(rate, m)
                : FinanceMath.NominalRate(rate, m);
            return new(new RateConversion(rate, m, output, inputs.Direction), null, errors);
        }
        catch (ArgumentException ex)
        {
            return new(null, ValueReader.Sentence(ex.Message), errors);
        }
    }

    /// <summary>Simple interest: principal × rate × days / days in the year.</summary>
    public static Answer<SimpleInterestAnswer> Simple(InterestInputs inputs, ValueReader reader)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(reader);
        var errors = new Dictionary<string, string>();
        var p = Required(reader, inputs.Principal, FieldRule.Amount("the principal"), "principal", errors, "type the amount, for example 10000");
        var r = Required(reader, inputs.AnnualRate, FieldRule.Rate("the rate"), "annualRate", errors, "type the yearly rate in percent");
        var d = Required(reader, inputs.Days, new FieldRule("the days", Min: 0, Max: 1_000_000), "days", errors, "type the number of days");
        if (errors.Count > 0) return new(null, Fix(errors), errors);
        int basis = inputs.DayBasis == 360 ? 360 : 365;
        var s = FinanceMath.SimpleInterest(p, r, d, basis);
        return new(new SimpleInterestAnswer(p, r, d, basis, s.Interest, s.FutureValue), null, errors);
    }

    /// <summary>Compound interest: principal × (1 + rate / m)^(m × years).</summary>
    public static Answer<CompoundInterestAnswer> Compound(InterestInputs inputs, ValueReader reader)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(reader);
        var errors = new Dictionary<string, string>();
        var p = Required(reader, inputs.Principal, FieldRule.Amount("the principal"), "principal", errors, "type the amount, for example 10000");
        var r = Required(reader, inputs.AnnualRate, FieldRule.Rate("the rate"), "annualRate", errors, "type the yearly rate in percent");
        var t = Required(reader, inputs.Years, new FieldRule("the years", Min: 0, Max: 10_000), "years", errors, "type the number of years");
        var m = Required(reader, inputs.YearCompoundings, new FieldRule("the compounding periods", Min: 0, MinIncluded: false, AllowInfinity: true), "yearCompoundings", errors, "type how often interest is added each year, for example 12");
        if (errors.Count > 0) return new(null, Fix(errors), errors);
        try
        {
            double fv = FinanceMath.CompoundInterest(p, r, t, m);
            if (!double.IsFinite(fv)) return new(null, "The result is too large to show.", errors);
            return new(new CompoundInterestAnswer(p, r, t, m, fv, fv - p), null, errors);
        }
        catch (ArgumentException ex)
        {
            return new(null, ValueReader.Sentence(ex.Message), errors);
        }
    }

    /// <summary>"12 times a year", "every day", "continuously".</summary>
    public static string HowOften(double m) => double.IsPositiveInfinity(m) ? "continuously" : m switch
    {
        1 => "once a year",
        2 => "twice a year",
        _ => Money.Number(m, 4) + " times a year",
    };

    internal static double Required(ValueReader reader, string text, FieldRule rule, string key, Dictionary<string, string> errors, string hint)
    {
        var f = reader.Read(text, rule);
        if (f.IsValid) return f.Value;
        errors[key] = f.Error ?? $"{ValueReader.Capital(rule.Name)} is empty: {hint}.";
        return double.NaN;
    }

    internal static string Fix(IReadOnlyDictionary<string, string> errors) =>
        errors.Count == 1 ? "Fix the field marked in red to see the answer." : "Fix the fields marked in red to see the answer.";
}

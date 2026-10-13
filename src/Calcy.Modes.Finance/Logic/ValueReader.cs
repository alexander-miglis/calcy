using System.Globalization;
using System.Text.RegularExpressions;
using Calcy.Core;
using Calcy.Core.Expressions;

namespace Calcy.Modes.Finance.Logic;

/// <summary>What an input field accepts, and the name used in its messages.</summary>
/// <param name="Name">Short name shown in messages, for example "N" or "the cost".</param>
/// <param name="IsPercent">Percent field: "5.5" and "5.5%" both mean 5.5 percent.</param>
/// <param name="Min">Smallest allowed value, or null for no limit.</param>
/// <param name="MinIncluded">Whether <paramref name="Min"/> itself is allowed.</param>
/// <param name="Max">Largest allowed value, or null for no limit.</param>
/// <param name="WholeNumber">Only whole numbers are allowed.</param>
/// <param name="AllowInfinity">Infinity is allowed (typed as inf, ∞ or "continuous"), for continuous compounding.</param>
public sealed record FieldRule(
    string Name,
    bool IsPercent = false,
    double? Min = null,
    bool MinIncluded = true,
    double? Max = null,
    bool WholeNumber = false,
    bool AllowInfinity = false)
{
    /// <summary>Any amount of money (positive for money received, negative for money paid).</summary>
    public static FieldRule Amount(string name) => new(name);

    /// <summary>An amount that must be more than zero.</summary>
    public static FieldRule PositiveAmount(string name) => new(name, Min: 0, MinIncluded: false);

    /// <summary>An annual rate in percent, more than -100 %.</summary>
    public static FieldRule Rate(string name) => new(name, IsPercent: true, Min: -100, MinIncluded: false);

    /// <summary>A whole number between the limits.</summary>
    public static FieldRule Whole(string name, double min, double max) => new(name, Min: min, Max: max, WholeNumber: true);
}

/// <summary>The state of one input after reading it.</summary>
public enum FieldState
{
    /// <summary>Nothing typed.</summary>
    Blank,

    /// <summary>A valid number.</summary>
    Valid,

    /// <summary>Something typed that cannot be used; see the message.</summary>
    Invalid,
}

/// <summary>The value of an input field, or why it cannot be used.</summary>
public readonly record struct FieldValue(FieldState State, double Value, string? Error)
{
    /// <summary>An empty field.</summary>
    public static FieldValue Blank => new(FieldState.Blank, double.NaN, null);

    /// <summary>A valid value.</summary>
    public static FieldValue Of(double value) => new(FieldState.Valid, value, null);

    /// <summary>An invalid field with a plain-language message.</summary>
    public static FieldValue Fail(string message) => new(FieldState.Invalid, double.NaN, message);

    /// <summary>True when nothing was typed.</summary>
    public bool IsBlank => State == FieldState.Blank;

    /// <summary>True when the value can be used.</summary>
    public bool IsValid => State == FieldState.Valid;
}

/// <summary>
/// Reads input fields. Every field accepts a Calcy expression (30*12, 5.5%, a/12 with a defined in Advanced), evaluated by
/// the shared calculator session, then checked against a <see cref="FieldRule"/>. Messages are plain language.
/// </summary>
public sealed partial class ValueReader
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    private readonly CalcSession _session;
    private readonly CultureInfo _culture;

    /// <summary>Creates a reader on a calculator session; the culture only decides which currency sign to point out.</summary>
    public ValueReader(CalcSession session, CultureInfo? culture = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _culture = culture ?? CultureInfo.CurrentCulture;
    }

    [GeneratedRegex(@"^[+-]?\d{1,3}(,\d{3})+(\.\d+)?$")]
    private static partial Regex GroupedNumber();

    /// <summary>Reads one field: blank, a valid number, or a message saying what to change.</summary>
    public FieldValue Read(string? text, FieldRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var t = (text ?? "").Trim();
        if (t.Length == 0) return FieldValue.Blank;

        if (ContainsCurrency(t))
            return FieldValue.Fail("Leave out the currency sign: type 1200, not $1,200.");
        if (GroupedNumber().IsMatch(t)) t = t.Replace(",", "", StringComparison.Ordinal);

        double value;
        if (rule.AllowInfinity && t.ToLowerInvariant() is "continuous" or "cont" or "inf" or "infinity" or "∞")
        {
            value = double.PositiveInfinity;
        }
        else
        {
            var read = Evaluate(t, rule);
            if (!read.IsValid) return read;
            value = read.Value;
            if (rule.IsPercent && t.Contains('%')) value *= 100;
        }
        return Check(value, rule);
    }

    private bool ContainsCurrency(string text)
    {
        foreach (var c in text)
            if (char.GetUnicodeCategory(c) == UnicodeCategory.CurrencySymbol) return true;
        var symbol = Money.CurrencySymbol(_culture);
        if (symbol.Length == 0) return false;
        if (!symbol.Any(char.IsLetter)) return text.Contains(symbol, StringComparison.Ordinal);
        // A symbol made of letters (kr, zł, CHF) only counts next to a number, so names like "kr" in a formula still work.
        var core = symbol.TrimEnd('.');
        var pattern = $@"(^|[\d\s]){Regex.Escape(core)}\.?($|[\d\s])";
        return Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant) && Regex.IsMatch(text, @"\d");
    }

    private FieldValue Evaluate(string text, FieldRule rule)
    {
        Expr result;
        try
        {
            using var cts = new CancellationTokenSource(Timeout);
            result = _session.EvaluateExpression(text, approximate: true, cts.Token);
        }
        catch (CalcException ex)
        {
            return FieldValue.Fail(Sentence(ex.Message));
        }
        catch (OperationCanceledException)
        {
            return FieldValue.Fail($"{Capital(rule.Name)} took too long to work out: type a simpler value.");
        }
        catch (DivideByZeroException)
        {
            return FieldValue.Fail("Division by zero.");
        }
        catch (Exception ex) when (ex is ArithmeticException or ArgumentException or InvalidOperationException or InsufficientExecutionStackException)
        {
            return FieldValue.Fail(Sentence(ex.Message));
        }

        switch (result)
        {
            case NumberExpr { Value: var n } when !n.IsReal:
                return FieldValue.Fail($"{Capital(rule.Name)} must be a real number, not a complex one.");
            case NumberExpr { Value: var n }:
                return double.IsNaN(n.Re) ? FieldValue.Fail($"{Capital(rule.Name)} is not a number.") : FieldValue.Of(n.Re);
            case ConstantExpr c when c.IsInfinite:
                return FieldValue.Of(c.ApproxValue);
        }
        var free = result.FreeSymbols();
        if (free.Count > 0)
            return FieldValue.Fail($"'{free[0]}' has no value yet: type a number, or define {free[0]} in Advanced first.");
        return FieldValue.Fail($"{Capital(rule.Name)} needs a single number.");
    }

    private static FieldValue Check(double v, FieldRule rule)
    {
        string name = Capital(rule.Name);
        if (double.IsNaN(v)) return FieldValue.Fail($"{name} is not a number.");
        if (double.IsInfinity(v) && !(rule.AllowInfinity && v > 0)) return FieldValue.Fail($"{name} is too large.");
        if (rule.WholeNumber && double.IsFinite(v) && Math.Abs(v - Math.Round(v)) > 1e-9)
            return FieldValue.Fail($"{name} must be a whole number.");
        if (rule.WholeNumber) v = Math.Round(v);
        if (rule.Min is { } min)
        {
            if (rule.MinIncluded && v < min) return FieldValue.Fail($"{name} must be at least {Show(min, rule)}.");
            if (!rule.MinIncluded && v <= min) return FieldValue.Fail($"{name} must be more than {Show(min, rule)}.");
        }
        if (rule.Max is { } max && v > max) return FieldValue.Fail($"{name} must be at most {Show(max, rule)}.");
        return FieldValue.Of(v);
    }

    private static string Show(double limit, FieldRule rule) =>
        Money.Number(limit, 4) + (rule.IsPercent ? " %" : "");

    /// <summary>Upper-cases the first letter of a name used at the start of a sentence.</summary>
    internal static string Capital(string s) => s.Length == 0 || char.IsUpper(s[0]) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Makes a library message read as a sentence: capital first letter and a full stop.</summary>
    internal static string Sentence(string message)
    {
        var m = message.Trim();
        if (m.Length == 0) return "This value cannot be used.";
        m = Capital(m);
        return m[^1] is '.' or '!' or '?' ? m : m + ".";
    }
}

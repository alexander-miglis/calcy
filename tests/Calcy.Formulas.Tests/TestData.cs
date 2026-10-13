using System.Text.RegularExpressions;
using Calcy.Formulas.Tests.Evaluation;

namespace Calcy.Formulas.Tests;

/// <summary>Shared access to the library and helpers that turn a formula into something numeric.</summary>
public static partial class TestData
{
    public static FormulaLibrary Library => FormulaLibrary.Default;

    /// <summary>
    /// Allowed symbol shape: one ASCII or Greek letter (or Δ followed by one letter), optionally followed by an
    /// underscore and a subscript of letters and digits. Examples: x, v_0, E_k, λ, ΔT, S_xx, Δν_Cs.
    /// </summary>
    [GeneratedRegex(@"^(Δ[A-Za-zΑ-Ωα-ω]|[A-Za-zΑ-Ωα-ω])(_[A-Za-z0-9Α-Ωα-ω]+)?$")]
    public static partial Regex SymbolPattern();

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    public static partial Regex KebabCase();

    public static TheoryData<string> FormulaIds()
    {
        var data = new TheoryData<string>();
        foreach (var f in Library.All) data.Add(f.Id);
        return data;
    }

    public static TheoryData<string> IdentityIds()
    {
        var data = new TheoryData<string>();
        foreach (var f in Library.All.Where(f => f.IsIdentity)) data.Add(f.Id);
        return data;
    }

    public static TheoryData<string> CategoryIds()
    {
        var data = new TheoryData<string>();
        foreach (var c in Library.Categories) data.Add(c.Id);
        return data;
    }

    /// <summary>Values of the constants listed by a formula, keyed by their symbol.</summary>
    public static Dictionary<string, double> ConstantValues(Formula f)
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var c in Library.ConstantsFor(f)) values[c.Symbol] = c.Value;
        return values;
    }

    /// <summary>All raw JSON data files embedded in the library assembly, keyed by resource name.</summary>
    public static IReadOnlyList<(string Name, string Json)> RawDataFiles()
    {
        var assembly = typeof(FormulaLibrary).Assembly;
        var list = new List<(string, string)>();
        foreach (string name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".json", StringComparison.Ordinal)).Order())
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            list.Add((name, reader.ReadToEnd()));
        }
        return list;
    }

    /// <summary>Counts sentences by their terminating punctuation.</summary>
    public static int SentenceCount(string text) => Regex.Matches(text.Trim(), @"[.!?](\s|$)").Count;

    /// <summary>
    /// Solves the formula's equation for <paramref name="unknown"/> with every other symbol taken from
    /// <paramref name="given"/> (plus the formula's constants). Returns every real root found.
    /// </summary>
    public static IReadOnlyList<double> Solve(Formula f, IReadOnlyDictionary<string, double> given, string unknown)
    {
        var (left, right) = Expression.ParseEquation(f.Equation);
        var env = ConstantValues(f);
        foreach (var (k, v) in given) env[k] = v;
        return Solver.FindRoots(x =>
        {
            env[unknown] = x;
            return left.Evaluate(env) - right.Evaluate(env);
        });
    }
}

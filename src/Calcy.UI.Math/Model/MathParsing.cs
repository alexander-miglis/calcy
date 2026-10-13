using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Parsing;

namespace Calcy.UI.Math.Model;

/// <summary>Parse helpers shared by the controls: a default context that knows the common function names.</summary>
public static class MathParsing
{
    /// <summary>Calculus and algebra names provided by other Calcy modules; known here so templates and parsing agree.</summary>
    private static readonly string[] ExtraFunctions =
    [
        "integrate", "diff", "limit", "solve", "nsolve", "expand", "factor", "taylor", "series", "partfrac",
        "collect", "cancel", "together", "roots", "zeros", "nint", "nderiv", "deriv", "dsolve", "laplace",
    ];

    private static readonly Lazy<IReadOnlySet<string>> Names = new(() =>
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in FunctionRegistry.CreateDefault().All)
        {
            set.Add(f.Name);
            foreach (var a in f.Aliases) set.Add(a);
        }
        foreach (var n in ExtraFunctions) set.Add(n);
        return set;
    });

    /// <summary>Every function name the default context knows (core builtins, their aliases and the calculus names).</summary>
    public static IReadOnlySet<string> DefaultFunctionNames => Names.Value;

    /// <summary>A new parse context that knows the default function names (Casio-style implicit multiplication).</summary>
    public static SimpleParseContext CreateDefaultContext() => new(Names.Value);

    /// <summary>The shared default parse context.</summary>
    public static IParseContext DefaultContext { get; } = CreateDefaultContext();

    /// <summary>Function names known to a context, for autocomplete-like decisions in the editor.</summary>
    public static IEnumerable<string> FunctionNamesOf(IParseContext? context)
    {
        if (context is CalcSession s)
        {
            foreach (var f in s.Functions.All)
            {
                yield return f.Name;
                foreach (var a in f.Aliases) yield return a;
            }
            foreach (var name in s.Env.Functions.Keys) yield return name;
        }
        foreach (var n in Names.Value) yield return n;
    }

    /// <summary>True when <paramref name="name"/> is a function in <paramref name="context"/> (or in the default list).</summary>
    public static bool IsFunction(IParseContext? context, string name) =>
        context?.IsFunction(name) ?? Names.Value.Contains(name);

    /// <summary>Parses a line of input, returning null and an error message when it does not parse.</summary>
    public static Statement? TryParseStatement(string text, IParseContext? context, out CalcException? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            return Parser.ParseStatement(text, context ?? DefaultContext);
        }
        catch (CalcException ex)
        {
            error = ex;
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or OverflowException or InsufficientExecutionStackException)
        {
            error = new CalcException(ex.Message);
            return null;
        }
    }
}

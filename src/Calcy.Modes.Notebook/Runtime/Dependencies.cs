using System.Text.RegularExpressions;
using Calcy.Core;
using Calcy.Core.Expressions;

namespace Calcy.Modes.Notebook.Runtime;

/// <summary>
/// Works out which names a piece of input can depend on, so a result can be reused while none of them changes.
/// The analysis is conservative: every identifier in the text, every defined name hidden inside a longer identifier
/// (ab can mean a times b), and everything the values and functions of those names refer to in turn.
/// </summary>
internal static partial class Dependencies
{
    /// <summary>The identifiers in a piece of calculator input or Python code.</summary>
    public static IReadOnlyList<string> Identifiers(string text)
    {
        var set = new List<string>();
        foreach (Match m in IdentifierPattern().Matches(text ?? ""))
            if (!set.Contains(m.Value)) set.Add(m.Value);
        return set;
    }

    /// <summary>The names an input can depend on in the given environment, sorted.</summary>
    public static SortedSet<string> Names(string text, EnvState env) => Names(Identifiers(text), env);

    /// <summary>The names a set of identifiers can depend on in the given environment, sorted.</summary>
    public static SortedSet<string> Names(IEnumerable<string> identifiers, EnvState env)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        void Add(string n)
        {
            if (names.Add(n)) queue.Enqueue(n);
        }
        var defined = env.Variables.Keys.Concat(env.Functions.Keys).ToList();
        foreach (var id in identifiers)
        {
            Add(id);
            if (id.Length < 2) continue;
            foreach (var d in defined)
                if (d.Length < id.Length && id.Contains(d, StringComparison.Ordinal)) Add(d);
        }
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            if (env.Variables.TryGetValue(n, out var v)) foreach (var s in Referenced(v)) Add(s);
            if (env.Functions.TryGetValue(n, out var f)) foreach (var s in Referenced(f.Body)) Add(s);
        }
        return names;
    }

    /// <summary>Symbols and called names inside an expression.</summary>
    public static IEnumerable<string> Referenced(Expr e)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        void Visit(Expr x)
        {
            switch (x)
            {
                case SymbolExpr s:
                    result.Add(s.Name);
                    break;
                case CallExpr c:
                    result.Add(c.Name);
                    break;
            }
            foreach (var ch in x.Children) Visit(ch);
        }
        Visit(e);
        return result;
    }

    /// <summary>The settings that change how input is read and evaluated.</summary>
    public static string SettingsKey(CalcSettings s) => $"{s.Angle}|{s.Implicit}|{s.Complex}|{s.SplitUnknownNames}";

    /// <summary>Names a code cell may set through calc.set("name", ...) or calc.eval("name = ...").</summary>
    public static IReadOnlyList<string> PossibleExports(string code)
    {
        var names = new List<string>();
        foreach (Match m in ExportPattern().Matches(code ?? ""))
        {
            var n = m.Groups["n"].Value;
            if (!names.Contains(n)) names.Add(n);
        }
        return names;
    }

    [GeneratedRegex(@"[\p{L}_][\p{L}\p{N}_]*")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(@"calc\s*\.\s*(set\s*\(\s*|eval\s*\(\s*)[""'](?<n>[\p{L}_][\p{L}\p{N}_]*)[""']?\s*(,|=)")]
    private static partial Regex ExportPattern();
}

/// <summary>The inputs a cached result was computed from: the text, the settings and the values of its names.</summary>
internal sealed class DependencyKey
{
    private readonly string _text;
    private readonly string _settings;
    private readonly int _generation;
    private readonly string[] _names;
    private readonly object?[] _values;

    private DependencyKey(string text, string settings, int generation, string[] names, object?[] values)
    {
        _text = text;
        _settings = settings;
        _generation = generation;
        _names = names;
        _values = values;
    }

    /// <summary>The names the input can depend on.</summary>
    public IReadOnlyList<string> Names => _names;

    public static DependencyKey Build(string text, EnvState env, string settings, int generation)
    {
        var names = Dependencies.Names(text, env).ToArray();
        var values = new object?[names.Length];
        for (int i = 0; i < names.Length; i++) values[i] = env.Lookup(names[i]);
        return new DependencyKey(text, settings, generation, names, values);
    }

    public bool Matches(DependencyKey other)
    {
        if (_text != other._text || _settings != other._settings || _generation != other._generation || _names.Length != other._names.Length) return false;
        for (int i = 0; i < _names.Length; i++)
        {
            if (_names[i] != other._names[i]) return false;
            if (!EnvState.SameValue(_values[i], other._values[i])) return false;
        }
        return true;
    }
}

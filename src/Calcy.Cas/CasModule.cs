using System.Collections.Immutable;
using Calcy.Core.Evaluation;

namespace Calcy.Cas;

/// <summary>
/// The computer algebra system: registers symbolic algebra, calculus and equation solving functions
/// (expand, factor, simplify, diff, integrate, limit, taylor, solve, symsum and more) into a function registry.
/// </summary>
public static class CasModule
{
    private static readonly object Gate = new();
    private static ImmutableHashSet<string> _ownNames = ImmutableHashSet.Create<string>(StringComparer.Ordinal);

    /// <summary>
    /// Numeric integration used when no closed form is found: (f, a, b) gives the integral of f from a to b
    /// (either bound may be infinite) or NaN when it fails. Defaults to adaptive Gauss-Kronrod.
    /// </summary>
    public static Func<Func<double, double>, double, double, double> NumericIntegrator { get; set; } = Numerics.GaussKronrod.Integrate;

    internal static bool IsOwnFunction(string name) => _ownNames.Contains(name);

    /// <summary>Registers every CAS function into the registry (replacing Core's simplify, factor and zeros).</summary>
    public static void Register(FunctionRegistry r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var defs = Definitions.All(r).ToList();
        lock (Gate)
        {
            var b = _ownNames.ToBuilder();
            foreach (var def in defs)
            {
                b.Add(def.Name);
                foreach (var a in def.Aliases) b.Add(a);
            }
            _ownNames = b.ToImmutable();
        }
        foreach (var def in defs) r.Add(def);
    }
}

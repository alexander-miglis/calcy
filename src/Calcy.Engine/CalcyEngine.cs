using Calcy.Cas;
using Calcy.Core;
using Calcy.Core.Evaluation;

namespace Calcy.Engine;

/// <summary>Composes the full Calcy engine: the core builtins plus the algebra, statistics, numerics and unit modules.</summary>
public static class CalcyEngine
{
    /// <summary>
    /// Single letters that modules may register as shorthand but that people use as variables far more often
    /// (d for distance, D for the discriminant in the formula collection).
    /// </summary>
    private static readonly string[] FreedAliases = ["d", "D"];

    /// <summary>A registry with every module registered. Earlier modules win when two define the same name.</summary>
    public static FunctionRegistry CreateRegistry()
    {
        var r = FunctionRegistry.CreateDefault();
        CasModule.Register(r);
        StatisticsModule.Register(r);
        // Numeric nsolve also handles systems and wide searches, so it replaces the algebra module's simpler one.
        NumericsModule.Register(r, "nsolve");
        UnitsModule.Register(r);
        foreach (var alias in FreedAliases) r.RemoveAlias(alias);
        return r;
    }

    /// <summary>A ready-to-use calculator session with every module.</summary>
    public static CalcSession CreateSession(CalcSettings? settings = null) => new(settings ?? new CalcSettings(), CreateRegistry());
}

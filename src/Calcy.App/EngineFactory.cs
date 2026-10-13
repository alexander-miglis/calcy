using Calcy.Core.Evaluation;

namespace Calcy.App;

/// <summary>Builds the function registry with every engine module (core, algebra, numerics, statistics, finance).</summary>
internal static class EngineFactory
{
    public static FunctionRegistry CreateRegistry() => Calcy.Engine.CalcyEngine.CreateRegistry();
}

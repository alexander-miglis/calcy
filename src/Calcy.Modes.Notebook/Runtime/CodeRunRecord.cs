using Calcy.Python;

namespace Calcy.Modes.Notebook.Runtime;

/// <summary>What a code cell's last run read and wrote, so the notebook can tell when the run is out of date.</summary>
/// <param name="Code">The code that ran.</param>
/// <param name="Reads">The calculator names the code could read, with their values when it ran.</param>
/// <param name="Effects">The variables and functions it set through calc.set, calc.eval and friends.</param>
/// <param name="PythonDefines">Python globals it created or changed.</param>
/// <param name="Sequence">Increases with every run in the notebook.</param>
/// <param name="Elapsed">Wall clock time.</param>
/// <param name="Status">How it ended.</param>
/// <param name="Success">True when it finished without an error.</param>
/// <param name="UsedInput">True when it asked for input (it is then never run again by itself).</param>
internal sealed record CodeRunRecord(
    string Code,
    IReadOnlyDictionary<string, object?> Reads,
    IReadOnlyList<EnvEffect> Effects,
    IReadOnlySet<string> PythonDefines,
    long Sequence,
    TimeSpan Elapsed,
    ExecutionStatus Status,
    bool Success,
    bool UsedInput);

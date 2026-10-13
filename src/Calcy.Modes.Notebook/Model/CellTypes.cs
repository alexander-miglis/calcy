using Calcy.Core;
using Calcy.Core.Expressions;

namespace Calcy.Modes.Notebook.Model;

/// <summary>The kinds of cell a notebook can hold.</summary>
public enum CellKind
{
    /// <summary>Rich text written in a Markdown subset, with inline maths between dollar signs.</summary>
    Text,

    /// <summary>Lines of maths: expressions and definitions such as a = 5 or f(x) = x^2.</summary>
    Maths,

    /// <summary>Named rows of data that become lists, with summary lines under the table.</summary>
    Table,

    /// <summary>An inline graph with its own expressions and sliders.</summary>
    Graph,

    /// <summary>Python run by the notebook's sandboxed interpreter.</summary>
    Code,
}

/// <summary>Where a cell's results stand.</summary>
public enum CellState
{
    /// <summary>Results are up to date.</summary>
    Ready,

    /// <summary>The input changed and new results are being worked out.</summary>
    Calculating,

    /// <summary>At least one part of the cell has an error.</summary>
    Error,

    /// <summary>The results use values from a code cell that has to run again (or a code cell whose inputs changed).</summary>
    Stale,

    /// <summary>A code cell that has not run in this session.</summary>
    NotRun,

    /// <summary>A code cell that is running now.</summary>
    Running,
}

/// <summary>The result of one maths line (or one table row, or one summary line).</summary>
/// <param name="Result">The calculator result.</param>
/// <param name="Tainted">True when the line uses a value that a stale or unrun code cell provides.</param>
public sealed record LineResult(CalcResult Result, bool Tainted)
{
    /// <summary>True when the line could not be calculated.</summary>
    public bool IsError => Result.IsError;
}

/// <summary>The value of a live box ($=expr$) in a text cell.</summary>
/// <param name="Source">The expression as written.</param>
/// <param name="Value">The exact value, or null on error.</param>
/// <param name="Approx">The decimal value when it differs from the exact one.</param>
/// <param name="Error">Why the value could not be worked out.</param>
/// <param name="Tainted">True when the value depends on a stale or unrun code cell.</param>
public sealed record LiveValue(string Source, Expr? Value, Expr? Approx, string? Error, bool Tainted);

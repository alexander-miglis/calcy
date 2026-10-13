using Calcy.Core;
using Calcy.Core.Expressions;

namespace Calcy.Modes.Solver.Logic;

/// <summary>Where text sent from another mode should go.</summary>
/// <param name="Tool">The tool to open.</param>
/// <param name="Lines">The text for its input (several lines for a system).</param>
/// <param name="Unknown">The unknown named in a solve(..., x) call, or null.</param>
public sealed record SolverRoute(SolverTool Tool, IReadOnlyList<string> Lines, string? Unknown);

public sealed partial class SolverEngine
{
    /// <summary>
    /// Decides which tool text from another mode belongs to: an inequality goes to INEQUALITY, a list of equations to
    /// NONLINEAR SYSTEM, anything else to ANY EQUATION. A solve(...) call is unwrapped to its equation and unknown.
    /// </summary>
    public SolverRoute Route(string text)
    {
        var t = (text ?? "").Trim();
        Expr e;
        try
        {
            e = Session.ParseExpression(MathText.Ascii(t));
        }
        catch (CalcException)
        {
            return new SolverRoute(SolverTool.Equation, [t], null);
        }
        string? unknown = null;
        if (e is CallExpr { Name: "solve" or "nsolve" or "zeros" or "roots" } call && call.Args.Length >= 1)
        {
            if (call.Args.Length >= 2 && call.Args[1] is SymbolExpr s) unknown = s.Name;
            e = call.Args[0];
            t = MathText.Input(e);
        }
        return e switch
        {
            RelationExpr { Op: not RelOp.Equal } => new SolverRoute(SolverTool.Inequality, [t], unknown),
            ListExpr l => new SolverRoute(SolverTool.NonlinearSystem, [.. l.Items.Select(MathText.Input)], null),
            LogicExpr { Op: LogicOp.And } a => new SolverRoute(SolverTool.NonlinearSystem, [.. a.Operands.Select(MathText.Input)], null),
            _ => new SolverRoute(SolverTool.Equation, [t], unknown),
        };
    }
}

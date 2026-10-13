using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Printing;
using Calcy.Engine;
using Calcy.Modes.Matrix.Logic;

namespace Calcy.Modes.Matrix.Tests;

/// <summary>Shared helpers for the logic tests.</summary>
internal static class TestUtil
{
    public static CalcSession Session() => CalcyEngine.CreateSession();

    /// <summary>Parses a Calcy matrix such as "[[1, 2], [3, 4]]".</summary>
    public static MatrixExpr M(string text, CalcSession? session = null)
    {
        var s = session ?? Session();
        return Assert.IsType<MatrixExpr>(s.EvaluateExpression(text));
    }

    /// <summary>Calcy input text (ASCII) of an expression.</summary>
    public static string In(Expr e) => ExprPrinter.ToInput(e);

    /// <summary>Applies a row operation the way a student would, to check that the recorded steps really lead to the result.</summary>
    public static MatrixExpr Apply(MatrixExpr m, RowOperation op)
    {
        var rows = Enumerable.Range(0, m.Rows).Select(r => Enumerable.Range(0, m.Cols).Select(c => m[r, c]).ToArray()).ToArray();
        switch (op.Kind)
        {
            case RowOperationKind.Swap:
                (rows[op.Target], rows[op.Source]) = (rows[op.Source], rows[op.Target]);
                break;
            case RowOperationKind.Scale:
                rows[op.Target] = [.. rows[op.Target].Select(x => Algebra.Mul(op.Factor, x))];
                break;
            default:
                rows[op.Target] = [.. rows[op.Target].Zip(rows[op.Source], (t, s) => Algebra.Add(t, Algebra.Mul(op.Factor, s)))];
                break;
        }
        return new MatrixExpr(m.Rows, m.Cols, [.. rows.SelectMany(r => r)]);
    }

    /// <summary>Checks that replaying the steps from the original matrix gives every recorded intermediate matrix.</summary>
    public static void AssertStepsReplay(EliminationResult r)
    {
        var current = r.Original;
        foreach (var step in r.Steps)
        {
            current = Apply(current, step.Operation);
            Assert.Equal(In(step.After), In(current));
        }
        Assert.Equal(In(r.Result), In(current));
    }
}

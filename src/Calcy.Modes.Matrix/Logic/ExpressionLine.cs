using System.Text;
using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Parsing;

namespace Calcy.Modes.Matrix.Logic;

/// <summary>The result of one expression line.</summary>
public sealed record ExpressionOutcome
{
    /// <summary>The text as typed.</summary>
    public required string Input { get; init; }

    /// <summary>The value (null on error).</summary>
    public Expr? Value { get; init; }

    /// <summary>The variable that was defined, for inputs such as D = A·B.</summary>
    public string? AssignedName { get; init; }

    /// <summary>The problem, when there is one.</summary>
    public string? Error { get; init; }

    /// <summary>True when the line failed.</summary>
    public bool IsError => Error is not null;
}

/// <summary>
/// Evaluates matrix expressions such as 2A^-1·B - I, Aᵀ·A or D = A·B with the calculator's own syntax (ᵀ is the core's
/// transpose operator). On top of that, I means an identity matrix whose size is taken from the other matrices in the line,
/// and ^T can be typed for ᵀ.
/// </summary>
public static class ExpressionLine
{
    /// <summary>Evaluates one line in the session (definitions are stored as variables).</summary>
    /// <param name="text">The input.</param>
    /// <param name="session">The calculator session that holds the matrices.</param>
    /// <param name="defaultIdentitySize">Size of I when nothing else in the line decides it.</param>
    /// <param name="ct">Cancellation.</param>
    public static ExpressionOutcome Evaluate(string text, CalcSession session, int defaultIdentitySize, CancellationToken ct = default)
    {
        var input = text.Trim();
        try
        {
            var prepared = CaretT(input, session.IsKnownName);
            var statement = session.Parse(prepared);
            switch (statement)
            {
                case AssignStatement a:
                {
                    var value = session.Evaluate(WithIdentity(a.Value, session, defaultIdentitySize), approximate: false, ct);
                    if (value.ContainsSymbol(a.Name))
                        throw new CalcException($"{a.Name} cannot be defined in terms of itself before it has a value");
                    session.Env.SetVariable(a.Name, value);
                    return new ExpressionOutcome { Input = input, Value = value, AssignedName = a.Name };
                }
                case ExprStatement e:
                {
                    var value = session.Evaluate(WithIdentity(e.Expression, session, defaultIdentitySize), approximate: false, ct);
                    session.Env.SetVariable("ans", value);
                    return new ExpressionOutcome { Input = input, Value = value };
                }
                default:
                {
                    var r = session.Evaluate(prepared, ct);
                    if (r.IsError) return new ExpressionOutcome { Input = input, Error = r.ErrorMessage ?? "Error" };
                    return new ExpressionOutcome { Input = input, Value = r.Exact, AssignedName = r.Name };
                }
            }
        }
        catch (CalcException ex) { return new ExpressionOutcome { Input = input, Error = ex.Message }; }
        catch (DivideByZeroException) { return new ExpressionOutcome { Input = input, Error = "Division by zero" }; }
        catch (OverflowException) { return new ExpressionOutcome { Input = input, Error = "The number is too large" }; }
        catch (InsufficientExecutionStackException) { return new ExpressionOutcome { Input = input, Error = "The expression is nested too deeply" }; }
    }

    /// <summary>
    /// Replaces I with an identity matrix when I is not a variable. Its size is that of the first square matrix in the line,
    /// else the row count of the first matrix, else <paramref name="defaultSize"/>.
    /// </summary>
    public static Expr WithIdentity(Expr e, CalcSession session, int defaultSize)
    {
        if (!e.ContainsSymbol("I") || session.Env.TryGetVariable("I", out _) || session.IsFunction("I")) return e;
        int? square = null, rows = null;
        foreach (var name in e.FreeSymbols())
        {
            if (!session.Env.TryGetVariable(name, out var v) || v is not MatrixExpr m) continue;
            rows ??= m.Rows;
            if (m.Rows == m.Cols) { square = m.Rows; break; }
        }
        int n = square ?? rows ?? Math.Max(1, defaultSize);
        var identity = MatrixOps.Identity(n);
        return e.Map(x => x is SymbolExpr { Name: "I" } ? identity : x);
    }

    /// <summary>
    /// Lets ^T stand for the transpose operator ᵀ (handy on keyboards without ᵀ). Left alone when T is a variable or when
    /// the T starts a longer name (A^Tx).
    /// </summary>
    public static string CaretT(string text, Func<string, bool> isKnownName)
    {
        if (!text.Contains("^T") || isKnownName("T")) return text;
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            bool caretT = text[i] == '^' && i + 1 < text.Length && text[i + 1] == 'T' &&
                          (i + 2 >= text.Length || !(char.IsLetterOrDigit(text[i + 2]) || text[i + 2] == '_'));
            if (!caretT)
            {
                sb.Append(text[i]);
                continue;
            }
            sb.Append('ᵀ');
            i++;
        }
        return sb.ToString();
    }
}

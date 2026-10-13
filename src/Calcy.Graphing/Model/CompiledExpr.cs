using Calcy.Core.Compilation;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Graphing.Numerics;

namespace Calcy.Graphing;

/// <summary>
/// A prepared expression compiled to a fast double delegate. The variables are the plot variables
/// (for example x, or x and y) followed by the item's parameters (slider values), which are passed per call.
/// Thread-safe: evaluation keeps no shared state.
/// </summary>
public sealed class CompiledExpr
{
    [ThreadStatic] private static double[]? _buffer1;
    [ThreadStatic] private static double[]? _buffer2;
    [ThreadStatic] private static double[]? _buffer0;

    private readonly Func<double[], double> _raw;
    private IntervalFunction? _interval;
    private readonly EvalContext _ctx;

    /// <summary>Compiles <paramref name="expression"/> for the given plot variables and parameters.</summary>
    public CompiledExpr(Expr expression, IReadOnlyList<string> plotVariables, IReadOnlyList<string> parameters, EvalContext ctx)
    {
        Expression = expression;
        PlotVariables = plotVariables;
        Parameters = parameters;
        _ctx = ctx;
        var all = plotVariables.Concat(parameters).ToArray();
        _raw = ExprCompiler.Compile(expression, all, ctx);
    }

    /// <summary>The prepared expression.</summary>
    public Expr Expression { get; }

    /// <summary>The plot variables, in argument order.</summary>
    public IReadOnlyList<string> PlotVariables { get; }

    /// <summary>The parameter names, in the order of the parameter vector.</summary>
    public IReadOnlyList<string> Parameters { get; }

    /// <summary>Interval version of the expression (compiled on first use).</summary>
    public IntervalFunction Interval => _interval ??= IntervalCompiler.Compile(Expression, PlotVariables.Concat(Parameters).ToArray(), _ctx);

    /// <summary>Value with no plot variable (constants such as x = 3, or point coordinates).</summary>
    public double Eval(double[] p)
    {
        var buf = Buffer(ref _buffer0, p.Length);
        Array.Copy(p, 0, buf, 0, p.Length);
        return Run(buf, p.Length);
    }

    /// <summary>Value at one plot variable.</summary>
    public double Eval(double a, double[] p)
    {
        var buf = Buffer(ref _buffer1, p.Length + 1);
        buf[0] = a;
        Array.Copy(p, 0, buf, 1, p.Length);
        return Run(buf, p.Length + 1);
    }

    /// <summary>Value at two plot variables.</summary>
    public double Eval(double a, double b, double[] p)
    {
        var buf = Buffer(ref _buffer2, p.Length + 2);
        buf[0] = a;
        buf[1] = b;
        Array.Copy(p, 0, buf, 2, p.Length);
        return Run(buf, p.Length + 2);
    }

    /// <summary>A one-variable delegate with the parameters fixed (snapshot of <paramref name="p"/>).</summary>
    public Func<double, double> Bind1(double[] p)
    {
        var copy = (double[])p.Clone();
        return x => Eval(x, copy);
    }

    /// <summary>A two-variable delegate with the parameters fixed.</summary>
    public Func<double, double, double> Bind2(double[] p)
    {
        var copy = (double[])p.Clone();
        return (x, y) => Eval(x, y, copy);
    }

    private double Run(double[] buf, int used)
    {
        // The compiled code reads exactly the slots it was compiled for; extra slots are harmless.
        _ = used;
        try
        {
            return _raw(buf);
        }
        catch (Exception ex) when (ex is ArithmeticException or IndexOutOfRangeException or InvalidOperationException)
        {
            return double.NaN;
        }
    }

    private static double[] Buffer(ref double[]? field, int size)
    {
        if (field is null || field.Length < size) field = new double[Math.Max(size, 4)];
        return field;
    }
}

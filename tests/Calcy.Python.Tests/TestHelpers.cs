using System.Text;

namespace Calcy.Python.Tests;

/// <summary>Helpers that run Python code and capture output.</summary>
internal static class PyRun
{
    public static (ExecutionResult Result, string Output) Run(string code, PythonEngineOptions? options = null,
        IEnumerable<string>? inputs = null, CancellationToken ct = default, PythonEngine? engine = null)
    {
        engine ??= new PythonEngine(options ?? new PythonEngineOptions { Timeout = TimeSpan.FromSeconds(20) });
        var sb = new StringBuilder();
        engine.Output = s => sb.Append(s);
        if (inputs != null)
        {
            var queue = new Queue<string>(inputs);
            engine.Input = _ => queue.Count > 0 ? queue.Dequeue() : null;
        }
        var r = engine.Execute(code, "<main>", ct);
        return (r, sb.ToString());
    }

    /// <summary>Runs code; returns stdout, plus "!Type: message" when an exception escaped (mirrors the CPython generator).</summary>
    public static string Out(string code, PythonEngineOptions? options = null, IEnumerable<string>? inputs = null)
    {
        var (r, output) = Run(code, options, inputs);
        if (r.Status == ExecutionStatus.Error)
            output += "!" + r.Error!.TypeName + ": " + r.Error.Message;
        else if (r.Status != ExecutionStatus.Completed && r.Status != ExecutionStatus.Exited)
            output += "!" + r.Status;
        return output;
    }

    public static string Eval(string expr)
    {
        var engine = new PythonEngine();
        var r = engine.Evaluate(expr);
        Assert.True(r.Success, r.ToString());
        return r.ValueRepr ?? "None";
    }
}

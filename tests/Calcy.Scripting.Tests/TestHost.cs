using System.Text;
using Calcy.Core;
using Calcy.Engine;

namespace Calcy.Scripting.Tests;

/// <summary>Creates hosts on fresh calculator sessions and collects their console output.</summary>
internal sealed class TestHost : IDisposable
{
    private readonly StringBuilder _out = new();

    public TestHost(ScriptHostOptions? options = null, CalcSession? session = null)
    {
        Session = session ?? CalcyEngine.CreateSession();
        Host = new ScriptHost(Session, options ?? ScriptHostOptions.Default with { Timeout = TimeSpan.FromSeconds(20) });
        Host.Output += (_, e) =>
        {
            lock (_out)
            {
                if (e.Kind == ScriptOutputKind.Math) _out.Append("[math ").Append(e.Text).Append("]\n");
                else _out.Append(e.Text);
            }
        };
    }

    public CalcSession Session { get; }

    public ScriptHost Host { get; }

    public string Output
    {
        get { lock (_out) return _out.ToString(); }
    }

    public ScriptRunResult Run(string code, string file = "main.py")
    {
        lock (_out) _out.Clear();
        return Host.Run(code, file);
    }

    /// <summary>Runs code that must succeed and returns its output.</summary>
    public string Out(string code)
    {
        var r = Run(code);
        Assert.True(r.Success, r.Output);
        return Output;
    }

    public void Dispose() => Host.Dispose();
}

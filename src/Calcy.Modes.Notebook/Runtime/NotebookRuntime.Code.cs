using System.Globalization;
using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Modes.Notebook.Model;
using Calcy.Python;
using Calcy.Scripting;

namespace Calcy.Modes.Notebook.Runtime;

public sealed partial class NotebookRuntime
{
    /// <summary>
    /// Runs a code cell in the shared interpreter. The calculator variables it sees are those of the cells above it;
    /// what it sets with calc.set is visible to the cells below. Returns null when something else is running.
    /// </summary>
    public async Task<ScriptRunResult?> RunAsync(CodeCell cell, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (_disposed || _running is not null || !Document.Cells.Contains(cell)) return null;
        _running = cell;
        cell.IsRunning = true;
        cell.State = CellState.Running;
        cell.StateMessage = "Running";
        cell.RunSummary = "running";
        cell.Output.Clear();
        cell.PreviousOutput = null;
        cell.RaiseResultsChanged();
        RunStateChanged?.Invoke(this, EventArgs.Empty);
        ScriptRunResult? result = null;
        try
        {
            if (_dirty || cell.EnvBefore is null) await RecalculateAsync().ConfigureAwait(true);
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                result = await RunCoreAsync(cell, cancellationToken).ConfigureAwait(true);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or ObjectDisposedException)
        {
            cell.Output.Add(CodeOutputKind.Failure, ex is OperationCanceledException ? "Stopped" : "The interpreter is not available: " + ex.Message);
        }
        finally
        {
            cell.IsRunning = false;
            _running = null;
            if (cell.LastRun is null)
            {
                cell.State = CellState.Error;
                cell.StateMessage = "The cell could not run";
                cell.RunSummary = "did not run";
            }
            cell.Output.Touch();
            RunStateChanged?.Invoke(this, EventArgs.Empty);
        }
        _dirty = true;
        await RecalculateAsync().ConfigureAwait(true);
        cell.RaiseResultsChanged();
        return result;
    }

    private async Task<ScriptRunResult> RunCoreAsync(CodeCell cell, CancellationToken ct)
    {
        var env = Session.Env;
        (cell.EnvBefore ?? EnvState.Empty).RestoreInto(env);
        var before = EnvState.Capture(env);
        var code = cell.Code;
        var reads = Dependencies.Names(Dependencies.Identifiers(code), before).ToDictionary(n => n, before.Lookup, StringComparer.Ordinal);
        var pyBefore = Host.GetVariables().ToDictionary(v => v.Name, v => v.Repr, StringComparer.Ordinal);
        var functionsBefore = Host.CalcFunctions.ToList();
        Host.Canvas.Reset();
        Host.Graphs.Clear();
        Host.Keyboard.Clear();
        _usedInput = false;

        var r = await Host.ExecuteInteractiveAsync(code.EndsWith('\n') ? code : code + "\n", ct).ConfigureAwait(true);

        // The value of a final expression is shown typeset when it is a calculator result.
        if (r.MathValue is { } math && r.ValueRepr is { } repr && cell.Output.RemoveTrailing(repr + "\n"))
            cell.Output.Add(CodeOutputKind.Math, math);
        if (Host.Canvas.HasDrawing) cell.Output.Add(CodeOutputKind.Drawing, frame: Host.Canvas.Capture());
        var graphs = Host.Graphs.Items;
        if (graphs.Count > 0)
        {
            var graphSession = new CalcSession(_settings, Session.Functions, new CalcEnvironment());
            EnvState.Capture(env).RestoreInto(graphSession.Env);
            cell.Output.Add(CodeOutputKind.Graph, graphs: graphs, window: Host.Graphs.Window, graphSession: graphSession);
        }
        if (!r.Success) cell.Output.Add(CodeOutputKind.Failure, FailureText(r));

        var after = EnvState.Capture(env);
        var effects = EnvState.Diff(before, after).Where(e => e.Name != "ans").ToList();
        var defines = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in Host.GetVariables())
            if (!pyBefore.TryGetValue(v.Name, out var old) || old != v.Repr) defines.Add(v.Name);
        foreach (var name in pyBefore.Keys)
            if (Host.GetVariables().All(v => v.Name != name)) defines.Add(name);
        if (!functionsBefore.SequenceEqual(Host.CalcFunctions)) _generation++;

        cell.LastRun = new CodeRunRecord(code, reads, effects, defines, ++_sequence, r.Elapsed, r.Status, r.Success, _usedInput);
        cell.RunSummary = Summary(r);
        return r;
    }

    private string FailureText(ScriptRunResult r) => r.Status switch
    {
        ExecutionStatus.Cancelled => "KeyboardInterrupt: stopped with STOP.",
        ExecutionStatus.TimedOut => $"Stopped: the cell ran longer than {Host.Options.Timeout?.TotalSeconds ?? 0:0.#} seconds.",
        ExecutionStatus.StepLimitExceeded => "Stopped: the cell ran too many steps.",
        ExecutionStatus.Exited => $"Exited with code {r.ExitCode}",
        _ => r.Error?.Traceback.TrimEnd('\n').Replace("\"<stdin>\"", "<cell>", StringComparison.Ordinal) ?? r.Summary,
    };

    private static string Summary(ScriptRunResult r)
    {
        string time = r.Elapsed.TotalMilliseconds < 1000
            ? string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, (int)Math.Round(r.Elapsed.TotalMilliseconds))} ms")
            : string.Create(CultureInfo.InvariantCulture, $"{r.Elapsed.TotalSeconds:0.00} s");
        return r.Status switch
        {
            ExecutionStatus.Cancelled => "stopped",
            ExecutionStatus.TimedOut => "time limit",
            ExecutionStatus.StepLimitExceeded => "step limit",
            ExecutionStatus.Error => "error after " + time,
            _ => "ran in " + time,
        };
    }

    /// <summary>
    /// Runs every code cell from the top with fresh Python globals (calculator results are recalculated first).
    /// Stops at the first cell that fails.
    /// </summary>
    public async Task RunAllAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || _running is not null) return;
        try
        {
            Host.Reset();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            return;
        }
        _dirty = true;
        await RecalculateAsync().ConfigureAwait(true);
        foreach (var cell in Document.Cells.OfType<CodeCell>().ToList())
        {
            if (cancellationToken.IsCancellationRequested || !Document.Cells.Contains(cell)) break;
            var r = await RunAsync(cell, cancellationToken).ConfigureAwait(true);
            if (r is null || !r.Success) break;
        }
    }

    /// <summary>Runs the stale code cells that may run by themselves (see <see cref="AutoRunCandidates"/>), top to bottom.</summary>
    public async Task RunStaleAsync(CancellationToken cancellationToken = default)
    {
        var done = new HashSet<CodeCell>();
        while (!_disposed && _running is null && !cancellationToken.IsCancellationRequested)
        {
            var next = AutoRunCandidates.FirstOrDefault(c => !done.Contains(c));
            if (next is null) break;
            done.Add(next);
            await RunAsync(next, cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>Stops the running code cell.</summary>
    public void Stop()
    {
        if (_running is null) return;
        Host.Stop();
    }

    /// <summary>Clears the output of every code cell; they count as not run again (values they set are dropped).</summary>
    public void ClearOutputs()
    {
        if (_running is not null) return;
        foreach (var c in Document.Cells.OfType<CodeCell>())
        {
            c.Output.Clear();
            c.LastRun = null;
            c.PreviousOutput = null;
            c.RunSummary = null;
            c.State = CellState.NotRun;
            c.RaiseResultsChanged();
        }
        MarkDirty();
    }

    // ------------------------------------------------------------------ interpreter callbacks (interpreter thread)

    private void OnHostOutput(object? sender, ScriptOutputEventArgs e)
    {
        var cell = _running;
        if (cell is null) return;
        switch (e.Kind)
        {
            case ScriptOutputKind.Standard:
            case ScriptOutputKind.Prompt:
                cell.Output.AppendText(CodeOutputKind.Text, e.Text);
                break;
            case ScriptOutputKind.Error:
                cell.Output.AppendText(CodeOutputKind.ErrorText, e.Text);
                break;
            case ScriptOutputKind.Math:
                cell.Output.Add(CodeOutputKind.Math, e.Text, e.Label);
                break;
            case ScriptOutputKind.Clear:
                cell.Output.Clear();
                break;
            case ScriptOutputKind.Input:
                // The answer is shown in the input row.
                break;
        }
    }

    private async Task<string?> OnInputAsync(ScriptInputRequest request, CancellationToken token)
    {
        var cell = _running;
        if (cell is null) return null;
        _usedInput = true;
        cell.Output.RemoveTrailing(request.Prompt);
        var item = cell.Output.Add(CodeOutputKind.Input, prompt: request.Prompt);
        var answer = item.BeginWaiting();
        using var registration = token.Register(() => item.Respond(null));
        if (InputHandler is { } handler)
        {
            string? text;
            try
            {
                text = await handler(cell, request.Prompt, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                text = null;
            }
            item.Respond(text);
        }
        return await answer.ConfigureAwait(false);
    }
}

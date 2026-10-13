using System.Globalization;
using System.Text.RegularExpressions;
using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;
using Calcy.Core.Parsing;
using Calcy.Graphing;
using Calcy.Modes.Notebook.Model;
using Calcy.Python;
using Calcy.Scripting;
using TableCell = Calcy.Modes.Notebook.Model.TableCell;
using TableRow = Calcy.Modes.Notebook.Model.TableRow;

namespace Calcy.Modes.Notebook.Runtime;

/// <summary>
/// Runs one notebook: its own calculator session (variables are scoped to the notebook) and one sandboxed Python
/// interpreter shared by all its code cells. Recalculation replays the cells from the top in order: each maths line,
/// table row, live value and graph sees exactly the definitions above it; results whose inputs did not change are
/// reused, so a change only recomputes what depends on it. Code cells never run during recalculation: what their
/// last run set is replayed instead, and they are marked stale when what they read has changed.
/// </summary>
/// <remarks>
/// Call the public members from one thread (the UI thread in the app). <see cref="RecalculateAsync"/> computes on a
/// worker thread and applies the results back on the calling thread's synchronization context.
/// </remarks>
public sealed partial class NotebookRuntime : IDisposable
{
    /// <summary>Longest time one maths line may take before it is stopped.</summary>
    public static readonly TimeSpan LineTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Code cells that ran faster than this run again by themselves when values they read change.</summary>
    public static readonly TimeSpan AutoRunLimit = TimeSpan.FromSeconds(3);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<object, CacheEntry> _cache = [];
    private readonly CalcSettings _settings;
    private CancellationTokenSource? _recalcCts;
    private CodeCell? _running;
    private int _recalcs;
    private int _generation;
    private long _sequence;
    private bool _dirty = true;
    private bool _usedInput;
    private bool _disposed;

    private sealed record CacheEntry(DependencyKey Key, CalcResult Result, IReadOnlyList<EnvEffect> Effects);

    /// <summary>Creates the runtime for a notebook. The settings object is shared with the app (angle unit and so on).</summary>
    public NotebookRuntime(NotebookDocument document, CalcSettings settings, ScriptHostOptions? options = null)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Session = Engine.CalcyEngine.CreateSession(settings);
        Host = new ScriptHost(Session, options ?? DefaultOptions);
        Host.Output += OnHostOutput;
        Host.InputProvider = OnInputAsync;
        foreach (var c in document.Cells) Attach(c);
        document.Changed += OnDocumentChanged;
    }

    /// <summary>Script limits for notebooks: drawings appear at once (no turtle animation), 30 seconds per run.</summary>
    public static ScriptHostOptions DefaultOptions { get; } = ScriptHostOptions.Default with { AnimateTurtle = false, Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>The notebook.</summary>
    public NotebookDocument Document { get; }

    /// <summary>The notebook's calculator session (after a recalculation it holds the definitions of every cell).</summary>
    public CalcSession Session { get; }

    /// <summary>The Python interpreter shared by the code cells.</summary>
    public ScriptHost Host { get; }

    /// <summary>The code cell that is running, if any.</summary>
    public CodeCell? RunningCell => _running;

    /// <summary>True while a recalculation or a code cell runs.</summary>
    public bool IsBusy => _recalcs > 0 || _running is not null;

    /// <summary>True when inputs changed since the last recalculation.</summary>
    public bool IsDirty => _dirty;

    /// <summary>
    /// Answers input() for code cells when set (tests, automation). When null, the request waits for
    /// <see cref="CodeOutputItem.Respond"/> on its output item (the screen shows an input box).
    /// </summary>
    public Func<CodeCell, string, CancellationToken, Task<string?>>? InputHandler { get; set; }

    /// <summary>Raised when inputs change and a recalculation is needed.</summary>
    public event EventHandler? RecalculationNeeded;

    /// <summary>Raised after new results have been applied to the cells.</summary>
    public event EventHandler? Recalculated;

    /// <summary>Raised when a code cell starts or stops running.</summary>
    public event EventHandler? RunStateChanged;

    /// <summary>Code cells whose results are out of date because something they read changed.</summary>
    public IReadOnlyList<CodeCell> StaleCodeCells => [.. Document.Cells.OfType<CodeCell>().Where(c => c.State == CellState.Stale)];

    /// <summary>Code cells that have not run in this session.</summary>
    public IReadOnlyList<CodeCell> UnrunCodeCells => [.. Document.Cells.OfType<CodeCell>().Where(c => c.LastRun is null && !c.IsRunning)];

    /// <summary>
    /// Stale code cells that may run again by themselves: they ran successfully in this session, did not ask for input,
    /// were quick, and their code has not been edited since.
    /// </summary>
    public IReadOnlyList<CodeCell> AutoRunCandidates => [.. Document.Cells.OfType<CodeCell>().Where(c =>
        c.State == CellState.Stale && c.LastRun is { Success: true, UsedInput: false } r && r.Elapsed < AutoRunLimit && r.Code == c.Code)];

    // ================================================================== document events

    private void OnDocumentChanged(object? sender, NotebookChangedEventArgs e)
    {
        if (e.Kind == NotebookChangeKind.Meta) return;
        if (e.Kind == NotebookChangeKind.Structure && e.Cell is { } c && Document.Cells.Contains(c)) Attach(c);
        MarkDirty();
    }

    /// <summary>Asks for a recalculation (for example after the angle unit or another setting changed).</summary>
    public void Invalidate() => MarkDirty();

    private void MarkDirty()
    {
        _dirty = true;
        RecalculationNeeded?.Invoke(this, EventArgs.Empty);
    }

    private void Attach(NotebookCell cell)
    {
        if (cell is GraphCell g && g.Document is null)
            g.Bind(new CalcSession(_settings, Session.Functions, new CalcEnvironment()));
    }

    // ================================================================== recalculation

    /// <summary>Recalculates on the calling thread (used by tests and tools).</summary>
    public void Recalculate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gate.Wait();
        _recalcs++;
        try
        {
            for (int pass = 0; pass < 3 && (pass == 0 || _dirty); pass++)
            {
                var plan = BuildPlan();
                _dirty = false;
                var outcome = Compute(plan, CancellationToken.None);
                Apply(plan, outcome);
            }
        }
        finally
        {
            _recalcs--;
            _gate.Release();
        }
        Recalculated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Recalculates in the background and applies the results on the caller's context. A newer call cancels a
    /// calculation that is still running, so only the latest inputs are applied.
    /// </summary>
    public async Task RecalculateAsync()
    {
        if (_disposed) return;
        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _recalcCts, cts)?.Cancel();
        _recalcs++;
        bool applied = false;
        try
        {
            await _gate.WaitAsync().ConfigureAwait(true);
            try
            {
                for (int pass = 0; pass < 3 && (pass == 0 || _dirty); pass++)
                {
                    if (cts.IsCancellationRequested || _disposed) return;
                    var plan = BuildPlan();
                    _dirty = false;
                    List<Outcome> outcome;
                    try
                    {
                        outcome = await Task.Run(() => Compute(plan, cts.Token), cts.Token).ConfigureAwait(true);
                    }
                    catch (OperationCanceledException)
                    {
                        _dirty = true;
                        return;
                    }
                    Apply(plan, outcome);
                    applied = true;
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            _recalcs--;
            Interlocked.CompareExchange(ref _recalcCts, null, cts);
            cts.Dispose();
        }
        if (applied) Recalculated?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------------ plan (inputs captured on the caller's thread)

    private abstract record CellPlan(NotebookCell Cell, int Version);

    private sealed record TextPlan(TextCell Text, int V, string[] Live) : CellPlan(Text, V);

    private sealed record MathPlan(MathCell Maths, int V, (MathLine Line, string Text)[] Lines) : CellPlan(Maths, V);

    private sealed record TablePlan(TableCell Table, int V, (TableRow Row, string Name, string? Definition)[] Rows, (MathLine Line, string Text)[] Summary)
        : CellPlan(Table, V);

    private sealed record GraphPlan(GraphCell Graph, int V, string[] Rows, (string Name, double Value)[] Sliders, string[] Own) : CellPlan(Graph, V);

    private sealed record CodePlan(CodeCell CodeCell, int V, string Code, CodeRunRecord? Run, bool PythonStale) : CellPlan(CodeCell, V);

    private List<CellPlan> BuildPlan()
    {
        var plan = new List<CellPlan>();
        var codeRuns = new List<(CodeRunRecord Run, int Index)>();
        for (int i = 0; i < Document.Cells.Count; i++)
        {
            switch (Document.Cells[i])
            {
                case TextCell t:
                    plan.Add(new TextPlan(t, t.Version, [.. t.Markdown.LiveSources]));
                    break;
                case MathCell m:
                    plan.Add(new MathPlan(m, m.Version, [.. m.Lines.Select(l => (l, l.Text))]));
                    break;
                case TableCell tc:
                    plan.Add(new TablePlan(tc, tc.Version, [.. tc.Rows.Select(r => (r, r.Name, r.Definition))], [.. tc.Summary.Select(l => (l, l.Text))]));
                    break;
                case GraphCell g:
                    plan.Add(new GraphPlan(g, g.Version, [.. g.Expressions], [.. g.Sliders], [.. g.OwnSliderNames]));
                    break;
                case CodeCell c:
                {
                    bool pyStale = false;
                    if (c.LastRun is { } run)
                    {
                        var ids = Dependencies.Identifiers(c.Code);
                        pyStale = codeRuns.Any(a => a.Run.Sequence > run.Sequence && ids.Any(a.Run.PythonDefines.Contains));
                        codeRuns.Add((run, i));
                    }
                    plan.Add(new CodePlan(c, c.Version, c.Code, c.LastRun, pyStale));
                    break;
                }
            }
        }
        return plan;
    }

    // ------------------------------------------------------------------ compute (worker thread, holds the gate)

    private sealed class Outcome
    {
        public required EnvState Before { get; init; }
        public LineResult?[] Lines { get; set; } = [];
        public LineResult?[] Rows { get; set; } = [];
        public LiveValue[] Live { get; set; } = [];
        public EnvState? GraphEnv { get; set; }
        public CellState State { get; set; } = CellState.Ready;
        public string? Message { get; set; }
    }

    private sealed class Pass(CancellationToken token)
    {
        public CancellationToken Token { get; } = token;
        public HashSet<string> Tainted { get; } = new(StringComparer.Ordinal);
        public HashSet<object> Seen { get; } = [];
    }

    private List<Outcome> Compute(List<CellPlan> plan, CancellationToken ct)
    {
        var env = Session.Env;
        EnvState.Empty.RestoreInto(env);
        var pass = new Pass(ct);
        var outcomes = new List<Outcome>(plan.Count);
        foreach (var p in plan)
        {
            ct.ThrowIfCancellationRequested();
            var o = new Outcome { Before = EnvState.Capture(env) };
            switch (p)
            {
                case TextPlan t:
                    o.Live = [.. t.Live.Select((src, i) => LiveValueOf(t.Text, i, src, pass))];
                    Summarize(o, o.Live.Any(v => v.Error is not null && !v.Tainted), o.Live.Any(v => v.Tainted), o.Live.FirstOrDefault(v => v.Error is not null)?.Error);
                    break;
                case MathPlan m:
                    o.Lines = [.. m.Lines.Select(l => string.IsNullOrWhiteSpace(l.Text) ? null : Evaluate(l.Line, l.Text, pass))];
                    SummarizeLines(o, o.Lines);
                    break;
                case TablePlan tp:
                    o.Rows = [.. tp.Rows.Select(r => EvaluateRow(r.Row, r.Name, r.Definition, pass))];
                    o.Lines = [.. tp.Summary.Select(l => string.IsNullOrWhiteSpace(l.Text) ? null : Evaluate(l.Line, l.Text, pass))];
                    SummarizeLines(o, [.. o.Rows, .. o.Lines]);
                    break;
                case GraphPlan g:
                    ComputeGraph(g, o, pass);
                    break;
                case CodePlan c:
                    ComputeCode(c, o, pass);
                    break;
            }
            outcomes.Add(o);
        }
        foreach (var key in _cache.Keys.Where(k => !pass.Seen.Contains(k)).ToList()) _cache.Remove(key);
        return outcomes;
    }

    private static void SummarizeLines(Outcome o, IEnumerable<LineResult?> lines)
    {
        var list = lines.Where(l => l is not null).Select(l => l!).ToList();
        var error = list.FirstOrDefault(l => l.IsError && !l.Tainted);
        Summarize(o, error is not null, list.Any(l => l.Tainted), error?.Result.ErrorMessage);
    }

    private static void Summarize(Outcome o, bool error, bool tainted, string? errorMessage)
    {
        if (tainted)
        {
            o.State = CellState.Stale;
            o.Message = "Uses values from a code cell that has to run (again)";
        }
        else if (error)
        {
            o.State = CellState.Error;
            o.Message = errorMessage;
        }
    }

    /// <summary>Evaluates a line, or replays its cached effects when nothing it depends on changed.</summary>
    private LineResult Evaluate(object key, string text, Pass pass, bool expressionOnly = false)
    {
        var env = Session.Env;
        var state = EnvState.Capture(env);
        var dk = DependencyKey.Build(text, state, Dependencies.SettingsKey(_settings), _generation);
        bool tainted = dk.Names.Any(pass.Tainted.Contains);
        pass.Seen.Add(key);
        if (_cache.TryGetValue(key, out var hit) && hit.Key.Matches(dk))
        {
            EnvState.Apply(env, hit.Effects);
        }
        else
        {
            using var lineCts = CancellationTokenSource.CreateLinkedTokenSource(pass.Token);
            lineCts.CancelAfter(LineTimeout);
            CalcResult r;
            try
            {
                r = expressionOnly ? EvaluateExpression(text, lineCts.Token) : Session.Evaluate(text, lineCts.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One line that trips the engine must not stop the rest of the notebook.
                r = new CalcResult { Input = text, Kind = ResultKind.Error, ErrorMessage = "This could not be calculated: " + ex.Message };
            }
            // A newer recalculation took over: nothing from this one is kept.
            pass.Token.ThrowIfCancellationRequested();
            if (r.IsError && lineCts.IsCancellationRequested)
                r = r with { ErrorMessage = $"Stopped: this took longer than {LineTimeout.TotalSeconds:0} seconds" };
            var after = EnvState.Capture(env);
            hit = new CacheEntry(dk, r, EnvState.Diff(state, after));
            _cache[key] = hit;
        }
        if (tainted) foreach (var e in hit.Effects) pass.Tainted.Add(e.Name);
        return new LineResult(hit.Result, tainted);
    }

    private CalcResult EvaluateExpression(string text, CancellationToken ct)
    {
        try
        {
            var exact = Session.EvaluateExpression(text, false, ct);
            return new CalcResult
            {
                Input = text,
                Kind = ResultKind.Value,
                Exact = exact,
                Approx = Session.Approximate(exact, ct),
                ExactIsNice = CalcSession.IsNiceExact(exact),
            };
        }
        catch (CalcException ex)
        {
            return new CalcResult { Input = text, Kind = ResultKind.Error, ErrorMessage = ex.Message, ErrorPosition = ex.Position, ErrorLength = ex.Length, Hint = ex.Hint };
        }
        catch (Exception ex) when (ex is ArithmeticException or OperationCanceledException or InsufficientExecutionStackException)
        {
            return new CalcResult { Input = text, Kind = ResultKind.Error, ErrorMessage = ex is OperationCanceledException ? "Stopped" : "This cannot be calculated" };
        }
    }

    private LiveValue LiveValueOf(TextCell cell, int index, string source, Pass pass)
    {
        if (string.IsNullOrWhiteSpace(source)) return new LiveValue(source, null, null, "Write an expression after =", false);
        var r = Evaluate($"{cell.Id}#{index}", source, pass, expressionOnly: true);
        return r.IsError
            ? new LiveValue(source, null, null, r.Result.ErrorMessage, r.Tainted)
            : new LiveValue(source, r.Result.Exact, r.Result.Approx, null, r.Tainted);
    }

    private LineResult? EvaluateRow(TableRow row, string name, string? definition, Pass pass)
    {
        if (definition is null) return null;
        if (!IdentifierPattern().IsMatch(name.Trim()))
        {
            return new LineResult(new CalcResult
            {
                Input = definition,
                Kind = ResultKind.Error,
                ErrorMessage = $"'{name}' is not a variable name: use letters, digits and _, starting with a letter",
            }, false);
        }
        return Evaluate(row, definition, pass);
    }

    private void ComputeGraph(GraphPlan g, Outcome o, Pass pass)
    {
        var env = Session.Env;
        var graphEnv = o.Before.Without(g.Own);
        o.GraphEnv = graphEnv;
        // Functions defined by the rows (f(x) = ...) are visible below the graph, like any definition.
        var temp = new CalcSession(_settings, Session.Functions, new CalcEnvironment());
        graphEnv.RestoreInto(temp.Env);
        var ctx = temp.ContextWith(GraphParser.PlotVariableNames);
        foreach (var text in g.Rows)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            try
            {
                if (Parser.ParseStatement(text.Trim(), ctx) is FunctionDefStatement fd && !temp.Functions.Contains(fd.Name) && fd.Name is not ("x" or "y" or "r" or "t" or "θ"))
                    temp.Env.SetFunction(new UserFunction(fd.Name, fd.Parameters, fd.Body));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The graph shows the error on its row.
            }
        }
        var effects = EnvState.Diff(graphEnv, EnvState.Capture(temp.Env)).Where(e => e.Function is not null).ToList();
        foreach (var (name, value) in g.Sliders)
        {
            if (Array.IndexOf(GraphParser.PlotVariableNames, name) >= 0 || Session.Functions.Contains(name) || !double.IsFinite(value)) continue;
            effects.Add(new EnvEffect(name, ExactNumber(value), null));
        }
        bool tainted = Dependencies.Names(g.Rows.SelectMany(Dependencies.Identifiers), graphEnv).Any(pass.Tainted.Contains);
        EnvState.Apply(env, effects);
        if (tainted)
        {
            foreach (var e in effects) pass.Tainted.Add(e.Name);
            Summarize(o, false, true, null);
        }
    }

    private void ComputeCode(CodePlan c, Outcome o, Pass pass)
    {
        var env = Session.Env;
        var exports = Dependencies.PossibleExports(c.Code);
        if (c.Run is not { } run)
        {
            foreach (var n in exports) pass.Tainted.Add(n);
            o.State = CellState.NotRun;
            o.Message = "Not run yet: press RUN (Shift+Enter) or RUN ALL";
            return;
        }
        var reasons = new List<string>();
        if (run.Code != c.Code) reasons.Add("the code changed since it ran");
        var changed = run.Reads.Where(kv => !EnvState.SameValue(o.Before.Lookup(kv.Key), kv.Value)).Select(kv => kv.Key).ToList();
        if (changed.Count > 0) reasons.Add((changed.Count == 1 ? changed[0] + " has" : string.Join(", ", changed.Take(4)) + " have") + " changed");
        var names = Dependencies.Names(Dependencies.Identifiers(c.Code), o.Before);
        if (names.Any(pass.Tainted.Contains)) reasons.Add("it uses results that are out of date");
        if (c.PythonStale) reasons.Add("a code cell above it ran again");
        EnvState.Apply(env, run.Effects);
        if (reasons.Count > 0)
        {
            foreach (var e in run.Effects) pass.Tainted.Add(e.Name);
            foreach (var n in exports) pass.Tainted.Add(n);
            o.State = CellState.Stale;
            o.Message = "Out of date: " + string.Join("; ", reasons);
        }
        else if (!run.Success)
        {
            o.State = CellState.Error;
            o.Message = run.Status switch
            {
                ExecutionStatus.Cancelled => "Stopped",
                ExecutionStatus.TimedOut => "Stopped: time limit",
                ExecutionStatus.StepLimitExceeded => "Stopped: step limit",
                _ => "The code raised an error",
            };
        }
    }

    /// <summary>A slider value as an exact number (0.85 becomes 17/20).</summary>
    internal static Expr ExactNumber(double value)
    {
        var text = value.ToString("0.############", CultureInfo.InvariantCulture);
        try
        {
            return new NumberExpr(Number.Exact(BigRational.ParseDecimal(text)));
        }
        catch (FormatException)
        {
            return new NumberExpr(Number.Approx(value));
        }
    }

    // ------------------------------------------------------------------ apply (caller's thread)

    private void Apply(List<CellPlan> plan, List<Outcome> outcomes)
    {
        for (int i = 0; i < plan.Count; i++)
        {
            var p = plan[i];
            var o = outcomes[i];
            var cell = p.Cell;
            cell.EnvBefore = o.Before;
            if (!Document.Cells.Contains(cell)) continue;
            if (cell.Version != p.Version)
            {
                // Typed into while we worked: another pass follows.
                _dirty = true;
                continue;
            }
            switch (p)
            {
                case TextPlan:
                    ((TextCell)cell).LiveValues = o.Live;
                    break;
                case MathPlan m:
                    for (int k = 0; k < m.Lines.Length; k++) m.Lines[k].Line.Result = o.Lines[k];
                    break;
                case TablePlan t:
                    for (int k = 0; k < t.Rows.Length; k++) t.Rows[k].Row.Result = o.Rows[k];
                    for (int k = 0; k < t.Summary.Length; k++) t.Summary[k].Line.Result = o.Lines[k];
                    break;
                case GraphPlan g:
                    if (o.GraphEnv is { } ge && g.Graph.ApplyEnvironment(ge)) _dirty = true;
                    if (o.State == CellState.Ready && g.Graph.Document?.Items.FirstOrDefault(it => it.Kind == GraphKind.Error) is { } bad)
                    {
                        o.State = CellState.Error;
                        o.Message = bad.Error;
                    }
                    break;
                case CodePlan c:
                    if (c.CodeCell.IsRunning) continue;
                    break;
            }
            cell.State = o.State;
            cell.StateMessage = o.Message;
            cell.RaiseResultsChanged();
        }
    }

    [GeneratedRegex(@"^[\p{L}_][\p{L}\p{N}_]*$")]
    private static partial Regex IdentifierPattern();

    // ================================================================== disposal

    /// <summary>Stops any running code and releases the interpreter.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _recalcCts?.Cancel();
        Document.Changed -= OnDocumentChanged;
        Host.Dispose();
    }
}

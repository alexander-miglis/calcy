using System.Collections.Concurrent;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Calcy.Modes.Code.Views;
using Calcy.Python;
using Calcy.Scripting;
using Calcy.UI.Controls;

namespace Calcy.Modes.Code;

public sealed partial class CodeView
{
    private const int MaxCards = 30;
    private const int MaxHistory = 200;

    private readonly ConcurrentQueue<ConsoleItem> _queue = new();
    private readonly DispatcherTimer _flush = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly StackPanel _cards = new() { Spacing = 10, Margin = new Thickness(10) };
    private readonly ScrollViewer _cardScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly List<string> _history = [];
    private TextBox _repl = null!;
    private RunCard? _active;
    private RunCard? _replCard;
    private int _runNumber = 1;
    private int _historyIndex = -1;
    private bool _running;
    private bool _replRunning;
    private string? _runningFile;

    /// <summary>Something the interpreter thread sends to the console, handled in order on the UI thread.</summary>
    private abstract record ConsoleItem;

    private sealed record TextItem(ScriptOutputKind Kind, string Text, string? Label) : ConsoleItem;

    private sealed record InputItem(string Prompt, TaskCompletionSource<string?> Answer, CancellationToken Token) : ConsoleItem;

    /// <summary>The run cards, newest first.</summary>
    internal IEnumerable<RunCard> Cards => _cards.Children.OfType<RunCard>();

    /// <summary>The REPL input box.</summary>
    internal TextBox ReplBox => _repl;

    /// <summary>True while a program runs.</summary>
    public bool IsRunning => _running;

    private Control BuildRunsPanel()
    {
        _cardScroll.Content = _cards;
        var clear = Ui.SmallButton("CLEAR", "Remove every run card");
        clear.Click += (_, _) =>
        {
            foreach (var c in Cards.Where(c => !c.IsRunning).ToList()) _cards.Children.Remove(c);
            _replCard = null;
            _saveLater.Trigger();
        };

        _repl = new TextBox
        {
            Classes = { "repl" },
            PlaceholderText = "type Python here to try a line",
            FontSize = 14,
            MinHeight = 34,
            Padding = new Thickness(8, 4),
            BorderThickness = new Thickness(0),
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _repl.Bind(TextBox.FontFamilyProperty, "Calcy.Font.Math");
        Avalonia.Automation.AutomationProperties.SetName(_repl, "Python line");
        _repl.AddHandler(KeyDownEvent, OnReplKeyDown, RoutingStrategies.Tunnel);
        var frame = new DashedFrame { Child = _repl };
        frame.Bind(DashedFrame.StrokeProperty, "Calcy.Muted");
        var prompt = Ui.Mono(">>>", 15, bold: true);
        prompt.VerticalAlignment = VerticalAlignment.Center;
        var replRow = new DockPanel { Margin = new Thickness(10, 8) };
        DockPanel.SetDock(prompt, Dock.Left);
        prompt.Margin = new Thickness(0, 0, 8, 0);
        replRow.Children.Add(prompt);
        replRow.Children.Add(frame);
        var replBar = new Border { Child = replRow, BorderThickness = new Thickness(0, 3, 0, 0) };
        replBar.Bind(Border.BorderBrushProperty, "Calcy.Line");

        var dock = new DockPanel();
        DockPanel.SetDock(replBar, Dock.Bottom);
        dock.Children.Add(replBar);
        dock.Children.Add(_cardScroll);
        var panel = new PixelPanel { Header = "Runs", Content = dock, HeaderContent = clear, MinHeight = 220 };
        panel.Bind(PixelPanel.HeaderBrushProperty, "Calcy.Sel");
        Avalonia.Automation.AutomationProperties.SetName(panel, "Console");
        return panel;
    }

    private void AddCard(RunCard card)
    {
        card.LineLinkClicked += (_, line) => GoToLine(card.FileName, line);
        card.RerunClicked += async (_, _) =>
        {
            if (!Open(card.FileName)) return;
            await RunCurrentAsync();
        };
        _cards.Children.Insert(0, card);
        while (_cards.Children.Count > MaxCards)
        {
            var last = _cards.Children[^1];
            if (last is RunCard { IsRunning: true }) break;
            _cards.Children.RemoveAt(_cards.Children.Count - 1);
            if (last == _replCard) _replCard = null;
        }
        _cardScroll.Offset = default;
    }

    /// <summary>Shows a line of a program in the editor (from a traceback link).</summary>
    public void GoToLine(string fileName, int line)
    {
        if (_current?.Name != fileName && !Open(fileName)) return;
        _editor.GoToLine(line);
        _editor.FocusEditor();
    }

    // ------------------------------------------------------------------ running

    /// <summary>Runs the open program (F5). Returns the result, or null when something was already running.</summary>
    public async Task<ScriptRunResult?> RunCurrentAsync()
    {
        if (_running || _replRunning) return null;
        SyncSource();
        var program = _current;
        var source = program.Source;
        _running = true;
        _runningFile = program.Name;
        _editor.ClearError();
        var card = new RunCard(_runNumber++, program.Name, _s.Session);
        AddCard(card);
        _active = card;
        _replCard = null;
        UpdateRunState();
        SetStatus($"Running {program.Name} …");
        _canvasView.Refresh();
        if (UsesKeys(source))
        {
            ShowSide(SidePanel.Canvas);
            Dispatcher.UIThread.Post(() => _canvasView.Focus(), DispatcherPriority.Input);
        }
        _flush.Start();
        ScriptRunResult result;
        try
        {
            result = await _host.RunAsync(source, program.Name);
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
        {
            _running = false;
            _flush.Stop();
            card.Finish(new ScriptRunResult { Kind = ScriptRunKind.Program, FileName = program.Name, Status = ExecutionStatus.Error }, "error", e.Message);
            UpdateRunState();
            return null;
        }
        Flush();
        _flush.Stop();
        _running = false;
        _runningFile = null;
        _active = null;
        card.Finish(result, Meta(result), StopMessage(result, source));
        if (result.ErrorLine is { } line && !result.Success && result.Status is ExecutionStatus.Error or ExecutionStatus.TimedOut or ExecutionStatus.StepLimitExceeded)
        {
            string message = result.Status == ExecutionStatus.Error && result.Error is { } err ? $"{err.TypeName}: {err.Message}" : result.Summary;
            if (_current == program) _editor.SetError(line, result.Error?.IsSyntaxError == true ? result.ErrorColumn : null, message);
        }
        SetStatus(result.Status switch
        {
            ExecutionStatus.Completed or ExecutionStatus.Exited when result.Success => $"Finished {program.Name}",
            ExecutionStatus.Cancelled => "Stopped",
            ExecutionStatus.TimedOut => "Stopped: time limit",
            _ => result.ErrorLine is { } l ? $"Error on line {l}" : "Error",
        });
        _canvasView.Refresh();
        RefreshGraphs();
        RefreshVariables();
        if (result.GraphCount > 0) ShowSide(SidePanel.Graph);
        else if (result.Drew) ShowSide(SidePanel.Canvas);
        UpdateRunState();
        _saveLater.Trigger();
        if (_canvasView.IsFocused || card.PendingInput is not null) _editor.FocusEditor();
        return result;
    }

    /// <summary>Stops the running program or REPL line (Esc, Ctrl+Break, STOP).</summary>
    public void Stop()
    {
        if (!_running && !_replRunning) return;
        _host.Stop();
        SetStatus("Stopping …");
    }

    private static bool UsesKeys(string source) =>
        source.Contains("get_key", StringComparison.Ordinal) || source.Contains("wait_key", StringComparison.Ordinal) ||
        source.Contains("is_pressed", StringComparison.Ordinal) || source.Contains("onkey", StringComparison.Ordinal) ||
        source.Contains(".key()", StringComparison.Ordinal) || source.Contains("escape()", StringComparison.Ordinal);

    private void UpdateRunState()
    {
        bool busy = _running || _replRunning;
        _runButton.IsEnabled = !busy;
        _runLabel.Text = _running ? "WORKING" : "RUN";
        _stopButton.IsEnabled = busy;
        _repl.IsEnabled = !_running;
        _canvasView.Keyboard = _running ? _host.Keyboard : null;
    }

    private string Meta(ScriptRunResult r) => r.Status switch
    {
        ExecutionStatus.Cancelled => "stopped by you",
        ExecutionStatus.TimedOut => string.Create(CultureInfo.InvariantCulture, $"stopped at {r.Elapsed.TotalSeconds:0.0} s"),
        ExecutionStatus.StepLimitExceeded => "step limit",
        ExecutionStatus.Error => "error · " + Duration(r.Elapsed),
        ExecutionStatus.Exited when r.ExitCode is { } c && c != 0 => $"exit {c} · " + Duration(r.Elapsed),
        _ => Duration(r.Elapsed) + " · " + r.Steps.ToString("N0", CultureInfo.InvariantCulture) + (r.Steps == 1 ? " step" : " steps"),
    };

    private static string Duration(TimeSpan t) => t.TotalMilliseconds < 1000
        ? string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, (int)Math.Round(t.TotalMilliseconds))} ms")
        : string.Create(CultureInfo.InvariantCulture, $"{t.TotalSeconds:0.00} s");

    private string? StopMessage(ScriptRunResult r, string source)
    {
        switch (r.Status)
        {
            case ExecutionStatus.Cancelled:
                return "KeyboardInterrupt: stopped with the Stop button.";
            case ExecutionStatus.TimedOut:
            {
                double limit = _host.Options.Timeout?.TotalSeconds ?? 0;
                var text = string.Create(CultureInfo.InvariantCulture, $"Stopped: the program ran longer than {limit:0.#} s.");
                if (r.ErrorLine is { } l && SourceLine(source, l) is { Length: > 0 } code) text += $"\n{code} on line {l} was still running.";
                return text + " Press Stop or Esc any time.";
            }
            case ExecutionStatus.StepLimitExceeded:
                return "Stopped: the program ran too many steps.";
            default:
                return null;
        }
    }

    private static string? SourceLine(string source, int line)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        return line >= 1 && line <= lines.Length ? lines[line - 1].Trim() : null;
    }

    // ------------------------------------------------------------------ console traffic

    private void OnHostOutput(object? sender, ScriptOutputEventArgs e) => _queue.Enqueue(new TextItem(e.Kind, e.Text, e.Label));

    private Task<string?> OnInputRequested(ScriptInputRequest request, CancellationToken token)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Enqueue(new InputItem(request.Prompt, tcs, token));
        Dispatcher.UIThread.Post(Flush);
        return tcs.Task;
    }

    private void Flush()
    {
        var card = _active;
        while (_queue.TryDequeue(out var item))
        {
            switch (item)
            {
                case TextItem t when card is not null:
                    switch (t.Kind)
                    {
                        case ScriptOutputKind.Math:
                            card.AppendMath(t.Text, t.Label);
                            break;
                        case ScriptOutputKind.Clear:
                            card.ClearOutput();
                            break;
                        default:
                            card.Append(t.Kind, t.Text);
                            break;
                    }
                    break;
                case InputItem input:
                    if (card is null)
                    {
                        input.Answer.TrySetResult(null);
                        break;
                    }
                    var task = card.RequestInput(input.Prompt, input.Token);
                    task.ContinueWith(a => input.Answer.TrySetResult(a.Result), TaskScheduler.Default);
                    SetStatus("Waiting for input …");
                    break;
            }
        }
        card?.CommitText();
        if (_graphsDirty && !_running) RefreshGraphs();
    }

    // ------------------------------------------------------------------ REPL

    private async void OnReplKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
            {
                e.Handled = true;
                var text = _repl.Text ?? "";
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || (text.Trim().Length > 0 && !_host.IsInputComplete(text + "\n")))
                {
                    // An unfinished block (def, for, if ...): continue on the next line.
                    InsertReplNewline();
                    return;
                }
                await SubmitReplAsync(text);
                return;
            }
            case Key.Up when !(_repl.Text ?? "").Contains('\n'):
                e.Handled = true;
                StepHistory(-1);
                return;
            case Key.Down when !(_repl.Text ?? "").Contains('\n'):
                e.Handled = true;
                StepHistory(1);
                return;
            case Key.Escape when !_replRunning && (_repl.Text ?? "").Length > 0:
                e.Handled = true;
                _repl.Text = "";
                return;
        }
    }

    private void InsertReplNewline()
    {
        var text = _repl.Text ?? "";
        var lastLine = text.Split('\n')[^1];
        int indent = lastLine.Length - lastLine.TrimStart().Length;
        if (lastLine.TrimEnd().EndsWith(':')) indent += 4;
        _repl.AcceptsReturn = true;
        _repl.TextWrapping = TextWrapping.NoWrap;
        int caret = _repl.CaretIndex;
        var insert = "\n" + new string(' ', indent);
        _repl.Text = text.Insert(Math.Clamp(caret, 0, text.Length), insert);
        _repl.CaretIndex = caret + insert.Length;
    }

    private void StepHistory(int dir)
    {
        if (_history.Count == 0) return;
        if (_historyIndex < 0) _historyIndex = _history.Count;
        _historyIndex = Math.Clamp(_historyIndex + dir, 0, _history.Count);
        _repl.Text = _historyIndex == _history.Count ? "" : _history[_historyIndex];
        _repl.CaretIndex = (_repl.Text ?? "").Length;
    }

    /// <summary>Runs a REPL line in the program's globals and shows it in the REPL card.</summary>
    public async Task<ScriptRunResult?> SubmitReplAsync(string code)
    {
        code = code.TrimEnd();
        if (code.Trim().Length == 0 || _running || _replRunning) return null;
        _repl.Text = "";
        _repl.AcceptsReturn = false;
        if (_history.Count == 0 || _history[^1] != code) _history.Add(code);
        while (_history.Count > MaxHistory) _history.RemoveAt(0);
        _historyIndex = -1;
        if (_replCard is null || !_cards.Children.Contains(_replCard) || _cards.Children.IndexOf(_replCard) != 0)
        {
            _replCard = new RunCard(_runNumber++, "<stdin>", _s.Session, repl: true);
            AddCard(_replCard);
        }
        var card = _replCard;
        card.AppendReplInput(code);
        _active = card;
        _replRunning = true;
        UpdateRunState();
        _flush.Start();
        ScriptRunResult result;
        try
        {
            result = await _host.ExecuteInteractiveAsync(code + "\n");
        }
        finally
        {
            Flush();
            _flush.Stop();
            _replRunning = false;
            _active = null;
            UpdateRunState();
        }
        card.Finish(result, "", null);
        _canvasView.Refresh();
        RefreshGraphs();
        RefreshVariables();
        _saveLater.Trigger();
        _repl.Focus();
        return result;
    }

    // ------------------------------------------------------------------ keys

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        switch (e.Key)
        {
            case Key.F5:
                e.Handled = true;
                if (!_running) _ = RunCurrentAsync();
                return;
            case Key.Escape when _running || _replRunning:
                e.Handled = true;
                Stop();
                return;
            case Key.Escape when IsFindVisible:
                e.Handled = true;
                HideFind();
                return;
            case Key.Cancel or Key.Pause when ctrl || e.Key == Key.Cancel:
                e.Handled = true;
                Stop();
                return;
            case Key.F3:
                e.Handled = true;
                if (!IsFindVisible) ShowFind();
                else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _editor.FindPrevious();
                else _editor.FindNext();
                return;
            case Key.S when ctrl:
                e.Handled = true;
                SaveState();
                _s.ShowStatus("Programs are saved automatically");
                return;
            case Key.F when ctrl && e.Source is not TextBox:
                e.Handled = true;
                ShowFind();
                return;
        }
    }
}

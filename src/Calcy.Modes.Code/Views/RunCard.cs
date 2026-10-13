using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Calcy.Core;
using Calcy.Scripting;
using Calcy.UI.Controls;
using Calcy.UI.Math;

namespace Calcy.Modes.Code.Views;

/// <summary>
/// One run in the console (design D5): a numbered card with the program's output as it arrives, input prompts answered
/// inline, typeset calculator results, and when it ends the status, the duration, a traceback with clickable lines and
/// RE-RUN. The REPL uses one card that collects every line typed.
/// </summary>
internal sealed partial class RunCard : Card
{
    /// <summary>Characters kept per block of console text; older output scrolls away (only the end is kept).</summary>
    private const int MaxChars = 60_000;

    private readonly StackPanel _body = new() { Spacing = 4 };
    private readonly CalcSession _session;
    private readonly List<Segment> _segments = [];
    private SelectableTextBlock? _text;
    private int _blockChars;
    private bool _hiddenNote;
    private TextBox? _pendingInput;

    /// <summary>A run of console text of one kind; text is collected here and copied to the Run once per refresh.</summary>
    private sealed class Segment(Run run, ScriptOutputKind kind)
    {
        public Run Run { get; } = run;
        public ScriptOutputKind Kind { get; } = kind;
        public StringBuilder Text { get; } = new();
        public bool Dirty { get; set; }
    }

    public RunCard(int number, string file, CalcSession session, bool repl = false)
    {
        Number = number;
        FileName = file;
        IsRepl = repl;
        _session = session;
        Title = repl ? $"#{number}  REPL" : $"#{number}  {file}";
        Meta = repl ? "" : "WORKING";
        Content = _body;
        Padding = new Thickness(10, 8);
        Avalonia.Automation.AutomationProperties.SetName(this, Title);
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(Card);

    /// <summary>The run number.</summary>
    public int Number { get; }

    /// <summary>The program's file name.</summary>
    public string FileName { get; }

    /// <summary>True for the REPL card.</summary>
    public bool IsRepl { get; }

    /// <summary>True while the run is going.</summary>
    public bool IsRunning { get; private set; } = true;

    /// <summary>The result, once finished.</summary>
    public ScriptRunResult? Result { get; private set; }

    /// <summary>Raised when a traceback line link is clicked (1 based line).</summary>
    public event EventHandler<int>? LineLinkClicked;

    /// <summary>Raised when RE-RUN is clicked.</summary>
    public event EventHandler? RerunClicked;

    /// <summary>The visible console text of the card (for tests and copying).</summary>
    public string ConsoleText
    {
        get
        {
            CommitText();
            var sb = new StringBuilder();
            foreach (var child in _body.Children) Collect(child, sb);
            return sb.ToString();
        }
    }

    private static void Collect(Control c, StringBuilder sb)
    {
        switch (c)
        {
            case SelectableTextBlock t:
                sb.Append(t.Inlines is { Count: > 0 } ins ? string.Concat(ins.OfType<Run>().Select(r => r.Text)) : t.Text);
                break;
            case TextBlock tb:
                sb.Append(tb.Text).Append('\n');
                break;
            case MathView m:
                sb.Append("[math ").Append(m.Text).Append("]\n");
                break;
            case TextBox box:
                sb.Append(box.Text);
                break;
            case Panel { Tag: FooterTag }:
                break;
            case Panel { Tag: InputTag } row:
                sb.Append(string.Concat(row.Children.OfType<TextBlock>().Select(t => t.Text))).Append('\n');
                break;
            case Panel p:
                foreach (var ch in p.Children) Collect(ch, sb);
                if (p is StackPanel { Orientation: Orientation.Horizontal } or WrapPanel) sb.Append('\n');
                break;
            case Decorator { Child: { } inner }:
                Collect(inner, sb);
                break;
            case ContentControl { Content: Control cc }:
                Collect(cc, sb);
                break;
        }
    }

    // ------------------------------------------------------------------ output

    /// <summary>Adds console text of a kind (standard, error, prompt or answer); call <see cref="CommitText"/> to show it.</summary>
    public void Append(ScriptOutputKind kind, string text)
    {
        if (text.Length == 0) return;
        if (kind == ScriptOutputKind.Input && _answeredInline)
        {
            // The answer is already shown in the inline input row.
            _answeredInline = false;
            return;
        }
        _text ??= NewTextBlock();
        Segment segment;
        if (_segments.Count > 0 && _segments[^1].Kind == kind) segment = _segments[^1];
        else
        {
            var run = new Run();
            switch (kind)
            {
                case ScriptOutputKind.Error:
                    run.Bind(TextElement.ForegroundProperty, run.GetResourceObservable("Calcy.Err"));
                    break;
                case ScriptOutputKind.Input:
                    run.FontWeight = FontWeight.Bold;
                    run.Bind(TextElement.ForegroundProperty, run.GetResourceObservable("Calcy.G1"));
                    break;
            }
            _text.Inlines!.Add(run);
            segment = new Segment(run, kind);
            _segments.Add(segment);
        }
        segment.Text.Append(text);
        segment.Dirty = true;
        _blockChars += text.Length;
        if (_blockChars > MaxChars) TrimFront(_blockChars - MaxChars * 3 / 4);
    }

    /// <summary>Copies collected text into the visible runs (done once per console refresh, not per print).</summary>
    public void CommitText()
    {
        foreach (var seg in _segments)
        {
            if (!seg.Dirty) continue;
            seg.Run.Text = seg.Text.ToString();
            seg.Dirty = false;
        }
    }

    private void TrimFront(int excess)
    {
        while (excess > 0 && _segments.Count > 0)
        {
            var first = _segments[0];
            if (first.Text.Length <= excess && _segments.Count > 1)
            {
                excess -= first.Text.Length;
                _blockChars -= first.Text.Length;
                _text!.Inlines!.Remove(first.Run);
                _segments.RemoveAt(0);
                continue;
            }
            int cut = Math.Min(excess, first.Text.Length);
            // Cut at a line break when one is near, so the kept output starts on a whole line.
            for (int i = cut; i < Math.Min(first.Text.Length, cut + 400); i++)
            {
                if (first.Text[i] != '\n') continue;
                cut = i + 1;
                break;
            }
            first.Text.Remove(0, cut);
            first.Dirty = true;
            _blockChars -= cut;
            excess = 0;
        }
        if (!_hiddenNote && _text is not null)
        {
            _hiddenNote = true;
            var note = Note("Earlier output is hidden; the console keeps the most recent part.");
            _body.Children.Insert(Math.Max(0, _body.Children.IndexOf(_text)), note);
        }
    }

    /// <summary>Removes text from the end of the last run when it ends with it (a prompt or a REPL echo).</summary>
    private bool RemoveTrailing(string suffix)
    {
        if (suffix.Length == 0 || _segments.Count == 0) return false;
        var last = _segments[^1];
        var sb = last.Text;
        if (sb.Length < suffix.Length || sb.ToString(sb.Length - suffix.Length, suffix.Length) != suffix) return false;
        sb.Length -= suffix.Length;
        _blockChars -= suffix.Length;
        last.Dirty = true;
        if (sb.Length == 0)
        {
            _text!.Inlines!.Remove(last.Run);
            _segments.RemoveAt(_segments.Count - 1);
        }
        if (_segments.Count == 0 && _text is not null)
        {
            _body.Children.Remove(_text);
            _text = null;
            _blockChars = 0;
            _hiddenNote = false;
        }
        return true;
    }

    private bool _answeredInline;

    private SelectableTextBlock NewTextBlock()
    {
        var t = new SelectableTextBlock
        {
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Inlines = [],
        };
        t.Bind(TextBlock.FontFamilyProperty, "Calcy.Font.Math");
        t.Bind(TextBlock.ForegroundProperty, "Calcy.Ink");
        t.Bind(SelectableTextBlock.SelectionBrushProperty, "Calcy.Sel");
        _body.Children.Add(t);
        _segments.Clear();
        _blockChars = 0;
        _hiddenNote = false;
        return t;
    }

    private void EndTextBlock()
    {
        CommitText();
        if (_segments.Count > 0 && _segments[^1].Run.Text is { } s && s.EndsWith('\n'))
            _segments[^1].Run.Text = s[..^1];
        _segments.Clear();
        _text = null;
        _blockChars = 0;
    }

    /// <summary>Adds a typeset calculator result.</summary>
    public void AppendMath(string calcText, string? label)
    {
        EndTextBlock();
        var view = new MathView { Text = calcText, Session = _session, FontSize = 18, Margin = new Thickness(0, 2), HorizontalAlignment = HorizontalAlignment.Left };
        view.Bind(MathView.ForegroundProperty, "Calcy.Ink");
        if (label is null)
        {
            _body.Children.Add(view);
            return;
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(Ui.Mono(label, 14));
        row.Children.Add(view);
        _body.Children.Add(row);
    }

    /// <summary>Clears the card's output (ti_system.disp_clr).</summary>
    public void ClearOutput()
    {
        _body.Children.Clear();
        _segments.Clear();
        _text = null;
        _blockChars = 0;
    }

    /// <summary>Shows an input box after the prompt and completes with the line typed (null when the run stops).</summary>
    public Task<string?> RequestInput(string prompt, CancellationToken token)
    {
        // The prompt was printed as output just before; it moves into the input row so the answer sits next to it.
        RemoveTrailing(prompt);
        EndTextBlock();
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var row = new DockPanel { LastChildFill = true, Tag = InputTag };
        var promptText = Ui.Mono(prompt, 14);
        DockPanel.SetDock(promptText, Dock.Left);
        var box = new TextBox
        {
            Classes = { "inline" },
            FontSize = 14,
            MinHeight = 26,
            Padding = new Thickness(4, 1),
            BorderThickness = new Thickness(0, 0, 0, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        box.Bind(TextBox.FontFamilyProperty, "Calcy.Font.Math");
        Avalonia.Automation.AutomationProperties.SetName(box, string.IsNullOrWhiteSpace(prompt) ? "Program input" : prompt.Trim());
        row.Children.Add(promptText);
        row.Children.Add(box);
        _body.Children.Add(row);
        _pendingInput = box;
        void Complete(string? value)
        {
            if (_pendingInput != box) return;
            _pendingInput = null;
            row.Children.Remove(box);
            if (value is not null)
            {
                var answer = Ui.Mono(value, 14, "Calcy.G1", bold: true);
                row.Children.Add(answer);
                _answeredInline = true;
            }
            tcs.TrySetResult(value);
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Complete(box.Text ?? "");
        };
        token.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(() => Complete(null)));
        Avalonia.Threading.Dispatcher.UIThread.Post(() => box.Focus(), Avalonia.Threading.DispatcherPriority.Input);
        return tcs.Task;
    }

    /// <summary>The input box waiting for an answer, if any.</summary>
    public TextBox? PendingInput => _pendingInput;

    /// <summary>Adds a REPL input line ("&gt;&gt;&gt; code").</summary>
    public void AppendReplInput(string code)
    {
        EndTextBlock();
        var lines = code.Replace("\r", "").Split('\n');
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Length; i++) sb.Append(i == 0 ? ">>> " : "... ").Append(lines[i]).Append(i < lines.Length - 1 ? "\n" : "");
        var t = Ui.Mono(sb.ToString(), 14, "Calcy.Muted", bold: true);
        t.TextWrapping = TextWrapping.Wrap;
        _body.Children.Add(t);
    }

    private void AddNote(string text)
    {
        EndTextBlock();
        _body.Children.Add(Note(text));
    }

    private static TextBlock Note(string text)
    {
        var t = Ui.Body(text, 12, "Calcy.Muted");
        t.FontStyle = FontStyle.Italic;
        return t;
    }

    // ------------------------------------------------------------------ finishing

    [GeneratedRegex("^\\s*File \"(?<file>[^\"]+)\", line (?<line>\\d+)")]
    private static partial Regex FrameLine();

    /// <summary>Shows how the run ended: meta (duration and steps), the traceback or stop message, and RE-RUN.</summary>
    public void Finish(ScriptRunResult r, string meta, string? stopMessage)
    {
        IsRunning = false;
        Result = r;
        _pendingInput = null;
        if (IsRepl)
        {
            FinishReplEntry(r);
            return;
        }
        Meta = meta;
        IsError = !r.Success;
        EndTextBlock();
        if (stopMessage is not null) AddMessage(stopMessage, r.ErrorLine);
        else if (r.Error is { } err && !r.Success) AddTraceback(err.Traceback, r.ErrorLine);
        if (_body.Children.Count == 0) AddNote("No output.");
        AddFooter(r.Status switch
        {
            Calcy.Python.ExecutionStatus.Completed => "FINISHED",
            Calcy.Python.ExecutionStatus.Exited => r.Success ? "EXITED" : $"EXIT CODE {r.ExitCode}",
            Calcy.Python.ExecutionStatus.Cancelled => "STOPPED",
            Calcy.Python.ExecutionStatus.TimedOut => "TIME LIMIT",
            Calcy.Python.ExecutionStatus.StepLimitExceeded => "STEP LIMIT",
            _ => r.ErrorLine is { } l ? $"ERROR ON LINE {l}" : "ERROR",
        }, !r.Success);
    }

    private void AddFooter(string status, bool failed)
    {
        var rerun = Ui.SmallButton("RE-RUN", "Run this program again");
        rerun.Click += (_, _) => RerunClicked?.Invoke(this, EventArgs.Empty);
        var label = Ui.Label(status, 10, failed ? "Calcy.Err" : "Calcy.Muted");
        var footer = new DockPanel { Margin = new Thickness(0, 6, 0, 0), Tag = FooterTag };
        DockPanel.SetDock(rerun, Dock.Right);
        footer.Children.Add(rerun);
        footer.Children.Add(label);
        _body.Children.Add(footer);
        _rerun = rerun;
    }

    private const string FooterTag = "footer";
    private const string InputTag = "input";

    private Button? _rerun;

    /// <summary>The RE-RUN button of a finished card.</summary>
    public Button? RerunButton => _rerun;

    private void FinishReplEntry(ScriptRunResult r)
    {
        if (r.MathValue is { } math && r.ValueRepr is { } repr)
        {
            // The echoed repr is replaced by the typeset result.
            RemoveTrailing(repr + "\n");
            AppendMath(math, null);
        }
        EndTextBlock();
        if (r.Status == Calcy.Python.ExecutionStatus.Cancelled) AddMessage("KeyboardInterrupt", null);
        else if (r.Error is { } err && !r.Success) AddTraceback(err.Traceback, null);
        Meta = "";
    }

    private void AddMessage(string message, int? line)
    {
        var t = Ui.Mono(message, 14, "Calcy.Err");
        t.TextWrapping = TextWrapping.Wrap;
        _body.Children.Add(t);
        if (line is { } l) _body.Children.Add(LineLink($"Go to line {l}", l));
    }

    private void AddTraceback(string traceback, int? errorLine)
    {
        var lines = traceback.TrimEnd('\n').Split('\n');
        var panel = new StackPanel { Spacing = 0 };
        for (int i = 0; i < lines.Length; i++)
        {
            var text = lines[i];
            var m = FrameLine().Match(text);
            bool last = i == lines.Length - 1;
            if (m.Success && m.Groups["file"].Value == FileName && int.TryParse(m.Groups["line"].Value, out var ln))
            {
                panel.Children.Add(LineLink(text.TrimStart(), ln));
                continue;
            }
            var t = Ui.Mono(text, last ? 14 : 13, last ? "Calcy.Err" : "Calcy.Muted", bold: last);
            t.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(t);
        }
        _body.Children.Add(panel);
    }

    private Control LineLink(string text, int line)
    {
        var t = Ui.Mono(text, 13, "Calcy.G1");
        t.TextDecorations = TextDecorations.Underline;
        t.TextWrapping = TextWrapping.Wrap;
        var link = new Button
        {
            Content = t,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 1),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Left,
            Tag = line,
        };
        link.Classes.Add("link");
        ToolTip.SetTip(link, $"Show line {line} in the editor");
        Avalonia.Automation.AutomationProperties.SetName(link, $"Go to line {line}");
        link.Click += (_, _) => LineLinkClicked?.Invoke(this, line);
        return link;
    }

    /// <summary>The traceback line links in the card (for tests).</summary>
    public IEnumerable<Button> LineLinks => _body.GetLogicalDescendantsOfType<Button>().Where(b => b.Classes.Contains("link"));

    /// <summary>Restores a saved card (static text).</summary>
    public static RunCard FromSaved(Programs.SavedRun saved, CalcSession session)
    {
        bool repl = saved.File == "REPL";
        var card = new RunCard(saved.Number, saved.File, session, repl);
        card.IsRunning = false;
        card.Meta = saved.Meta;
        card.IsError = saved.Failed;
        if (saved.Output.Length > 0) card.Append(ScriptOutputKind.Standard, saved.Output);
        card.EndTextBlock();
        if (!repl) card.AddFooter(saved.Failed ? "FAILED, EARLIER SESSION" : "EARLIER SESSION", saved.Failed);
        return card;
    }

    /// <summary>The end of the card's text, for saving.</summary>
    public string SaveText(int max = 1500)
    {
        var text = ConsoleText.TrimEnd();
        return text.Length <= max ? text : "...\n" + text[^max..];
    }
}

/// <summary>Logical tree helpers.</summary>
internal static class LogicalExtensions
{
    public static IEnumerable<T> GetLogicalDescendantsOfType<T>(this Control root) where T : Control
    {
        foreach (var child in Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(root))
            if (child is T t) yield return t;
    }
}

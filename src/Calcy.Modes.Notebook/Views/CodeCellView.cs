using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Calcy.Graphing;
using Calcy.Modes.Notebook.Model;
using Calcy.UI.Controls;
using Calcy.UI.Graph;
using Calcy.UI.Math;
using Calcy.UI.Services;

namespace Calcy.Modes.Notebook.Views;

/// <summary>
/// A code cell: highlighted Python (click to edit), RUN and STOP, and the output under a dashed line: console text,
/// typeset calculator results, input prompts answered inline, drawings and native graphs from calc.graph.
/// </summary>
internal sealed class CodeCellView : CellView
{
    private const int MaxShownChars = 20_000;

    private readonly CodeCell _cell;
    private readonly SelectableTextBlock _display;
    private readonly TextBox _editor;
    private readonly DashedFrame _outputFrame;
    private readonly StackPanel _output = new() { Spacing = 6 };
    private readonly PixelButton _run;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly List<(CodeOutputItem Item, Control View, int Length, bool Waiting)> _rendered = [];
    private readonly Border _liveHost = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _placeholder = Ui.Body("", 13, "Calcy.Muted");
    private readonly StackPanel _previous = new() { Spacing = 4 };
    private readonly SelectableTextBlock _previousText = TextView("", "Calcy.Muted");
    private FramePicture? _live;
    private int _version = -1;
    private bool _editing;

    public CodeCellView(NotebookView owner, CodeCell cell) : base(owner, cell)
    {
        _cell = cell;
        _display = new SelectableTextBlock
        {
            FontSize = 15,
            LineHeight = 22.5,
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(14, 10),
            Cursor = new Cursor(StandardCursorType.Ibeam),
            Inlines = [],
        };
        _display.Res(TextBlock.FontFamilyProperty, "Calcy.Font.Math").Res(TextBlock.ForegroundProperty, "Calcy.Ink").Res(SelectableTextBlock.SelectionBrushProperty, "Calcy.Sel");
        _display.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left && string.IsNullOrEmpty(_display.SelectedText)) StartEditing();
        };
        _editor = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 15,
            MinHeight = 44,
            Padding = new Thickness(11, 7),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Background = Brushes.Transparent,
            VerticalContentAlignment = VerticalAlignment.Top,
            Text = cell.Code,
        };
        _editor.Res(TextBox.FontFamilyProperty, "Calcy.Font.Math").Res(TextBox.BorderBrushProperty, "Calcy.Sel");
        Avalonia.Automation.AutomationProperties.SetName(_editor, "Python code");
        _editor.TextChanged += (_, _) => _cell.Code = _editor.Text ?? "";
        _editor.AddHandler(KeyDownEvent, OnEditorKey, RoutingStrategies.Tunnel);
        _editor.LostFocus += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (_editing && !_editor.IsKeyboardFocusWithin) StopEditing();
        }, DispatcherPriority.Background);

        var earlier = Ui.Body("Output from an earlier session. Run the cell to bring it up to date.", 12, "Calcy.Muted");
        earlier.FontStyle = FontStyle.Italic;
        _previous.Children.Add(earlier);
        _previous.Children.Add(_previousText);
        var area = new StackPanel { Spacing = 6, Children = { _placeholder, _previous, _output, _liveHost } };
        _outputFrame = new DashedFrame { Child = area, Padding = new Thickness(14, 9, 14, 10) };
        _outputFrame.Res(DashedFrame.StrokeProperty, "Calcy.Muted");

        _run = Ui.Button("RUN", "Run the cell (Shift+Enter)", "primary");
        _run.Margin = new Thickness(4, 0, 0, 0);
        _run.Click += (_, _) =>
        {
            if (_cell.IsRunning) Owner.StopCode();
            else _ = Owner.RunCodeAsync(_cell);
        };
        AddHeaderControl(_run);

        Body.Content = new StackPanel { Children = { _display, _editor, _outputFrame } };
        _poll.Tick += (_, _) => RenderOutput();
        cell.InputChanged += OnInput;
        ShowMode();
        Highlight();
        Refresh();
    }

    protected override string KindTitle => "Code cell (Python, sandboxed)";

    protected override string MetaText => _cell.IsRunning ? "" : _cell.RunSummary ?? (_cell.PreviousOutput is not null ? "earlier session" : "");

    /// <summary>The code editor.</summary>
    public TextBox CodeEditor => _editor;

    /// <summary>True while the source editor is shown.</summary>
    public bool IsEditing => _editing;

    /// <summary>The output controls (for tests).</summary>
    public StackPanel OutputPanel => _output;

    /// <summary>The input box of a waiting input() call, if any.</summary>
    public TextBox? PendingInput { get; private set; }

    private void OnInput(object? sender, EventArgs e)
    {
        if (!_editing && _editor.Text != _cell.Code) _editor.Text = _cell.Code;
        if (!_editing) Highlight();
    }

    // ------------------------------------------------------------------ editing

    public void StartEditing()
    {
        if (_editing) return;
        _editing = true;
        _editor.Text = _cell.Code;
        ShowMode();
        Owner.Select(this, focus: false);
        Dispatcher.UIThread.Post(() =>
        {
            _editor.Focus();
            _editor.CaretIndex = (_editor.Text ?? "").Length;
        }, DispatcherPriority.Input);
    }

    public void StopEditing()
    {
        if (!_editing) return;
        _editing = false;
        _cell.Code = _editor.Text ?? "";
        ShowMode();
        Highlight();
    }

    private void ShowMode()
    {
        _editor.IsVisible = _editing;
        _display.IsVisible = !_editing;
    }

    private void Highlight()
    {
        _display.Inlines!.Clear();
        var code = _cell.Code.TrimEnd('\n');
        if (code.Length == 0)
        {
            var hint = new Run("# Python: print(1 + 1), import calc, calc.get(\"a\"), calc.set(\"k\", 3)");
            hint.Res(TextElement.ForegroundProperty, "Calcy.Muted");
            _display.Inlines.Add(hint);
            return;
        }
        foreach (var (text, kind) in PythonHighlighter.Tokenize(code))
        {
            var run = new Run(text);
            switch (kind)
            {
                case PyTokenKind.Keyword or PyTokenKind.Builtin:
                    run.FontWeight = FontWeight.Bold;
                    run.Res(TextElement.ForegroundProperty, "Calcy.G4");
                    break;
                case PyTokenKind.String:
                    run.Res(TextElement.ForegroundProperty, "Calcy.G1");
                    break;
                case PyTokenKind.Comment:
                    run.FontStyle = FontStyle.Italic;
                    run.Res(TextElement.ForegroundProperty, "Calcy.Muted");
                    break;
            }
            _display.Inlines.Add(run);
        }
    }

    private void OnEditorKey(object? sender, KeyEventArgs e)
    {
        var text = _editor.Text ?? "";
        int caret = Math.Clamp(_editor.CaretIndex, 0, text.Length);
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                StopEditing();
                Focus();
                return;
            case Key.Tab when e.KeyModifiers == KeyModifiers.None:
                e.Handled = true;
                Insert(text, caret, "    ");
                return;
            case Key.Tab when e.KeyModifiers == KeyModifiers.Shift:
            {
                e.Handled = true;
                int start = text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
                int spaces = 0;
                while (spaces < 4 && start + spaces < text.Length && text[start + spaces] == ' ') spaces++;
                if (spaces == 0) return;
                _editor.Text = text.Remove(start, spaces);
                _editor.CaretIndex = Math.Max(start, caret - spaces);
                return;
            }
            case Key.Enter when e.KeyModifiers == KeyModifiers.None:
            {
                e.Handled = true;
                int start = text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
                if (caret == 0) start = 0;
                var line = text[start..caret];
                int indent = line.Length - line.TrimStart(' ').Length;
                if (line.TrimEnd().EndsWith(':')) indent += 4;
                Insert(text, caret, "\n" + new string(' ', indent));
                return;
            }
        }
    }

    private void Insert(string text, int caret, string insert)
    {
        _editor.Text = text.Insert(caret, insert);
        _editor.CaretIndex = caret + insert.Length;
    }

    // ------------------------------------------------------------------ output

    /// <summary>Shows new output (polled while the cell runs).</summary>
    public void RenderOutput()
    {
        var items = _cell.Output.Items;
        int version = _cell.Output.Version;
        bool running = _cell.IsRunning;
        if (version != _version || running)
        {
            _version = version;
            bool rebuild = items.Count < _rendered.Count;
            for (int i = 0; i < _rendered.Count && !rebuild; i++) rebuild = !ReferenceEquals(items[i], _rendered[i].Item);
            if (rebuild)
            {
                _output.Children.Clear();
                _rendered.Clear();
                PendingInput = null;
            }
            for (int i = 0; i < _rendered.Count; i++)
            {
                var (item, view, length, waiting) = _rendered[i];
                if (item.Kind is CodeOutputKind.Text or CodeOutputKind.ErrorText)
                {
                    var text = item.Text;
                    if (text.Length != length && view is SelectableTextBlock tb)
                    {
                        tb.Text = Trim(text);
                        _rendered[i] = (item, view, text.Length, false);
                    }
                }
                else if (item.Kind == CodeOutputKind.Input && waiting && !item.IsWaiting)
                {
                    var fresh = InputView(item);
                    _output.Children[_output.Children.IndexOf(view)] = fresh;
                    _rendered[i] = (item, fresh, 0, false);
                }
            }
            for (int i = _rendered.Count; i < items.Count; i++)
            {
                var item = items[i];
                var view = ItemView(item);
                _output.Children.Add(view);
                _rendered.Add((item, view, item.Text.Length, item.Kind == CodeOutputKind.Input && item.IsWaiting));
            }
        }
        // While the code draws, the live canvas is shown; when it ends the drawing becomes an output item.
        bool live = running && !items.Any(i => i.Kind == CodeOutputKind.Drawing) && Owner.Runtime?.Host.Canvas is { HasDrawing: true };
        if (live && _live is null)
        {
            _live = new FramePicture { HorizontalAlignment = HorizontalAlignment.Left };
            _live.Follow(Owner.Runtime!.Host.Canvas);
            _liveHost.Child = _live;
        }
        else if (!live && _live is not null)
        {
            _liveHost.Child = null;
            _live = null;
        }
        UpdatePlaceholder();
    }

    private void UpdatePlaceholder()
    {
        bool hasRun = _cell.LastRun is not null || _cell.IsRunning;
        _previous.IsVisible = !hasRun && _cell.PreviousOutput is not null;
        if (_previous.IsVisible && _previousText.Text != _cell.PreviousOutput)
        {
            _previousText.Text = _cell.PreviousOutput;
            _previousText.Res(TextBlock.ForegroundProperty, _cell.PreviousFailed ? "Calcy.Err" : "Calcy.Muted");
        }
        string? note = null;
        if (_rendered.Count == 0 && _live is null)
        {
            if (_cell.IsRunning) note = "Running...";
            else if (!hasRun && _cell.PreviousOutput is null) note = "Not run yet. Code never runs by itself when a notebook opens: press RUN or Shift+Enter.";
            else if (hasRun) note = "No output.";
        }
        _placeholder.Text = note ?? "";
        _placeholder.IsVisible = note is not null;
        _outputFrame.IsVisible = !Cell.Collapsed;
    }
    private static string Trim(string text) => text.Length <= MaxShownChars ? text.TrimEnd('\n') : "...\n" + text[^MaxShownChars..].TrimEnd('\n');

    private Control ItemView(CodeOutputItem item) => item.Kind switch
    {
        CodeOutputKind.Text => TextView(Trim(item.Text), "Calcy.Ink"),
        CodeOutputKind.ErrorText => TextView(Trim(item.Text), "Calcy.Err"),
        CodeOutputKind.Failure => TextView(item.Text, "Calcy.Err"),
        CodeOutputKind.Math => MathItem(item),
        CodeOutputKind.Input => InputView(item),
        CodeOutputKind.Drawing => DrawingView(item),
        CodeOutputKind.Graph => GraphView(item),
        _ => TextView(item.Text, "Calcy.Ink"),
    };

    private static SelectableTextBlock TextView(string text, string brush)
    {
        var t = new SelectableTextBlock { Text = text, FontSize = 15, TextWrapping = TextWrapping.Wrap };
        t.Res(TextBlock.FontFamilyProperty, "Calcy.Font.Math").Res(TextBlock.ForegroundProperty, brush).Res(SelectableTextBlock.SelectionBrushProperty, "Calcy.Sel");
        return t;
    }

    private Control MathItem(CodeOutputItem item)
    {
        var view = new MathView { Text = item.Text, Session = Owner.Session, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        if (item.Label is null) return view;
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Ui.Mono(item.Label, 15), view } };
    }

    private Control InputView(CodeOutputItem item)
    {
        var row = new DockPanel();
        var prompt = Ui.Mono(item.Prompt ?? "", 15);
        DockPanel.SetDock(prompt, Dock.Left);
        row.Children.Add(prompt);
        if (item.IsWaiting)
        {
            var box = new TextBox { FontSize = 15, MinHeight = 28, Padding = new Thickness(4, 1), BorderThickness = new Thickness(0, 0, 0, 2), VerticalContentAlignment = VerticalAlignment.Center };
            box.Res(TextBox.FontFamilyProperty, "Calcy.Font.Math");
            Avalonia.Automation.AutomationProperties.SetName(box, string.IsNullOrWhiteSpace(item.Prompt) ? "Input for the code" : item.Prompt.Trim());
            box.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                item.Respond(box.Text ?? "");
            };
            row.Children.Add(box);
            PendingInput = box;
            Dispatcher.UIThread.Post(() => box.Focus(), DispatcherPriority.Input);
        }
        else
        {
            row.Children.Add(item.Answer is null ? Ui.Mono("(no answer)", 15, "Calcy.Muted") : Ui.Mono(item.Answer, 15, "Calcy.G1", bold: true));
            if (PendingInput?.Parent == null) PendingInput = null;
        }
        return row;
    }

    private static Control DrawingView(CodeOutputItem item)
    {
        var p = new FramePicture { HorizontalAlignment = HorizontalAlignment.Left };
        if (item.Frame is { } f) p.Show(f);
        var box = Ui.Box(p, "Calcy.Card", 2);
        box.HorizontalAlignment = HorizontalAlignment.Left;
        return box;
    }

    private Control GraphView(CodeOutputItem item)
    {
        var session = item.GraphSession ?? Owner.Session;
        var doc = new GraphDocument(session);
        foreach (var g in item.Graphs)
        {
            var added = doc.Add(g.Text);
            if (g.ColorIndex is int c) added.ColorIndex = ((c % 5) + 5) % 5;
        }
        var view = item.Window ?? GraphFit.FitY(doc.Items, doc.ParameterSource, new Viewport(-10, 10, -10, 10));
        var canvas = new GraphCanvas { Session = session, Parameters = doc.Parameters, Items = [.. doc.Items], Viewport = view, Height = 220, ShowPointsOfInterest = false };
        Avalonia.Automation.AutomationProperties.SetName(canvas, "Graph from code: " + string.Join(", ", item.Graphs.Select(g => g.Text)));
        var open = Ui.Button("OPEN IN GRAPHING", "Show these graphs in Graphing mode");
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Click += (_, _) => Owner.Services.Navigate("graphing", new PlotRequest([.. item.Graphs.Select(g => g.Text)]));
        var frame = Ui.Box(canvas, "Calcy.Card", 2);
        return new StackPanel { Spacing = 6, Children = { frame, open } };
    }

    // ------------------------------------------------------------------ cell

    public override bool FocusInput(bool atEnd = false)
    {
        StartEditing();
        return true;
    }

    public override bool Run()
    {
        StopEditing();
        _ = Owner.RunCodeAsync(_cell);
        return true;
    }

    public override void Refresh()
    {
        base.Refresh();
        bool running = _cell.IsRunning;
        _run.Content = running ? "STOP" : "RUN";
        _run.Classes.Set("primary", !running);
        _run.Classes.Set("err", running);
        ToolTip.SetTip(_run, running ? "Stop the code (Esc)" : "Run the cell (Shift+Enter)");
        if (running) _poll.Start();
        else _poll.Stop();
        _version = -1;
        RenderOutput();
    }

    protected override void OnCollapsedChanged() => _outputFrame.IsVisible = !Cell.Collapsed;

    public override void Detach()
    {
        base.Detach();
        _cell.InputChanged -= OnInput;
        _poll.Stop();
    }
}

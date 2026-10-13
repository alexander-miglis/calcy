using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Calcy.Graphing;
using Calcy.Modes.Code.Views;
using Calcy.UI.Controls;
using Calcy.UI.Graph;
using Calcy.UI.Services;

namespace Calcy.Modes.Code;

/// <summary>What the panel above the console shows.</summary>
public enum SidePanel
{
    /// <summary>The drawing canvas (turtle, ti_plotlib, ti_draw, casioplot, calcydraw).</summary>
    Canvas,

    /// <summary>Graphs from calc.graph.add, as an interactive graph.</summary>
    Graph,

    /// <summary>The program's variables.</summary>
    Variables,
}

public sealed partial class CodeView
{
    private readonly CanvasView _canvasView = new();
    private readonly GraphCanvas _graph = new();
    private readonly StackPanel _variables = new() { Margin = new Thickness(10), Spacing = 2 };
    private readonly ContentControl _sideContent = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly List<PixelToggle> _sideTabs = [];
    private PixelPanel _side = null!;
    private Control _graphView = null!;
    private Control _variablesView = null!;
    private TextBlock _graphEmpty = null!;
    private PixelButton _sendToGraphing = null!;
    private SidePanel _sidePanel = SidePanel.Canvas;
    private GraphDocument? _graphDoc;
    private bool _graphsDirty;

    /// <summary>The canvas view.</summary>
    public CanvasView CanvasView => _canvasView;

    /// <summary>The embedded graph.</summary>
    public GraphCanvas GraphCanvas => _graph;

    /// <summary>The panel shown above the console.</summary>
    public SidePanel SidePanelShown => _sidePanel;

    private Control BuildSidePanel()
    {
        _canvasView.Canvas = _host.Canvas;
        Avalonia.Automation.AutomationProperties.SetName(_canvasView, "Canvas");

        // Graph view: the native interactive graph and SEND TO GRAPHING.
        _graph.Session = _s.Session;
        _graph.ShowPointsOfInterest = false;
        _graph.Viewport = new Viewport(-10, 10, -7, 7);
        var graphHost = new Border { Child = _graph, BorderThickness = new Thickness(0, 0, 0, 3), Height = 260 };
        graphHost.Bind(Border.BackgroundProperty, "Calcy.Card");
        graphHost.Bind(Border.BorderBrushProperty, "Calcy.Line");
        _graphEmpty = Ui.Label("Graphs from calc.graph.add appear here", 12, "Calcy.Muted");
        _graphEmpty.HorizontalAlignment = HorizontalAlignment.Center;
        _graphEmpty.VerticalAlignment = VerticalAlignment.Center;
        _graphEmpty.TextWrapping = TextWrapping.Wrap;
        _graphEmpty.TextAlignment = TextAlignment.Center;
        _sendToGraphing = Ui.SmallButton("SEND TO GRAPHING", "Open these graphs in Graphing mode", "ok");
        _sendToGraphing.Click += (_, _) => SendToGraphing();
        var fit = Ui.SmallButton("FIT", "Fit the graphs");
        fit.Click += (_, _) => _graph.Fit();
        var graphActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Right, Children = { fit, _sendToGraphing } };
        var graphGrid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        graphGrid.Children.Add(graphHost);
        graphGrid.Children.Add(_graphEmpty);
        Grid.SetRow(graphActions, 1);
        graphGrid.Children.Add(graphActions);
        _graphView = graphGrid;

        var varsScroll = new ScrollViewer { Content = _variables, Height = 300, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _variablesView = new Border { Child = varsScroll }.Bind(Border.BackgroundProperty, "Calcy.Card");

        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (panel, text) in new[] { (SidePanel.Canvas, "CANVAS"), (SidePanel.Graph, "GRAPH"), (SidePanel.Variables, "VARS") })
        {
            var p = panel;
            var t = new PixelToggle { Content = text, MinHeight = 26, Padding = new Thickness(8, 0), FontSize = 11, Margin = new Thickness(0, 0, -2, 0) };
            Avalonia.Automation.AutomationProperties.SetName(t, panel == SidePanel.Variables ? "Variables" : panel.ToString());
            t.Click += (_, _) => ShowSide(p);
            _sideTabs.Add(t);
            tabs.Children.Add(t);
        }
        _side = new PixelPanel { Header = "Canvas", HeaderContent = tabs, Content = _sideContent, MaxWidth = 420 };
        Avalonia.Automation.AutomationProperties.SetName(_side, "Canvas");
        return _side;
    }

    /// <summary>Shows the canvas, the graph or the variables above the console.</summary>
    public void ShowSide(SidePanel panel)
    {
        _sidePanel = panel;
        for (int i = 0; i < _sideTabs.Count; i++) _sideTabs[i].IsChecked = i == (int)panel;
        _side.Header = panel switch { SidePanel.Graph => "Graph", SidePanel.Variables => "Variables", _ => "Canvas" };
        _sideContent.Content = panel switch
        {
            SidePanel.Graph => _graphView,
            SidePanel.Variables => _variablesView,
            _ => _canvasView,
        };
        if (panel == SidePanel.Variables) RefreshVariables();
        if (panel == SidePanel.Graph && _graphsDirty) RefreshGraphs();
        if (!_restoring) _saveLater.Trigger();
    }

    private void RefreshGraphs()
    {
        _graphsDirty = false;
        var requests = _host.Graphs.Items;
        var doc = new GraphDocument(_s.Session);
        foreach (var r in requests)
        {
            var item = doc.Add(r.Text);
            if (r.ColorIndex is { } c) item.ColorIndex = c;
        }
        _graphDoc = doc;
        _graph.Parameters = doc.Parameters;
        _graph.Items = [.. doc.Items];
        _graphEmpty.IsVisible = requests.Count == 0;
        _sendToGraphing.IsEnabled = requests.Count > 0;
        if (_host.Graphs.Window is { } w) _graph.Viewport = w;
        else if (requests.Count > 0)
        {
            var size = _graph.Bounds.Size;
            _graph.Viewport = size.Width > 2 && size.Height > 2 ? new Viewport(-10, 10, -7, 7).Square(size.Width, size.Height) : new Viewport(-10, 10, -7, 7);
            Dispatcher.UIThread.Post(() => _graph.Fit(), DispatcherPriority.Background);
        }
        _graph.Refresh();
    }

    /// <summary>The graphs the program asked for, as Graphing mode text.</summary>
    public IReadOnlyList<string> GraphTexts => _graphDoc?.Items.Select(i => i.Text).ToList() ?? [];

    /// <summary>Opens the program's graphs in Graphing mode.</summary>
    public void SendToGraphing()
    {
        var texts = _host.Graphs.Items.Select(g => g.Text).ToList();
        if (texts.Count == 0) return;
        _s.Navigate("graphing", new PlotRequest(texts));
    }

    private void RefreshVariables()
    {
        if (_sidePanel != SidePanel.Variables && _variables.Children.Count > 0 && _running) return;
        _variables.Children.Clear();
        var vars = _host.GetVariables().Where(v => v.TypeName != "module").ToList();
        if (vars.Count == 0)
        {
            var empty = Ui.Label("Run a program to see its variables", 12, "Calcy.Muted");
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.Margin = new Thickness(0, 24);
            _variables.Children.Add(empty);
        }
        foreach (var v in vars)
        {
            var name = Ui.Mono(v.Name, 14, bold: true);
            name.Width = 110;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            var type = Ui.Label(v.TypeName.ToUpperInvariant(), 10, "Calcy.Muted");
            type.Width = 74;
            var repr = Ui.Mono(v.Repr, 13);
            repr.TextTrimming = TextTrimming.CharacterEllipsis;
            ToolTip.SetTip(repr, v.Repr.Length > 400 ? v.Repr[..400] + "..." : v.Repr);
            var row = new DockPanel { Margin = new Thickness(0, 1) };
            DockPanel.SetDock(name, Dock.Left);
            DockPanel.SetDock(type, Dock.Left);
            row.Children.Add(name);
            row.Children.Add(type);
            row.Children.Add(repr);
            _variables.Children.Add(row);
        }
        if (_host.CalcFunctions.Count > 0)
        {
            var note = Ui.Body("Calculator functions from Python: " + string.Join(", ", _host.CalcFunctions), 12, "Calcy.Muted");
            note.Margin = new Thickness(0, 8, 0, 0);
            _variables.Children.Add(note);
        }
    }
}

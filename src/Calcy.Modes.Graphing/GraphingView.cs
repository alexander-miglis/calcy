using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Calcy.Graphing;
using Calcy.UI.Controls;
using Calcy.UI.Graph;
using Calcy.UI.Services;

namespace Calcy.Modes.Graphing;

/// <summary>Which part of the centre panel is shown.</summary>
public enum GraphViewMode
{
    /// <summary>The graph only.</summary>
    Graph,

    /// <summary>The table of values only.</summary>
    Table,

    /// <summary>Graph and table side by side.</summary>
    Split,
}

/// <summary>
/// The Graphing screen (design board Graphing.dc.html): expressions with colour swatches, sliders and inline errors on the
/// left; the graph with its toolbar (views, zoom, presets, window, analysis, export) in the centre; analysis cards on the right.
/// </summary>
public sealed partial class GraphingView : UserControl, IReceivesPayload, IFocusFirst, IBusy
{
    private const string ModeId = "graphing";

    private readonly AppServices _s;
    private readonly GraphDocument _doc;
    private readonly GraphCanvas _canvas;
    private readonly Grid _content = new();
    private readonly Border _graphHost;
    private readonly Control _tableHost;
    private readonly List<PixelToggle> _viewTabs = [];
    private readonly Debouncer _saveLater;
    private GraphViewMode _view = GraphViewMode.Graph;
    private int _selected = -1;
    private bool _restoring;

    /// <summary>Creates the screen on the shared services (session, theme, preferences).</summary>
    public GraphingView(AppServices services)
    {
        _s = services;
        _doc = new GraphDocument(services.Session);
        _saveLater = new Debouncer(TimeSpan.FromMilliseconds(400), SaveState);
        AddStyles();

        _canvas = new GraphCanvas
        {
            Session = services.Session,
            Parameters = _doc.Parameters,
            Viewport = new Viewport(-10, 10, -7.5, 7.5),
        };
        _canvas.PointsOfInterestChanged += (_, _) => RefreshCards();
        _canvas.ViewportChanged += (_, _) => _saveLater.Trigger();
        _canvas.PropertyChanged += (_, e) =>
        {
            if (e.Property == GraphCanvas.SelectedPointProperty) RefreshCards();
            else if (e.Property == BoundsProperty) KeepScaleOnResize(e);
        };
        _canvas.PointPicked += OnPointPicked;
        _graphHost = new Border { Child = _canvas, BorderThickness = new Thickness(2), Margin = new Thickness(8) };
        Bind(_graphHost, Border.BackgroundProperty, "Calcy.Card");
        Bind(_graphHost, Border.BorderBrushProperty, "Calcy.Line");
        _tableHost = BuildTablePanel();

        var left = BuildExpressionPanel();
        var centre = BuildGraphPanel();
        var right = BuildAnalysisPanel();
        var root = new Grid
        {
            Margin = new Thickness(16),
            ColumnDefinitions = new ColumnDefinitions("330,16,*,16,300"),
        };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(centre, 2);
        Grid.SetColumn(right, 4);
        root.Children.Add(left);
        root.Children.Add(centre);
        root.Children.Add(right);
        // Side panels hug their content (as on the design board) but never grow past the screen: they scroll instead.
        root.PropertyChanged += (_, e) =>
        {
            if (e.Property != BoundsProperty) return;
            left.MaxHeight = right.MaxHeight = Math.Max(120, root.Bounds.Height);
        };
        Content = root;

        _doc.Changed += (_, _) => OnDocumentChanged();
        _doc.Parameters.ValuesChanged += (_, p) => OnParameterChanged(p);
        _s.Session.Env.Changed += (_, _) => Dispatcher.UIThread.Post(OnSessionChanged);
        _s.SettingsChanged += (_, _) => OnSessionChanged();

        RestoreState();
        SetView(_view);
        RebuildRows();
        RefreshCards();
    }

    /// <summary>The document behind the screen (expressions and sliders).</summary>
    public GraphDocument Document => _doc;

    /// <summary>The graph control.</summary>
    public GraphCanvas Canvas => _canvas;

    /// <summary>The current view (graph, table or split).</summary>
    public GraphViewMode ViewMode => _view;

    /// <inheritdoc />
    public bool IsBusy => _canvas.IsBusy || _saveLater.Pending;

    /// <inheritdoc />
    public void FocusFirst() => _addBox.Focus();

    /// <inheritdoc />
    public void Receive(object payload)
    {
        switch (payload)
        {
            case PlotRequest p:
                int first = -1;
                foreach (var text in p.Expressions.Where(t => !string.IsNullOrWhiteSpace(t)))
                {
                    int existing = IndexOfText(text);
                    if (existing >= 0)
                    {
                        _doc.Items[existing].IsVisible = true;
                        if (first < 0) first = existing;
                        continue;
                    }
                    _doc.Add(text.Trim());
                    if (first < 0) first = _doc.Items.Count - 1;
                }
                if (first >= 0) Select(first);
                _canvas.Refresh();
                break;
            case InputRequest r:
                _addBox.Text = r.Text;
                _addBox.CaretIndex = r.Text.Length;
                if (r.Evaluate) CommitAddBox();
                break;
        }
    }

    private int IndexOfText(string text)
    {
        static string Norm(string s) => s.Replace(" ", "");
        for (int i = 0; i < _doc.Items.Count; i++)
            if (Norm(_doc.Items[i].Text) == Norm(text)) return i;
        return -1;
    }

    /// <summary>Adds an expression row (as if typed in the "+" row).</summary>
    public GraphItem AddExpression(string text)
    {
        var item = _doc.Add(text);
        Select(_doc.Items.Count - 1);
        return _doc.Items[^1];
    }

    /// <summary>Selects a row: its points of interest are marked and listed in the analysis cards.</summary>
    public void Select(int index)
    {
        _selected = index >= 0 && index < _doc.Items.Count ? index : -1;
        var item = SelectedItem;
        _canvas.FocusItem = item is { IsFunction: true } ? item : null;
        UpdateRowSelection();
        RefreshCards();
    }

    /// <summary>The selected row's item.</summary>
    public GraphItem? SelectedItem => _selected >= 0 && _selected < _doc.Items.Count ? _doc.Items[_selected] : null;

    /// <summary>Switches between the graph, the table and both.</summary>
    public void SetView(GraphViewMode mode)
    {
        _view = mode;
        for (int i = 0; i < _viewTabs.Count; i++) _viewTabs[i].IsChecked = i == (int)mode;
        _content.Children.Clear();
        _content.ColumnDefinitions.Clear();
        switch (mode)
        {
            case GraphViewMode.Graph:
                _content.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                _content.Children.Add(_graphHost);
                Grid.SetColumn(_graphHost, 0);
                break;
            case GraphViewMode.Table:
                _content.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                _content.Children.Add(_tableHost);
                Grid.SetColumn(_tableHost, 0);
                break;
            default:
                _content.ColumnDefinitions.Add(new ColumnDefinition(2, GridUnitType.Star));
                _content.ColumnDefinitions.Add(new ColumnDefinition(1.15, GridUnitType.Star));
                _content.Children.Add(_graphHost);
                _content.Children.Add(_tableHost);
                Grid.SetColumn(_graphHost, 0);
                Grid.SetColumn(_tableHost, 1);
                break;
        }
        if (mode != GraphViewMode.Graph) RefreshTable();
        if (_hint is not null) _hint.IsVisible = mode != GraphViewMode.Table;
        _saveLater.Trigger();
    }

    // ------------------------------------------------------------------ document events

    private void OnDocumentChanged()
    {
        _canvas.Items = [.. _doc.Items];
        if (_selected >= _doc.Items.Count) _selected = _doc.Items.Count - 1;
        if (_rowViews.Count != _doc.Items.Count) RebuildRows();
        else UpdateRows();
        var sel = SelectedItem;
        _canvas.FocusItem = sel is { IsFunction: true } ? sel : null;
        if (_view != GraphViewMode.Graph) RefreshTable();
        RefreshCards();
        _saveLater.Trigger();
    }

    private bool _sessionUpdate;

    private void OnSessionChanged()
    {
        if (_sessionUpdate) return;
        _sessionUpdate = true;
        try
        {
            _doc.Reparse();
        }
        finally
        {
            _sessionUpdate = false;
        }
    }

    private void OnParameterChanged(GraphParameter p)
    {
        UpdateSliders(p);
        if (_view != GraphViewMode.Graph) RefreshTableLater();
        RefreshResultsLater();
        _saveLater.Trigger();
    }

    private Size _lastCanvasSize;

    /// <summary>Keeps the graph's scale (units per pixel) when the canvas is resized, so shapes keep their proportions.</summary>
    private void KeepScaleOnResize(AvaloniaPropertyChangedEventArgs e)
    {
        var size = _canvas.Bounds.Size;
        if (size.Width < 2 || size.Height < 2) return;
        var old = _lastCanvasSize;
        _lastCanvasSize = size;
        var v = _canvas.Viewport;
        if (old.Width < 2 || old.Height < 2)
        {
            // First layout: make the scale equal on both axes unless the window came from saved state with its own shape.
            if (!_restoredWindow) _canvas.SetViewport(v.Square(size.Width, size.Height), remember: false);
            return;
        }
        if (old == size) return;
        double w = v.Width * size.Width / old.Width, h = v.Height * size.Height / old.Height;
        _canvas.SetViewport(new Viewport(v.CenterX - w / 2, v.CenterX + w / 2, v.CenterY - h / 2, v.CenterY + h / 2), remember: false);
    }

    // ------------------------------------------------------------------ persistence

    private bool _restoredWindow;

    private sealed class SavedView
    {
        public GraphState? Graph { get; set; }
        public int Selected { get; set; } = -1;
        public bool Exact { get; set; }
    }

    private void RestoreState()
    {
        var saved = _s.Prefs.GetModeState<SavedView>(ModeId);
        if (saved?.Graph is not { } g) return;
        _restoring = true;
        try
        {
            _doc.Load(g);
            _canvas.Items = [.. _doc.Items];
            _canvas.Viewport = g.Viewport;
            _restoredWindow = true;
            _canvas.TickStyle = g.PiTicks ? TickStyle.Pi : TickStyle.Decimal;
            _view = Enum.TryParse<GraphViewMode>(g.View, out var v) ? v : GraphViewMode.Graph;
            _tableStart = g.TableStart;
            _tableStep = g.TableStep > 0 ? g.TableStep : 0.5;
            _tableExact = saved.Exact;
            _selected = saved.Selected < _doc.Items.Count ? saved.Selected : -1;
        }
        finally
        {
            _restoring = false;
        }
    }

    private void SaveState()
    {
        if (_restoring) return;
        var g = _doc.Save(_canvas.Viewport, _canvas.TickStyle == TickStyle.Pi);
        g.View = _view.ToString();
        g.TableStart = _tableStart;
        g.TableStep = _tableStep;
        _s.Prefs.SetModeState(ModeId, new SavedView { Graph = g, Selected = _selected, Exact = _tableExact });
        _s.Prefs.Save();
    }

    // ------------------------------------------------------------------ helpers

    private static void Bind(AvaloniaObject target, AvaloniaProperty property, string resource)
    {
        if (target is StyledElement el) el.Bind(property, el.GetResourceObservable(resource));
    }

    private static TextBlock Label(string text, double size = 11, string brush = "Calcy.Ink")
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
        Bind(t, TextBlock.FontFamilyProperty, "Calcy.Font.Label");
        Bind(t, TextBlock.ForegroundProperty, brush);
        return t;
    }

    private static TextBlock MathText(string text, double size = 16, bool bold = false, string brush = "Calcy.Ink")
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeight.Bold : FontWeight.Normal, TextWrapping = TextWrapping.Wrap };
        Bind(t, TextBlock.FontFamilyProperty, "Calcy.Font.Math");
        Bind(t, TextBlock.ForegroundProperty, brush);
        return t;
    }

    private static PixelButton ToolButton(string text, string tip, double minWidth = 38)
    {
        var b = new PixelButton
        {
            Content = text,
            Classes = { "small", "paper", "flat" },
            MinWidth = minWidth,
            MinHeight = 38,
            Padding = new Thickness(7, 0),
            BorderThickness = new Thickness(2),
            FontSize = 11,
        };
        ToolTip.SetTip(b, tip);
        Avalonia.Automation.AutomationProperties.SetName(b, tip);
        return b;
    }

    private static string InkToken(int colorIndex) => "Calcy.G" + (((colorIndex % 5) + 5) % 5 + 1);

    private void AddStyles()
    {
        var part = (Func<Selector?, Selector>)(s => s!.Template().OfType<Border>().Name("PART_BorderElement"));
        Styles.Add(new Style(x => part(x.OfType<TextBox>().Class("expr")))
        {
            Setters =
            {
                new Setter(Border.BackgroundProperty, Brushes.Transparent),
                new Setter(Border.BorderBrushProperty, Brushes.Transparent),
                new Setter(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 2)),
            },
        });
        Styles.Add(new Style(x => part(x.OfType<TextBox>().Class("expr").Class(":pointerover")))
        {
            Setters =
            {
                new Setter(Border.BackgroundProperty, Brushes.Transparent),
                new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Calcy.Muted")),
                new Setter(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 2)),
            },
        });
        Styles.Add(new Style(x => part(x.OfType<TextBox>().Class("expr").Class(":focus")))
        {
            Setters =
            {
                new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Calcy.Card")),
                new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Calcy.Ink")),
                new Setter(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 2)),
            },
        });
        Styles.Add(new Style(x => x.OfType<TextBox>().Class("expr"))
        {
            Setters =
            {
                new Setter(TextBox.MinHeightProperty, 32.0),
                new Setter(TextBox.PaddingProperty, new Thickness(4, 3)),
                new Setter(TextBox.BorderThicknessProperty, new Thickness(0, 0, 0, 2)),
            },
        });
    }

    /// <summary>Runs an action on the UI thread once calls to <see cref="Trigger"/> stop for a moment (works headless too).</summary>
    private sealed class Debouncer(TimeSpan delay, Action action)
    {
        private int _generation;

        public bool Pending { get; private set; }

        public void Trigger()
        {
            int g = ++_generation;
            Pending = true;
            Task.Delay(delay).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
            {
                if (g != _generation) return;
                Pending = false;
                action();
            }), TaskScheduler.Default);
        }
    }
}

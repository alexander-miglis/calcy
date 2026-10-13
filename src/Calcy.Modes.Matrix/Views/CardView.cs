using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Calcy.Modes.Matrix.Logic;
using Calcy.UI.Controls;

namespace Calcy.Modes.Matrix.Views;

/// <summary>What a card's buttons ask the screen to do.</summary>
public interface ICardActions
{
    /// <summary>Copies text to the clipboard (and reports it in the status bar).</summary>
    void Copy(string text, string what);

    /// <summary>Sends text to Advanced mode.</summary>
    void Use(string text);

    /// <summary>Saves a value as a variable; returns an error message or null when it worked.</summary>
    string? Store(string name, StorePart part);

    /// <summary>A free name to suggest for STORE AS.</summary>
    string SuggestName(StorePart part);

    /// <summary>Puts an expression back into the expression line.</summary>
    void Edit(string text);

    /// <summary>The card changed (steps opened, decimals shown), so the saved state should be updated.</summary>
    void Changed();

    /// <summary>True when the settings ask for decimals first (design decision D6); exact values are the default.</summary>
    bool PreferDecimals { get; }
}

/// <summary>
/// Shows one <see cref="MatrixCard"/> as a Calcy result card: the input, the result (exact first, S⇔D switches to
/// decimals), optional elimination steps and the COPY, COPY LATEX, STORE AS and USE buttons.
/// </summary>
public sealed class CardView : Card
{
    private readonly MatrixCard _model;
    private readonly ICardActions _actions;
    private readonly StackPanel _body = new() { Spacing = 8 };
    private readonly StackPanel _steps = new() { Spacing = 6, Margin = new Thickness(0, 4, 0, 0) };
    private readonly Border _stepsBox;
    private readonly StackPanel _storeRow = new() { Spacing = 6, IsVisible = false };
    private bool _decimal;

    /// <summary>Builds the card.</summary>
    public CardView(MatrixCard model, ICardActions actions)
    {
        _model = model;
        _actions = actions;
        Title = $"#{model.Number}  {model.Title}";
        Meta = model.Time;
        IsError = model.IsError;
        Padding = new Thickness(12, 10);
        AutomationProperties.SetName(this, $"Card {model.Number}: {model.Title.ToLowerInvariant()}");

        _stepsBox = new Border { Child = _steps, BorderThickness = new Thickness(0, 2, 0, 0), Padding = new Thickness(0, 8, 0, 0), IsVisible = false };
        _stepsBox.Bind(Border.BorderBrushProperty, _stepsBox.GetResourceObservable("Calcy.Key"));

        var input = new TextBlock { Text = model.Input, FontSize = 15, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Classes = { "math", "muted" } };
        // View toggles (decimals, steps) sit beside the input; the actions that reuse the result sit underneath.
        var toggles = Toggles();
        var top = new DockPanel();
        DockPanel.SetDock(toggles, Dock.Right);
        top.Children.Add(toggles);
        top.Children.Add(input);
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(top);
        content.Children.Add(_body);
        content.Children.Add(_stepsBox);
        content.Children.Add(_storeRow);
        var buttons = Buttons();
        if (buttons.Children.Count > 0) content.Children.Add(buttons);
        Content = content;
        _decimal = (actions.PreferDecimals || model.PreferDecimals) && model.HasApprox;
        Render();
        if (model.Steps.Count > 0 && model.StepsOpen) ToggleSteps(true);
    }

    /// <summary>Uses the Calcy card look.</summary>
    protected override Type StyleKeyOverride => typeof(Card);

    /// <summary>The card's data.</summary>
    public MatrixCard Model => _model;

    /// <summary>True while decimals are shown.</summary>
    public bool ShowsDecimals => _decimal;

    /// <summary>True while the steps are shown.</summary>
    public bool StepsVisible => _stepsBox.IsVisible;

    /// <summary>Shows or hides the elimination steps.</summary>
    public void ToggleSteps(bool? open = null)
    {
        bool show = open ?? !_stepsBox.IsVisible;
        if (show && _steps.Children.Count == 0) BuildSteps();
        _stepsBox.IsVisible = show;
        _model.StepsOpen = show;
        if (_stepsButton is not null) _stepsButton.Content = show ? "HIDE STEPS" : $"STEPS {_model.Steps.Count(s => s.Kind == CardBlockKind.Step)}";
    }

    /// <summary>Switches between exact values and decimals.</summary>
    public void ToggleDecimals()
    {
        _decimal = !_decimal;
        Render();
        if (_stepsBox.IsVisible)
        {
            _steps.Children.Clear();
            BuildSteps();
        }
    }

    private PixelButton? _stepsButton;

    private static PixelButton SmallButton(Panel row, string text, string tip, string color, Action click)
    {
        var b = new PixelButton { Content = text, Classes = { "small" } };
        if (color.Length > 0) b.Classes.Add(color);
        ToolTip.SetTip(b, tip);
        AutomationProperties.SetName(b, tip);
        b.Click += (_, _) => click();
        row.Children.Add(b);
        return b;
    }

    private StackPanel Toggles()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        if (_model.HasApprox) SmallButton(row, "S⇔D", "Switch between exact values and decimals", "mem", () => { ToggleDecimals(); });
        if (_model.Steps.Count > 0)
            _stepsButton = SmallButton(row, $"STEPS {_model.Steps.Count(s => s.Kind == CardBlockKind.Step)}", "Show or hide the elimination steps", "sel", () => { ToggleSteps(); _actions.Changed(); });
        return row;
    }

    private WrapPanel Buttons()
    {
        var row = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        PixelButton Add(string text, string tip, string color, Action click) => SmallButton(row, text, tip, color, click);
        if (_model.CopyText is { } copy) Add("COPY", "Copy as Calcy input", "", () => _actions.Copy(copy, "Calcy input"));
        if (_model.Latex is { } latex) Add("COPY LATEX", "Copy as LaTeX", "", () => _actions.Copy(latex, "LaTeX"));
        if (_model.StoreParts.Count > 0) Add("STORE AS", "Save the result under a name", "", OpenStore);
        if (_model.UseText is { } use) Add("USE", "Use the result in Advanced mode", "head", () => _actions.Use(use));
        if (_model.IsError && _model.Source is { } source) Add("EDIT", "Put the expression back in the line", "", () => _actions.Edit(source));
        return row;
    }

    private void Render()
    {
        _body.Children.Clear();
        foreach (var block in _model.Blocks) _body.Children.Add(BlockControl(block, large: true));
    }

    private void BuildSteps()
    {
        foreach (var block in _model.Steps) _steps.Children.Add(BlockControl(block, large: false));
    }

    private Control BlockControl(CardBlock b, bool large)
    {
        switch (b.Kind)
        {
            case CardBlockKind.Matrix:
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                if (b.Label is { } label) row.Children.Add(MathText(label, large ? 17 : 14, bold: false));
                row.Children.Add(Grid(b, large ? 17 : 14));
                if (b.Text is { } after) row.Children.Add(MathText(after, large ? 17 : 14, bold: false));
                return Scrollable(row);
            }
            case CardBlockKind.Step:
            {
                var panel = new StackPanel { Spacing = 3 };
                panel.Children.Add(MathText(b.Label ?? "", 15, bold: true));
                var grid = Grid(b, 14);
                grid.Margin = new Thickness(14, 0, 0, 0);
                panel.Children.Add(Scrollable(grid));
                return panel;
            }
            case CardBlockKind.Note:
            {
                var t = new TextBlock { Text = b.Text, FontSize = 13, Classes = { "body", "muted" } };
                return t;
            }
            case CardBlockKind.Heading:
            {
                var t = new TextBlock { Text = b.Text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Classes = { "label" }, Margin = new Thickness(0, 4, 0, 0) };
                return t;
            }
            case CardBlockKind.Formula:
                return MathText(_decimal && b.ApproxText is not null ? b.ApproxText : b.Text ?? "", large ? 15 : 14, bold: false);
            default:
            {
                var text = _decimal && b.ApproxText is not null ? b.ApproxText : b.Text ?? "";
                var t = MathText(text, large ? 20 : 15, bold: true);
                if (b.Emphasis) t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable("Calcy.Err"));
                return t;
            }
        }
    }

    private MatrixView Grid(CardBlock b, double size)
    {
        var cells = _decimal && b.ApproxCells is not null ? b.ApproxCells : b.Cells;
        return new MatrixView
        {
            Cells = cells,
            CellFontSize = size,
            AugmentAt = b.AugmentAt,
            BracketThickness = size >= 16 ? 3 : 2,
            ColumnGap = size >= 16 ? 14 : 10,
            RowGap = size >= 16 ? 4 : 2,
        };
    }

    private static TextBlock MathText(string text, double size, bool bold)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Classes = { "math" },
        };
        return t;
    }

    private static Control Scrollable(Control c) => new ScrollViewer
    {
        Content = c,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };

    private void OpenStore()
    {
        if (_storeRow.IsVisible)
        {
            _storeRow.IsVisible = false;
            return;
        }
        _storeRow.Children.Clear();
        var parts = _model.StoreParts;
        ComboBox? which = null;
        if (parts.Count > 1)
        {
            which = new ComboBox { ItemsSource = parts.Select(p => p.Label).ToList(), SelectedIndex = 0, MinWidth = 150, FontSize = 12 };
            AutomationProperties.SetName(which, "Part to store");
            _storeRow.Children.Add(which);
        }
        var name = new TextBox { Text = _actions.SuggestName(parts[0]), Width = 90, FontSize = 16, MinHeight = 34, Height = 34, Padding = new Thickness(6, 0) };
        AutomationProperties.SetName(name, "Name to store as");
        var ok = new PixelButton { Content = "STORE", Classes = { "small", "ok" } };
        var cancel = new PixelButton { Content = "CANCEL", Classes = { "small", "paper" } };
        var message = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Classes = { "body" }, IsVisible = false };
        message.Bind(TextBlock.ForegroundProperty, message.GetResourceObservable("Calcy.Err"));
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new TextBlock { Text = "AS", Classes = { "label" }, VerticalAlignment = VerticalAlignment.Center }, name, ok, cancel } };
        _storeRow.Children.Add(line);
        _storeRow.Children.Add(message);
        if (which is not null) which.SelectionChanged += (_, _) => name.Text = _actions.SuggestName(parts[Math.Max(0, which.SelectedIndex)]);
        void Commit()
        {
            var part = parts[Math.Max(0, which?.SelectedIndex ?? 0)];
            var error = _actions.Store((name.Text ?? "").Trim(), part);
            if (error is null)
            {
                _storeRow.IsVisible = false;
                return;
            }
            message.Text = error;
            message.IsVisible = true;
        }
        ok.Click += (_, _) => Commit();
        cancel.Click += (_, _) => _storeRow.IsVisible = false;
        name.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) { e.Handled = true; Commit(); }
            else if (e.Key == Avalonia.Input.Key.Escape) { e.Handled = true; _storeRow.IsVisible = false; }
        };
        _storeRow.IsVisible = true;
        name.Focus();
        name.SelectAll();
    }

    /// <summary>Opens the STORE AS row and stores under <paramref name="name"/> (used by tests and keyboard users).</summary>
    public string? StoreAs(string name, int part = 0) => _actions.Store(name, _model.StoreParts[part]);
}

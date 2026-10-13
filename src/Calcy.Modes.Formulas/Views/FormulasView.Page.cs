using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Calcy.Formulas;
using Calcy.Modes.Formulas.Logic;
using Calcy.UI.Controls;
using Calcy.UI.Math;

namespace Calcy.Modes.Formulas.Views;

/// <summary>
/// The middle column: the open formula with its name, typeset equation, explanation, when to use it, what it
/// assumes, the variables table and answer, the constants it uses, the worked example and related formulas.
/// </summary>
public sealed partial class FormulasView
{
    private StackPanel _pageContent = null!;
    private ScrollViewer _pageScroll = null!;
    private MathView _equation = null!;

    /// <summary>The typeset equation (for tests).</summary>
    internal MathView EquationView => _equation;

    private PixelPanel BuildPage()
    {
        _pageContent = new StackPanel { Spacing = 18, Margin = new Thickness(18, 16, 18, 22), Name = "FormulaPage" };
        _pageScroll = new ScrollViewer
        {
            Content = _pageContent,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var panel = new PixelPanel { Header = "Formula", Margin = new Thickness(0, 0, 6, 6), Content = _pageScroll };
        AutomationProperties.SetName(panel, "Formula");
        panel.SizeChanged += (_, _) => FitPageHeader();
        return panel;
    }

    /// <summary>The category name in the header, or its short form when the full name would not fit next to the level tag.</summary>
    private void FitPageHeader()
    {
        if (_formula is null) return;
        var full = (_library.GetCategory(_formula.Category)?.Name ?? "Formula").ToUpperInvariant();
        double width = _page.Bounds.Width;
        double tag = PixelWidth(FormulaBrowser.LevelTag(_formula.Level), 1.5) + 12;
        // Panel border (2 x 4) and header padding (2 x 12), the tag, and a gap.
        double room = width - 32 - tag - 12;
        _page.Header = width <= 0 || PixelWidth(full, 2) <= room ? full : FormulaBrowser.ShortCategory(_formula.Category);
    }

    /// <summary>The width of a line of pixel-font text (5 pixels a glyph, 1 between glyphs).</summary>
    private static double PixelWidth(string text, double pixelSize) =>
        text.Length == 0 ? 0 : text.Length * (PixelFont.Width + 1) * pixelSize - pixelSize;

    /// <summary>
    /// Opens a formula: shows its page with the box texts it had last time, marks it in the list and solves live.
    /// </summary>
    internal void OpenFormula(Formula formula, bool fromList = false, bool focusInput = false, bool save = true)
    {
        ArgumentNullException.ThrowIfNull(formula);
        _formula = formula;
        _state.FormulaId = formula.Id;
        _saved = _state.InputsFor(formula.Id);
        _unknown = formula.IsIdentity ? "" : _saved.SolveFor ?? FormulaCalculator.DefaultUnknown(formula);
        _lastInput = null;
        _lastOutcome = null;
        BuildFormulaPage(formula);
        if (!fromList) SyncListSelection();
        _pageScroll.Offset = default;
        Recalculate();
        if (save) SaveNow();
        if (focusInput) Dispatcher.UIThread.Post(FocusFirstInput, DispatcherPriority.Loaded);
    }

    /// <summary>Puts the cursor in the first empty value box (or the first box).</summary>
    internal void FocusFirstInput()
    {
        var rows = _rows.Where(r => r.Input.IsVisible).ToList();
        var box = (rows.FirstOrDefault(r => string.IsNullOrWhiteSpace(r.Input.Text)) ?? rows.FirstOrDefault())?.Input;
        if (box is null) return;
        box.Focus(NavigationMethod.Tab);
        box.SelectAll();
    }

    private void BuildFormulaPage(Formula f)
    {
        _pageContent.Children.Clear();
        var category = _library.GetCategory(f.Category);
        FitPageHeader();
        _page.HeaderContent = Ui.Tag(FormulaBrowser.LevelTag(f.Level), f.Level == FormulaLevel.University ? "uni" : "school",
            FormulaBrowser.LevelName(f.Level));

        // Name and topic.
        var title = Ui.Text(f.Name, "title");
        title.Name = "FormulaName";
        var topic = Ui.Text(string.IsNullOrEmpty(f.Subcategory) ? category?.Name ?? "" : f.Subcategory, "hint");
        _pageContent.Children.Add(new StackPanel { Spacing = 2, Children = { title, topic } });

        // The equation, typeset like a textbook (E = mc², v = u + at) in the author's order.
        _equation = new MathView
        {
            Expression = FormulaTexts.Equation(_services.Session, f, _library),
            Text = f.Equation,
            FontSize = 30,
            KeepTermOrder = true,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Name = "Equation",
        };
        AutomationProperties.SetName(_equation, f.Equation);
        var equationStack = new StackPanel { Spacing = 10, Children = { _equation } };
        if (f.IsIdentity)
        {
            var holds = Ui.Tag("Holds for every value", "identity", "An identity: both sides are equal whatever the values");
            holds.HorizontalAlignment = HorizontalAlignment.Center;
            equationStack.Children.Add(holds);
        }
        var equationBox = new Border { Child = equationStack };
        equationBox.Classes.Add("equation");
        _pageContent.Children.Add(equationBox);

        // What it says, when to use it, what it assumes.
        var about = new StackPanel { Spacing = 12 };
        if (f.Explanation.Length > 0) about.Children.Add(Ui.Text(f.Explanation, "body"));
        if (f.WhenToUse.Length > 0) about.Children.Add(Labelled("When to use", Ui.Text(f.WhenToUse, "body")));
        var assumes = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
        foreach (var a in f.Assumptions)
        {
            var chip = new Border { Child = Ui.Text(a, "chiptext") };
            chip.Classes.Add("chip");
            assumes.Children.Add(chip);
        }
        if (f.Assumptions.Count == 0) assumes.Children.Add(Ui.Text(f.IsIdentity ? "Nothing: it is always true." : "Nothing special.", "hint"));
        about.Children.Add(Labelled("Assumes", assumes));
        _pageContent.Children.Add(about);

        // The variables table and the answer.
        _pageContent.Children.Add(BuildTable(f));
        _pageContent.Children.Add(BuildAnswerBox(f));

        // Constants filled in for you.
        var constants = _library.ConstantsFor(f);
        if (constants.Count > 0) _pageContent.Children.Add(BuildConstants(constants));

        if (f.Example is not null) _pageContent.Children.Add(BuildExample(f));

        var related = _library.RelatedTo(f);
        if (related.Count > 0) _pageContent.Children.Add(BuildRelated(related));

        if (!string.IsNullOrWhiteSpace(f.Source)) _pageContent.Children.Add(Ui.Text("Convention: " + f.Source, "hint"));
    }

    private static StackPanel Labelled(string caption, Control content) =>
        new() { Spacing = 5, Children = { Ui.Caption(caption), content } };

    private Control BuildConstants(IReadOnlyList<FormulaConstant> constants)
    {
        var list = new StackPanel { Name = "FormulaConstants" };
        foreach (var c in constants)
        {
            var symbol = Ui.Symbol(c.Symbol, 16);
            symbol.MinWidth = 44;
            symbol.Margin = new Thickness(0, 0, 10, 0);
            symbol.VerticalAlignment = VerticalAlignment.Top;
            var name = Ui.Text(c.Name, "rowname");
            var value = Ui.Text(ConstantText.Value(c), "mathtext");
            var line = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 4, Children = { value } };
            if (ConstantText.Tag(c) is { } tagText)
                line.Children.Add(Ui.Tag(tagText, tagText == "EXACT" ? "exact" : "typical", tagText == "EXACT" ? "Exact by definition" : "A typical value"));
            else line.Children.Add(Ui.Text(ConstantText.Precision(c), "meaning"));
            DockPanel.SetDock(symbol, Dock.Left);
            var row = new Border
            {
                Padding = new Thickness(12, 8),
                Child = new DockPanel { Children = { symbol, new StackPanel { Spacing = 3, Children = { name, line } } } },
                BorderThickness = new Thickness(0, 0, 0, 2),
            }.Token(Border.BorderBrushProperty, "Calcy.Key");
            AutomationProperties.SetName(row, ConstantText.Describe(c));
            ToolTip.SetTip(row, c.Description);
            list.Children.Add(row);
        }
        var strip = new Border { Child = Ui.Caption("Constants, filled in for you") };
        strip.Classes.Add("strip");
        var box = new Border { Child = new StackPanel { Children = { strip, list } } };
        box.Classes.Add("box");
        return box;
    }

    private Control BuildExample(Formula f)
    {
        var ex = f.Example!;
        var tryButton = Ui.Button("Try example", "Try the worked example", "Fill in the example's values, solve and keep the answer as a card", "primary");
        tryButton.Name = "TryExample";
        tryButton.Click += (_, _) => TryExample();
        tryButton.Margin = new Thickness(10, 0, 0, 0);
        DockPanel.SetDock(tryButton, Dock.Right);
        var caption = Ui.Caption("Worked example");
        caption.VerticalAlignment = VerticalAlignment.Center;
        var strip = new Border { Child = new DockPanel { Children = { tryButton, caption } } };
        strip.Classes.Add("strip");

        var body = new StackPanel { Spacing = 12, Margin = new Thickness(14, 12, 14, 14) };
        body.Children.Add(Ui.Text(ex.Story, "body"));

        var given = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
        foreach (var g in FormulaTexts.ExampleGiven(f))
        {
            var chip = new Border { Child = Ui.Text(g, "chipmath") };
            chip.Classes.Add("chip");
            chip.Classes.Add("given");
            given.Children.Add(chip);
        }
        if (!f.IsIdentity && ex.SolveFor is { } s)
        {
            var find = new Border { Child = Ui.Text("find " + SymbolText.Plain(s), "chipmath") };
            find.Classes.Add("chip");
            find.Classes.Add("find");
            given.Children.Add(find);
        }
        if (given.Children.Count > 0) body.Children.Add(given);

        if (ex.Steps.Count > 0)
        {
            var steps = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowSpacing = 6 };
            for (int i = 0; i < ex.Steps.Count; i++)
            {
                steps.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var n = Ui.Text((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), "stepnumber");
                n.Margin = new Thickness(0, 1, 12, 0);
                var t = Ui.Text(ex.Steps[i], "step");
                Grid.SetRow(n, i);
                Grid.SetRow(t, i);
                Grid.SetColumn(t, 1);
                steps.Children.Add(n);
                steps.Children.Add(t);
            }
            body.Children.Add(steps);
        }
        if (FormulaTexts.ExampleAnswer(f) is { } answer)
        {
            var line = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, Children = { Ui.Caption("Answer"), Ui.Text(answer, "cardresult") } };
            ((TextBlock)line.Children[0]).VerticalAlignment = VerticalAlignment.Center;
            body.Children.Add(line);
        }

        var box = new Border { Child = new StackPanel { Children = { strip, body } }, Name = "Example" };
        box.Classes.Add("example");
        return box;
    }

    private Control BuildRelated(IReadOnlyList<Formula> related)
    {
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8, Name = "Related" };
        foreach (var r in related)
        {
            var b = new PixelButton
            {
                Content = Ui.Text(r.Name, "chiptext"),
                Padding = new Thickness(10, 4),
                MinHeight = 32,
                ClipToBounds = false,
                Tag = r,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            b.Classes.Add("small");
            b.Classes.Add("card");
            AutomationProperties.SetName(b, "Open " + r.Name);
            ToolTip.SetTip(b, r.Equation);
            var target = r;
            b.Click += (_, _) => OpenFormula(target);
            wrap.Children.Add(b);
        }
        return Labelled("Related formulas", wrap);
    }

    /// <summary>Fills in the worked example's values, solves (or checks) and keeps the answer as a card.</summary>
    internal void TryExample()
    {
        var f = _formula;
        if (f.Example is not { } ex) return;
        var texts = FormulaTexts.ExampleInputs(f);
        if (!f.IsIdentity && ex.SolveFor is { } s && f.GetVariable(s) is not null)
        {
            _unknown = s;
            _saved.SolveFor = s;
        }
        SetInputs(texts);
        SyncRows();
        Recalculate(keep: true);
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Calcy.Modes.Formulas.Logic;
using Calcy.Modes.Formulas.Views;
using Calcy.UI.Controls;
using Calcy.UI.Math;
using Calcy.UI.Services;
using Calcy.UI.TestKit;

namespace Calcy.Modes.Formulas.Tests;

/// <summary>The Formulas screen rendered headless: typing, solving, cards, search, constants, persistence, layout and screenshots.</summary>
public class FormulasViewTests
{
    // The app shows a mode below the top bar and above the status line.
    private const double Width = 1360;
    private const double Height = 790;

    public static TheoryData<string> Schemes => new() { "nes", "pico", "gameboy", "amber", "paper" };

    private static (FormulasView View, Window Window, AppServices Services) Open(string scheme = "nes", double width = Width, double height = Height,
        AppServices? services = null)
    {
        services ??= Headless.CreateServices(scheme);
        var view = (FormulasView)new FormulasMode().CreateView(services);
        var window = Headless.Show(view, width, height);
        Idle(view);
        return (view, window, services);
    }

    private static void Idle(FormulasView view)
    {
        Assert.True(Headless.WaitForIdle(view, TimeSpan.FromSeconds(30)), "the view kept working");
        Headless.Pump();
    }

    private static T Named<T>(Control root, string name) where T : Control =>
        root.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? root.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    private static void Type(FormulasView view, TextBox box, string text)
    {
        box.Focus();
        box.Text = text;
        box.CaretIndex = text.Length;
        Idle(view);
    }

    private static void Click(FormulasView view, Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Idle(view);
    }

    private static void Press(FormulasView view, Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        Idle(view);
    }

    private static string Answer(Control view) => Named<TextBlock>(view, "Answer").Text ?? "";

    private static void Show(FormulasView view, string id)
    {
        view.OpenFormula(TestSupport.Formula(id));
        Idle(view);
    }

    /// <summary>The mechanics screenshot state: v = u + a t with v, u and t filled in, solved for a and kept as a card.</summary>
    private static void SolveSuvat(FormulasView view)
    {
        Show(view, "suvat-v-u-a-t");
        Type(view, view.InputFor("v"), "25");
        Type(view, view.InputFor("u"), "5");
        Type(view, view.InputFor("t"), "8");
        Assert.Equal("a = 2.5 m/s²", Answer(view));
        Click(view, Named<PixelButton>(view, "Solve"));
    }

    private static void SeedCards(FormulasView view)
    {
        Show(view, "photon-energy-frequency");
        view.TryExample();
        Idle(view);
        Show(view, "pythagoras-theorem");
        view.TryExample();
        Idle(view);
    }

    // ------------------------------------------------------------------ behaviour

    [Fact]
    public void ModeDescribesItself()
    {
        var mode = new FormulasMode();
        Assert.Equal(("formulas", "Formulas", "Documented formula collection", "Ctrl+0", "G3", "formula"),
            (mode.Id, mode.Title, mode.Description, mode.Shortcut, mode.AccentToken, mode.Icon));
    }

    [Fact]
    public void SolvesLiveWhileTypingAndKeepsCardsOnEnter() => Headless.Run(() =>
    {
        var (view, w, services) = Open();
        Assert.Equal("suvat-v-u-a-t", view.CurrentFormula.Id);
        Assert.Equal("a", view.Unknown);
        Assert.Equal(OutcomeKind.NeedsInput, view.LastOutcome!.Kind);
        Assert.Equal("Fill in v, u and t to find a", Named<TextBlock>(view, "AnswerMessage").Text);

        Type(view, view.InputFor("v"), "25");
        Type(view, view.InputFor("u"), "5");
        Assert.Equal("Fill in t (time) to find a", Named<TextBlock>(view, "AnswerMessage").Text);
        Type(view, view.InputFor("t"), "8");
        Assert.Equal("a = 2.5 m/s²", Answer(view));
        Assert.False(view.InputFor("a").IsVisible);
        Assert.Empty(view.State.Cards);

        // Enter keeps the answer as a card.
        view.InputFor("t").Focus();
        Press(view, w, Key.Enter);
        var card = Assert.Single(view.State.Cards);
        Assert.Equal(["a = 2.5 m/s²"], card.Results);
        Assert.Equal("v = 25 m/s, u = 5 m/s, t = 8 s", card.Given);
        Assert.Equal("#1  MECHANICS", view.CardControls.Single().Title);

        // A bad value marks its box and says why.
        Type(view, view.InputFor("t"), "-1");
        Assert.Contains("error", view.InputFor("t").Classes);
        Assert.Equal("t: t must be at least 0", Named<TextBlock>(view, "AnswerMessage").Text);
        Type(view, view.InputFor("t"), "4*2");
        Assert.DoesNotContain("error", view.InputFor("t").Classes);
        Assert.Equal("a = 2.5 m/s²", Answer(view));
        w.Close();
    });

    [Fact]
    public void ClickingAVariableSolvesForIt() => Headless.Run(() =>
    {
        var (view, w, _) = Open();
        SolveSuvat(view);

        // The symbol toggle of t makes t the unknown; its box turns into the answer.
        var tToggle = view.GetVisualDescendants().OfType<PixelToggle>().First(t => Avalonia.Automation.AutomationProperties.GetName(t) == "Solve for t, time");
        Click(view, tToggle);
        Assert.Equal("t", view.Unknown);
        Assert.False(view.InputFor("t").IsVisible);
        Assert.True(view.InputFor("a").IsVisible);
        Assert.Equal(OutcomeKind.NeedsInput, view.LastOutcome!.Kind);
        Type(view, view.InputFor("a"), "2.5");
        Assert.Equal("t = 8 s", Answer(view));
        Assert.True(tToggle.IsChecked);

        // So does the SOLVE FOR toggle on a row; clicking it twice keeps it checked.
        var uSolve = view.GetVisualDescendants().OfType<PixelToggle>().First(t => Avalonia.Automation.AutomationProperties.GetName(t) == "Solve for u");
        Click(view, uSolve);
        Click(view, uSolve);
        Assert.Equal("u", view.Unknown);
        Assert.True(uSolve.IsChecked);
        Assert.Equal("u = 5 m/s", Answer(view));
        w.Close();
    });

    [Fact]
    public void CardsCopyAndUse() => Headless.Run(() =>
    {
        var (view, w, services) = Open();
        NavigationRequest? sent = null;
        services.NavigationRequested += (_, r) => sent = r;
        SolveSuvat(view);
        var card = view.CardControls.Single();
        var buttons = card.GetVisualDescendants().OfType<PixelButton>().ToList();

        Click(view, buttons.First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Copy card 1"));
        Assert.Equal("a = 2.5 m/s²", view.LastCopied);

        Click(view, buttons.First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Use card 1 in Advanced"));
        Assert.NotNull(sent);
        Assert.Equal("advanced", sent!.ModeId);
        var request = Assert.IsType<InputRequest>(sent.Payload);
        Assert.False(request.Evaluate);
        Assert.Equal("5/2", request.Text);
        Assert.Equal(2.5, TestSupport.Eval(request.Text));

        // OPEN brings the formula back with its values; the x removes the card.
        Show(view, "ohms-law");
        Click(view, buttons.First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Open card 1"));
        Assert.Equal("suvat-v-u-a-t", view.CurrentFormula.Id);
        Assert.Equal("25", view.InputFor("v").Text);
        Assert.Equal("a = 2.5 m/s²", Answer(view));
        Click(view, view.CardControls.Single().GetVisualDescendants().OfType<PixelButton>()
            .First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Remove card 1"));
        Assert.Empty(view.State.Cards);
        w.Close();
    });

    [Fact]
    public void TryExampleFillsSolvesAndKeeps() => Headless.Run(() =>
    {
        var (view, w, _) = Open();
        Show(view, "projectile-range");
        Click(view, Named<PixelButton>(view, "TryExample"));
        Assert.Equal("R", view.Unknown);
        Assert.Equal("20", view.InputFor("v").Text);
        Assert.Equal("R = 40.77471967 m", Answer(view));
        var card = Assert.Single(view.State.Cards);
        Assert.Equal("R = 40.77471967 m", card.Results.Single());

        // An empty box with a usual value uses it, shown as the placeholder.
        Assert.Equal("9.81", view.InputFor("g").PlaceholderText);
        Type(view, view.InputFor("g"), "");
        Assert.Equal("R = 40.77471967 m", Answer(view));
        Assert.Contains(view.LastOutcome!.Answer!.Notes, n => n.Contains("usual value", StringComparison.Ordinal));
        w.Close();
    });

    [Fact]
    public void ConstantsAreFilledInAndListed() => Headless.Run(() =>
    {
        var (view, w, _) = Open();
        Show(view, "photon-energy-frequency");
        Assert.Equal(2, view.Inputs.Count);
        var constants = Named<StackPanel>(view, "FormulaConstants");
        Assert.Single(constants.Children);
        Type(view, view.InputFor("f"), "5.5e14");
        Assert.Equal("E = 3.644338583×10⁻¹⁹ J", Answer(view));
        w.Close();
    });

    [Fact]
    public void IdentitiesAreChecked() => Headless.Run(() =>
    {
        var (view, w, _) = Open();
        Show(view, "pythagorean-identity");
        Assert.Equal("", view.Unknown);
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<PixelToggle>(),
            t => (Avalonia.Automation.AutomationProperties.GetName(t) ?? "").StartsWith("Solve for", StringComparison.Ordinal));
        Assert.Equal("Check both sides", Avalonia.Automation.AutomationProperties.GetName(Named<PixelButton>(view, "Solve")));
        Type(view, view.InputFor("θ"), "0.7");
        Assert.Equal("both sides = 1", Answer(view));
        Click(view, Named<PixelButton>(view, "Solve"));
        Assert.True(view.State.Cards.Single().IsCheck);
        w.Close();
    });

    [Fact]
    public void SearchFindsAndOpens() => Headless.Run(() =>
    {
        var (view, w, _) = Open();
        var search = Named<TextBox>(view, "FormulaSearch");
        Type(view, search, "snel");
        Assert.Equal("snells-law", view.SearchResults[0].Id);
        Assert.False(Named<ListBox>(view, "CategoryList").IsVisible);
        search.Focus();
        Press(view, w, Key.Enter);
        Assert.Equal("snells-law", view.CurrentFormula.Id);
        Assert.Equal("", search.Text);
        Assert.Equal("waves", view.OpenCategory);
        Assert.True(Named<ListBox>(view, "CategoryList").IsVisible);

        // Escape clears; Ctrl+F comes back to the search box.
        Type(view, search, "E=mc^2");
        Assert.Equal("mass-energy-equivalence", view.SearchResults[0].Id);
        Press(view, w, Key.Escape);
        Assert.Equal("", search.Text);
        view.Inputs[0].Focus();
        Press(view, w, Key.F, RawInputModifiers.Control);
        Assert.True(search.IsFocused);

        // A typo-free name that matches nothing says so.
        Type(view, search, "qqqzzzxxx");
        Assert.Empty(view.SearchResults);
        w.Close();
    });

    [Fact]
    public void PayloadsSearchOrOpen() => Headless.Run(() =>
    {
        var (view, w, _) = Open();
        view.Receive(new InputRequest("kinetic energy"));
        Idle(view);
        Assert.Equal("kinetic energy", Named<TextBox>(view, "FormulaSearch").Text);
        Assert.Equal("kinetic-energy", view.SearchResults[0].Id);
        Assert.Equal("suvat-v-u-a-t", view.CurrentFormula.Id);

        view.Receive(new InputRequest("E=mc^2", Evaluate: true));
        Idle(view);
        Assert.Equal("mass-energy-equivalence", view.CurrentFormula.Id);

        view.Receive(new FormulaRequest("ohms-law"));
        Idle(view);
        Assert.Equal("ohms-law", view.CurrentFormula.Id);

        view.Receive("pythagoras-theorem");
        Idle(view);
        Assert.Equal("pythagoras-theorem", view.CurrentFormula.Id);
        w.Close();
    });

    [Fact]
    public void CategoriesOpenAndClose() => Headless.Run(() =>
    {
        var (view, w, _) = Open();
        Assert.Equal("mechanics", view.OpenCategory);
        var list = Named<ListBox>(view, "CategoryList");
        int closed = TestSupport.Library.Categories.Count;
        Assert.Equal(closed + TestSupport.Library.ByCategory("mechanics").Count, list.ItemCount);
        view.ToggleCategory("geometry");
        Headless.Pump();
        Assert.Equal("geometry", view.OpenCategory);
        Assert.Equal(closed + TestSupport.Library.ByCategory("geometry").Count, list.ItemCount);
        view.ToggleCategory("geometry");
        Assert.Null(view.OpenCategory);
        Assert.Equal(closed, list.ItemCount);

        // Picking a formula in the list opens it.
        view.ExpandCategory("chemistry");
        var first = list.Items.OfType<ListBoxItem>().First(i => i.Tag is FormulaListEntry);
        list.SelectedItem = first;
        Idle(view);
        Assert.Equal(((FormulaListEntry)first.Tag!).Formula.Id, view.CurrentFormula.Id);
        w.Close();
    });

    [Fact]
    public void RelatedFormulasOpen() => Headless.Run(() =>
    {
        var (view, w, _) = Open();
        var related = Named<WrapPanel>(view, "Related").Children.OfType<PixelButton>().First();
        var target = (Calcy.Formulas.Formula)related.Tag!;
        Click(view, related);
        Assert.Equal(target.Id, view.CurrentFormula.Id);
        w.Close();
    });

    [Fact]
    public void ConstantUseGoesToTheFocusedBoxOrToAdvanced() => Headless.Run(() =>
    {
        var (view, w, services) = Open();
        NavigationRequest? sent = null;
        services.NavigationRequested += (_, r) => sent = r;
        view.ShowTab(FormulasTab.Constants);
        Headless.Pump();
        Assert.Equal(56, view.ListedConstants.Count);
        Type(view, Named<TextBox>(view, "FormulaSearch"), "planck");
        Assert.Equal("h", view.ListedConstants[0].Symbol);

        // With no value box focused, USE sends the constant to Advanced.
        view.UseConstant(view.ListedConstants[0]);
        Assert.Equal("advanced", sent!.ModeId);
        Assert.Equal("6.62607015e-34", ((InputRequest)sent.Payload!).Text);

        // With a box focused, it goes into the box at the caret.
        sent = null;
        Show(view, "mass-energy-equivalence");
        var m = view.InputFor("m");
        Type(view, m, "2*");
        m.Focus();
        m.CaretIndex = 2;
        Headless.Pump();
        Type(view, Named<TextBox>(view, "FormulaSearch"), "electron mass");
        Assert.Equal("m_e", view.ListedConstants[0].Symbol);
        view.UseConstant(view.ListedConstants[0]);
        Idle(view);
        Assert.Null(sent);
        Assert.Equal("2*9.1093837015e-31", m.Text);
        Assert.Equal("E = 1.637421155×10⁻¹³ J", Answer(view));
        w.Close();
    });

    [Fact]
    public void StateComesBack() => Headless.Run(() =>
    {
        var (view, w, services) = Open();
        SolveSuvat(view);
        view.SetUnknown("t");
        Idle(view);
        Show(view, "photon-energy-frequency");
        Type(view, view.InputFor("f"), "5e14");
        view.ShowTab(FormulasTab.Constants);
        w.Close();

        var (again, w2, _) = Open(services: services);
        Assert.Equal("photon-energy-frequency", again.CurrentFormula.Id);
        Assert.Equal("5e14", again.InputFor("f").Text);
        Assert.Equal(FormulasTab.Constants, again.CurrentTab);
        Assert.Single(again.State.Cards);
        again.OpenFormula(TestSupport.Formula("suvat-v-u-a-t"));
        Idle(again);
        Assert.Equal("t", again.Unknown);
        Assert.Equal("25", again.InputFor("v").Text);
        w2.Close();
    });

    [Fact]
    public void EquationIsTypesetInTheAuthorsOrder() => Headless.Run(() =>
    {
        var (view, w, _) = Open();
        Assert.True(view.EquationView.KeepTermOrder);
        Assert.False(view.EquationView.PreserveStructure);
        Assert.Equal("v = u + a*t", view.EquationView.Text);
        Assert.Null(view.EquationView.ErrorMessage);
        Assert.True(view.EquationView.Bounds.Height > 20);
        w.Close();
    });

    // ------------------------------------------------------------------ layout

    public static TheoryData<double, double, string> Sizes => new()
    {
        { 1360, 790, "suvat-v-u-a-t" },
        { 1100, 900, "suvat-v-u-a-t" },
        { 900, 510, "suvat-v-u-a-t" },
        { 900, 510, "determinant-3x3" },
        { 900, 510, "triangle-area-sine" },
        { 1100, 900, "pythagorean-identity" },
        { 900, 510, "photon-energy-wavelength" },
        { 900, 510, "population-standard-deviation" },
        { 900, 510, "charged-particle-radius" },
        { 1000, 600, "single-slit-minimum" },
        { 1920, 1050, "net-present-value" },
    };

    /// <summary>Nothing sticks out of its parent at any window size, for long names and many variables too.</summary>
    [Theory]
    [MemberData(nameof(Sizes))]
    public void EverythingStaysInsideItsParent(double width, double height, string id) => Headless.Run(() =>
    {
        var (view, w, _) = Open("nes", width, height);
        SeedCards(view);
        Show(view, id);
        view.TryExample();
        Idle(view);
        AssertFits(view);
        view.ShowTab(FormulasTab.Constants);
        Headless.Pump();
        AssertFits(view);
        view.ShowTab(FormulasTab.Formulas);
        Type(view, Named<TextBox>(view, "FormulaSearch"), "energy");
        AssertFits(view);
        w.Close();
    });

    /// <summary>The layout check itself: a box wider than its card and a cramped pixel label are both reported.</summary>
    [Fact]
    public void LayoutCheckCatchesOverflow() => Headless.Run(() =>
    {
        var box = new TextBox { MinWidth = 260 };
        var label = new PixelText { Text = "A LABEL THAT IS FAR TOO LONG", PixelSize = 2 };
        var card = new Border { Width = 200, Child = new StackPanel { Children = { box, new Border { Width = 120, Child = label } } } };
        var w = Headless.Show(new Grid { Children = { card } }, 400, 300);
        var problems = Problems(card);
        Assert.Contains(problems, p => p.StartsWith("TextBox", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("pixel text", StringComparison.Ordinal));
        w.Close();
    });

    /// <summary>
    /// Every input, button, toggle, typeset formula and pixel label lies inside each of its ancestors horizontally, and
    /// pixel text gets its full width (it cannot shrink). The panels fit the screen height.
    /// </summary>
    private static void AssertFits(FormulasView view)
    {
        Headless.Pump();
        var problems = Problems(view);
        foreach (var panel in view.GetVisualDescendants().OfType<PixelPanel>())
        {
            var at = panel.TranslatePoint(default, view);
            if (at is { } a && (a.Y + panel.Bounds.Height > view.Bounds.Height + 0.5 || a.X + panel.Bounds.Width > view.Bounds.Width + 0.5))
                problems.Add($"panel {panel.Header} ends at {a.X + panel.Bounds.Width:0}x{a.Y + panel.Bounds.Height:0} in {view.Bounds.Width:0}x{view.Bounds.Height:0}");
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems.Distinct().Take(20)));
    }

    private static List<string> Problems(Control view)
    {
        var problems = new List<string>();
        foreach (var c in view.GetVisualDescendants().OfType<Control>())
        {
            if (!c.IsEffectivelyVisible || c is not (TextBox or Button or PixelToggle or MathView or PixelText or Border { Classes.Count: > 0 })) continue;
            if (c is PixelText text && NaturalWidth(text) > text.Bounds.Width + 0.5)
                problems.Add($"pixel text '{text.Text}' needs {NaturalWidth(text):0} px but has {text.Bounds.Width:0}");
            for (var p = c.GetVisualParent() as Control; p is not null && !ReferenceEquals(p, view); p = p.GetVisualParent() as Control)
            {
                if (p is Avalonia.Controls.Presenters.ScrollContentPresenter) break;
                var at = c.TranslatePoint(default, p);
                if (at is null) break;
                double left = at.Value.X, right = left + c.Bounds.Width;
                if (left < -0.5 || right > p.Bounds.Width + 0.5)
                {
                    problems.Add($"{Describe(c)} spans {left:0}..{right:0} in {Describe(p)} of width {p.Bounds.Width:0}");
                    break;
                }
            }
        }
        return problems;
    }

    /// <summary>The width pixel text draws at, whatever space it was given (it never shrinks or wraps).</summary>
    private static double NaturalWidth(PixelText t)
    {
        int n = t.Text?.Length ?? 0;
        return n == 0 ? 0 : n * (PixelFont.Width + t.LetterSpacing) * t.PixelSize - t.LetterSpacing * t.PixelSize;
    }

    private static string Describe(Control c)
    {
        var name = Avalonia.Automation.AutomationProperties.GetName(c);
        return $"{c.GetType().Name}{(string.IsNullOrEmpty(c.Name) ? "" : "#" + c.Name)}{(string.IsNullOrEmpty(name) ? "" : $" '{name}'")}";
    }

    // ------------------------------------------------------------------ screenshots

    [Theory]
    [MemberData(nameof(Schemes))]
    public void Screenshot_mechanics_solved(string scheme) => Headless.Run(() =>
    {
        var (view, w, _) = Open(scheme);
        SeedCards(view);
        SolveSuvat(view);
        AssertFits(view);
        Assert.True(new FileInfo(Headless.Save(w, $"formulas-solved-{scheme}.png")).Length > 10_000);
        w.Close();
    });

    [Theory]
    [MemberData(nameof(Schemes))]
    public void Screenshot_worked_example(string scheme) => Headless.Run(() =>
    {
        var (view, w, _) = Open(scheme);
        Show(view, "projectile-range");
        Click(view, Named<PixelButton>(view, "TryExample"));
        Named<Border>(view, "Example").BringIntoView();
        Headless.Pump();
        AssertFits(view);
        Assert.True(new FileInfo(Headless.Save(w, $"formulas-example-{scheme}.png")).Length > 10_000);
        w.Close();
    });

    [Theory]
    [MemberData(nameof(Schemes))]
    public void Screenshot_constants(string scheme) => Headless.Run(() =>
    {
        var (view, w, _) = Open(scheme);
        Show(view, "photon-energy-wavelength");
        view.TryExample();
        Idle(view);
        view.ShowTab(FormulasTab.Constants);
        Headless.Pump();
        AssertFits(view);
        Assert.True(new FileInfo(Headless.Save(w, $"formulas-constants-{scheme}.png")).Length > 10_000);
        w.Close();
    });

    [Theory]
    [MemberData(nameof(Schemes))]
    public void Screenshot_search(string scheme) => Headless.Run(() =>
    {
        var (view, w, _) = Open(scheme);
        SeedCards(view);
        Show(view, "pythagorean-identity");
        view.TryExample();
        Idle(view);
        Type(view, Named<TextBox>(view, "FormulaSearch"), "energy");
        Assert.NotEmpty(view.SearchResults);
        AssertFits(view);
        Assert.True(new FileInfo(Headless.Save(w, $"formulas-search-{scheme}.png")).Length > 10_000);
        w.Close();
    });

    [Theory]
    [MemberData(nameof(Schemes))]
    public void Screenshot_narrow(string scheme) => Headless.Run(() =>
    {
        var (view, w, _) = Open(scheme, 900, 510);
        SeedCards(view);
        SolveSuvat(view);
        Assert.True(view.NarrowTable);
        AssertFits(view);
        Assert.True(new FileInfo(Headless.Save(w, $"formulas-narrow-{scheme}.png")).Length > 10_000);
        w.Close();
    });

    [Fact]
    public void Screenshot_narrow_page_top() => Headless.Run(() =>
    {
        var (view, w, _) = Open("nes", 900, 510);
        SeedCards(view);
        Show(view, "projectile-max-height");
        AssertFits(view);
        // The card actions stay on one line in the narrow cards column.
        var actions = view.CardControls[0].GetVisualDescendants().OfType<WrapPanel>().First(p => p.Children.OfType<PixelButton>().Count() == 4);
        Assert.Single(actions.Children.Select(c => c.Bounds.Y).Distinct());
        Assert.True(new FileInfo(Headless.Save(w, "formulas-narrow-top-nes.png")).Length > 10_000);
        w.Close();
    });

    [Fact]
    public void Screenshot_medium_window() => Headless.Run(() =>
    {
        var (view, w, _) = Open("nes", 1100, 900);
        SeedCards(view);
        Show(view, "determinant-3x3");
        view.TryExample();
        Idle(view);
        AssertFits(view);
        Assert.True(new FileInfo(Headless.Save(w, "formulas-medium-nes.png")).Length > 10_000);
        w.Close();
    });
}

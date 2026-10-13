using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Calcy.Modes.Finance.Logic;
using Calcy.Modes.Finance.Views;
using Calcy.Statistics;
using Calcy.UI.Controls;
using Calcy.UI.Services;
using Calcy.UI.TestKit;

namespace Calcy.Modes.Finance.Tests;

/// <summary>The Finance screen rendered headless: interaction, persistence and screenshots in several schemes.</summary>
public class FinanceViewTests
{
    // The app shows a mode below the 64 px top bar and above the status line.
    private const double Width = 1360;
    private const double Height = 790;

    private static (FinanceView View, Window Window, AppServices Services) Open(string scheme = "nes", double width = Width, double height = Height, AppServices? services = null)
    {
        services ??= Headless.CreateServices(scheme);
        var view = (FinanceView)new FinanceMode().CreateView(services);
        var window = Headless.Show(view, width, height);
        view.FocusFirst();
        Headless.Pump();
        return (view, window, services);
    }

    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Headless.Pump(1);
    }

    private static void Type(TextBox box, string text)
    {
        box.Text = text;
        box.ClearSelection();
        box.CaretIndex = text.Length;
        Headless.Pump(1);
    }

    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        Headless.Pump(1);
    }

    private static T Find<T>(Control root, Func<T, bool> match) where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(match);

    private static PixelButton ButtonNamed(Control root, string name) =>
        Find<PixelButton>(root, b => Avalonia.Automation.AutomationProperties.GetName(b) == name && b.IsEffectivelyVisible);

    /// <summary>The TI guidebook mortgage: 100,000 over 30 years at 8 %, solving PMT with its SOLVE button.</summary>
    private static void FillMortgage(FinanceView view)
    {
        Type(view.TvmBox(TvmVariable.N), "30*12");
        Type(view.TvmBox(TvmVariable.InterestRate), "8");
        Type(view.TvmBox(TvmVariable.PresentValue), "100000");
        Type(view.TvmBox(TvmVariable.Payment), "");
        Type(view.TvmBox(TvmVariable.FutureValue), "0");
        // A real click moves the focus to the button.
        view.SolveButton(TvmVariable.Payment).Focus();
        Click(view.SolveButton(TvmVariable.Payment));
    }

    [Theory]
    [InlineData("nes")]
    [InlineData("pico")]
    [InlineData("gameboy")]
    [InlineData("amber")]
    [InlineData("paper")]
    public void Screenshot_mortgage_and_schedule(string scheme)
    {
        var (tvm, schedule) = Headless.Run(() =>
        {
            var (view, window, _) = Open(scheme);
            FillMortgage(view);
            Assert.Equal("-733.7645739", view.TvmBox(TvmVariable.Payment).Text);
            Assert.Equal(-733.76, Money.Cents(view.TvmAnswer!.Value));
            Assert.Single(view.CardControls);
            Assert.Equal("#1  TVM: PMT", view.CardControls[0].Title);
            var tvm = Headless.Save(window, $"finance-{scheme}.png");

            view.ShowTab(FinanceTab.Amortization);
            Headless.Pump();
            var table = view.AmortizationTable;
            Assert.Equal(30 + 12, table.Rows.Count);
            Assert.Equal(["YEAR 1", "-8,805.17", "-7,969.81", "-835.36", "99,164.64"], table.Rows[0].Cells);
            Assert.Equal(["1", "-733.76", "-666.67", "-67.10", "99,932.90"], table.Rows[1].Cells);
            Assert.Equal(["TOTAL", "-264,155.25", "-164,155.25", "-100,000.00", "0.00"], table.Total!.Cells);
            return (tvm, Headless.Save(window, $"finance-amortization-{scheme}.png"));
        });
        Assert.True(new FileInfo(tvm).Length > 10_000, tvm);
        Assert.True(new FileInfo(schedule).Length > 10_000, schedule);
    }

    [Theory]
    [InlineData("nes")]
    [InlineData("pico")]
    [InlineData("gameboy")]
    [InlineData("amber")]
    [InlineData("paper")]
    public void Screenshot_cash_flows(string scheme)
    {
        var path = Headless.Run(() =>
        {
            var (view, window, _) = Open(scheme);
            view.ShowTab(FinanceTab.CashFlow);
            Headless.Pump();
            var r = view.CashFlowAnswer!;
            Assert.Equal([-1000.0, 300, 400, 500, 200], r.Expanded);
            Assert.Equal(2.6, r.Payback!.Value, 12);
            Click(ButtonNamed(view, "Add card: cash flows"));
            Assert.Equal("#1  CASH FLOWS", view.CardControls[0].Title);
            // Several answers: USE offers a choice.
            var use = ButtonNamed(view, "Use card 1 in Advanced");
            var menu = Assert.IsType<MenuFlyout>(use.Flyout);
            Assert.Equal(3, menu.Items.Count);
            return Headless.Save(window, $"finance-cashflow-{scheme}.png");
        });
        Assert.True(new FileInfo(path).Length > 10_000, path);
    }

    [Theory]
    [InlineData(FinanceTab.Interest)]
    [InlineData(FinanceTab.Dates)]
    [InlineData(FinanceTab.Depreciation)]
    [InlineData(FinanceTab.Bonds)]
    public void Screenshot_other_tabs(FinanceTab tab)
    {
        var path = Headless.Run(() =>
        {
            var (view, window, _) = Open("nes");
            view.ShowTab(tab);
            Headless.Pump();
            switch (tab)
            {
                case FinanceTab.Interest:
                    Assert.Equal("6.1678 % effective", view.ConversionText);
                    break;
                case FinanceTab.Dates:
                    Assert.Equal("350 days", view.ActualDaysText);
                    break;
                case FinanceTab.Depreciation:
                    Assert.Equal(5, view.DepreciationTable.Rows.Count);
                    break;
                case FinanceTab.Bonds:
                    Assert.NotNull(view.BondAnswer);
                    break;
            }
            return Headless.Save(window, $"finance-{tab.ToString().ToLowerInvariant()}-nes.png");
        });
        Assert.True(new FileInfo(path).Length > 10_000, path);
    }

    [Fact]
    public void Screenshot_small_window_with_errors()
    {
        Headless.Run(() =>
        {
            // The smallest main window (900 x 600) leaves 900 x 510 for the mode below the top bar.
            var (view, window, _) = Open("pico", 900, 510);
            Type(view.TvmBox(TvmVariable.N), "-5");
            Type(view.TvmBox(TvmVariable.PresentValue), "$1,200");
            Assert.Contains("error", view.TvmBox(TvmVariable.N).Classes);
            Assert.Equal("?", view.SolvedValueText);
            Headless.Save(window, "finance-small-errors-pico.png");
        });
    }

    [Theory]
    [InlineData(FinanceTab.Amortization)]
    [InlineData(FinanceTab.CashFlow)]
    [InlineData(FinanceTab.Interest)]
    public void Screenshot_small_window_tabs(FinanceTab tab)
    {
        var path = Headless.Run(() =>
        {
            var (view, window, _) = Open("paper", 900, 510);
            FillMortgage(view);
            view.ShowTab(tab);
            Headless.Pump();
            if (tab == FinanceTab.Amortization)
            {
                // The short window scrolls the whole page; show the table at the bottom.
                view.AmortizationTable.FindAncestorOfType<ScrollViewer>()!.ScrollToEnd();
                Headless.Pump();
            }
            return Headless.Save(window, $"finance-small-{tab.ToString().ToLowerInvariant()}-paper.png");
        });
        Assert.True(new FileInfo(path).Length > 10_000, path);
    }

    [Fact]
    public void First_run_shows_the_design_example_solving_pmt_live()
    {
        Headless.Run(() =>
        {
            var (view, _, _) = Open();
            var pmt = view.TvmBox(TvmVariable.Payment);
            Assert.Equal("", pmt.Text);
            Assert.Contains("solved", pmt.Classes);
            Assert.Equal("-372.86", pmt.PlaceholderText);
            Assert.Equal(-372.86, Money.Cents(view.TvmAnswer!.Value));
            Assert.Empty(view.CardControls);

            // Live: changing PV updates the answer without a card.
            Type(view.TvmBox(TvmVariable.PresentValue), "40000");
            Assert.Equal(-745.72, Money.Cents(view.TvmAnswer!.Value));
            Assert.Empty(view.CardControls);
        });
    }

    [Fact]
    public void Enter_solves_the_blank_field_and_escape_clears_a_field()
    {
        Headless.Run(() =>
        {
            var (view, window, _) = Open();
            var n = view.TvmBox(TvmVariable.N);
            n.Focus();
            Press(window, Key.Enter);
            Assert.Equal(Money.Field(Tvm.SolvePayment(60, 4.5, 20000, 0)), view.TvmBox(TvmVariable.Payment).Text);
            Assert.Single(view.CardControls);

            // PMT is now the solved field: it follows changes to the others.
            Type(view.TvmBox(TvmVariable.N), "48");
            Assert.Equal(Money.Field(Tvm.SolvePayment(48, 4.5, 20000, 0)), view.TvmBox(TvmVariable.Payment).Text);

            // Clear I% with Escape and solve it with Enter.
            var rate = view.TvmBox(TvmVariable.InterestRate);
            rate.Focus();
            Press(window, Key.Escape);
            Assert.Equal("", rate.Text);
            Press(window, Key.Enter);
            Assert.Equal(4.5, double.Parse(rate.Text!, System.Globalization.CultureInfo.InvariantCulture), 6);
            Assert.Equal("#2  TVM: I%", view.CardControls[0].Title);
        });
    }

    [Fact]
    public void Ctrl_enter_solves_the_focused_field_and_begin_mode_changes_the_answer()
    {
        Headless.Run(() =>
        {
            var (view, window, _) = Open();
            FillMortgage(view);
            var pv = view.TvmBox(TvmVariable.PresentValue);
            pv.Focus();
            Press(window, Key.Enter, RawInputModifiers.Control);
            Assert.Equal("#2  TVM: PV", view.CardControls[0].Title);
            Assert.Equal(100000, view.TvmAnswer!.Value, 3);

            Click(Find<PixelToggle>(view, t => Avalonia.Automation.AutomationProperties.GetName(t) == "Payments at the start of each period"));
            Assert.Equal(PaymentTiming.Begin, view.State.Tvm.Timing);
            Click(view.SolveButton(TvmVariable.Payment));
            Assert.Equal(-733.7645739 / (1 + 0.08 / 12), view.TvmAnswer!.Value, 6);
        });
    }

    [Fact]
    public void Invalid_fields_show_plain_messages()
    {
        Headless.Run(() =>
        {
            var (view, _, services) = Open();
            string? status = null;
            services.StatusMessage += (_, m) => status = m;
            Type(view.TvmBox(TvmVariable.N), "abc(");
            Type(view.TvmBox(TvmVariable.FutureValue), "q");
            var fv = view.TvmBox(TvmVariable.FutureValue);
            Assert.Contains("error", fv.Classes);
            Assert.Equal("'q' has no value yet: type a number, or define q in Advanced first.", ToolTip.GetTip(fv));
            Click(view.SolveButton(TvmVariable.Payment));
            Assert.Empty(view.CardControls);
            Assert.Equal("Fix the fields marked in red to see the answer.", status);
            Assert.Same(view.TvmBox(TvmVariable.N), TopLevel.GetTopLevel(view)!.FocusManager!.GetFocusedElement());
        });
    }

    [Fact]
    public void Presets_fill_the_fields_and_solve()
    {
        Headless.Run(() =>
        {
            var (view, _, _) = Open();
            Click(ButtonNamed(view, "Example: Retirement"));
            Assert.Equal("25*12", view.TvmBox(TvmVariable.N).Text);
            Assert.Equal("-500000", view.TvmBox(TvmVariable.PresentValue).Text);
            Assert.True(view.TvmAnswer!.Value > 0);
            Assert.Equal("#1  TVM: PMT", view.CardControls[0].Title);
        });
    }

    [Fact]
    public void Use_sends_the_calcy_expression_to_advanced()
    {
        Headless.Run(() =>
        {
            var (view, _, services) = Open();
            NavigationRequest? sent = null;
            services.NavigationRequested += (_, r) => sent = r;
            FillMortgage(view);
            Click(ButtonNamed(view, "Use card 1 in Advanced"));
            Assert.NotNull(sent);
            Assert.Equal("advanced", sent!.ModeId);
            var input = Assert.IsType<InputRequest>(sent.Payload);
            Assert.False(input.Evaluate);
            Assert.Equal("tvm_pmt(360, 8, 100000, 0, 12, 12)", input.Text);
            var value = services.Session.EvaluateExpression(input.Text, approximate: true);
            Assert.Equal(-733.7645739, ((Calcy.Core.Expressions.NumberExpr)value).Value.Re, 6);
        });
    }

    [Fact]
    public void Copy_and_export_csv_put_text_on_the_clipboard()
    {
        Headless.Run(() =>
        {
            var (view, _, _) = Open();
            FillMortgage(view);
            Click(ButtonNamed(view, "Copy card 1"));
            Assert.StartsWith("TVM: PMT\nN = 360, I% = 8, PV = 100,000.00, FV = 0.00\nPMT = ", view.LastCopied);

            view.ShowTab(FinanceTab.Amortization);
            Headless.Pump();
            Click(ButtonNamed(view, "Export CSV: the schedule"));
            Assert.StartsWith("Payment,Year,Amount,Interest,Principal,Balance\n1,1,-733.76,-666.67,-67.10,99932.90\n", view.LastCopied);

            Click(ButtonNamed(view, "Add card: amortization"));
            Assert.Equal("#2  AMORTIZATION", view.CardControls[0].Title);
            view.LastCopied = null;
            Click(ButtonNamed(view, "Export card 2 as CSV"));
            Assert.Equal(362, view.LastCopied!.TrimEnd('\n').Split('\n').Length);
        });
    }

    [Fact]
    public void Years_open_and_close()
    {
        Headless.Run(() =>
        {
            var (view, window, _) = Open();
            FillMortgage(view);
            view.ShowTab(FinanceTab.Amortization);
            Headless.Pump();
            Click(ButtonNamed(view, "Show every payment"));
            Assert.Equal(390, view.AmortizationTable.Rows.Count);
            Click(ButtonNamed(view, "Show years only"));
            Assert.Equal(30, view.AmortizationTable.Rows.Count);
            Assert.All(view.AmortizationTable.Rows, r => Assert.Equal(TableRowKind.Group, r.Kind));

            // Enter on a year row opens it.
            var year2 = view.AmortizationTable.GetVisualDescendants().OfType<TableRow>().First(r => r.Data.Group == 1);
            year2.Focus();
            Press(window, Key.Enter);
            Assert.Equal(42, view.AmortizationTable.Rows.Count);
            Assert.Equal("13", view.AmortizationTable.Rows[2].Cells[0]);
        });
    }

    [Fact]
    public void Amortization_explains_when_the_loan_is_not_solved()
    {
        Headless.Run(() =>
        {
            var (view, _, _) = Open();
            Type(view.TvmBox(TvmVariable.N), "");
            view.ShowTab(FinanceTab.Amortization);
            Headless.Pump();
            Assert.Null(view.AmortizationShown);
            Assert.True(ButtonNamed(view, "Go to the TVM tab").IsEffectivelyVisible);
        });
    }

    [Fact]
    public void State_and_cards_survive_a_new_view()
    {
        var services = Headless.Run(() =>
        {
            var (view, _, s) = Open();
            FillMortgage(view);
            view.ShowTab(FinanceTab.CashFlow);
            Type(view.FlowBox(0), "-2000");
            return s;
        });
        Headless.Run(() =>
        {
            var (view, _, _) = Open(services: services);
            Assert.Equal(FinanceTab.CashFlow, view.CurrentTab);
            Assert.Equal("-2000", view.FlowBox(0).Text);
            Assert.Equal("-733.7645739", view.TvmBox(TvmVariable.Payment).Text);
            Assert.Single(view.CardControls);
            Assert.Equal(TvmVariable.Payment, view.State.Solved);
        });
    }

    [Fact]
    public void Received_text_goes_into_the_focused_field()
    {
        Headless.Run(() =>
        {
            var (view, window, _) = Open();
            var pv = view.TvmBox(TvmVariable.PresentValue);
            pv.Focus();
            Headless.Pump();
            view.Receive(new InputRequest("12345.5"));
            Assert.Equal("12345.5", pv.Text);

            view.Receive(new InputRequest("50000", Evaluate: true));
            Assert.Equal("50000", pv.Text);
            Assert.Equal("#1  TVM: PMT", view.CardControls[0].Title);

            view.ShowTab(FinanceTab.CashFlow);
            view.Receive(new InputRequest("-5000"));
            Assert.Equal("-5000", view.FlowBox(0).Text);
        });
    }

    [Fact]
    public void Keyboard_switches_tabs()
    {
        Headless.Run(() =>
        {
            var (view, window, _) = Open();
            Press(window, Key.PageDown, RawInputModifiers.Control);
            Assert.Equal(FinanceTab.Amortization, view.CurrentTab);
            Press(window, Key.PageUp, RawInputModifiers.Control);
            Press(window, Key.PageUp, RawInputModifiers.Control);
            Assert.Equal(FinanceTab.Bonds, view.CurrentTab);
            Assert.True(view.IsKeyboardFocusWithin, "the first field of the new tab has the focus");
            Press(window, Key.PageDown, RawInputModifiers.Control);
            Assert.Equal(FinanceTab.Tvm, view.CurrentTab);
        });
    }

    [Fact]
    public void Mode_metadata()
    {
        var m = new FinanceMode();
        Assert.Equal(("finance", "Finance", "Loans, savings, cash flows", "Ctrl+8", "G5", "finance"), (m.Id, m.Title, m.Description, m.Shortcut, m.AccentToken, m.Icon));
    }
}

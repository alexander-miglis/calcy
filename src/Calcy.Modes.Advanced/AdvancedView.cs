using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Printing;
using Calcy.UI.Controls;
using Calcy.UI.Math;
using Calcy.UI.Services;

namespace Calcy.Modes.Advanced;

/// <summary>
/// The Advanced screen: result cards on top, the structured maths editor with template helpers below (design D4),
/// the symbol map and variables on the right. Every calculation becomes a card (design D5).
/// </summary>
public sealed class AdvancedView : UserControl, IReceivesPayload, IFocusFirst, IBusy
{
    private readonly AppServices _s;
    private readonly StackPanel _cards = new() { Spacing = 14, Margin = new Thickness(16) };
    private readonly ScrollViewer _cardScroll;
    private readonly MathEditor _input;
    private readonly TextBlock _preview = new() { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _hint = new() { FontSize = 13 };
    private readonly StackPanel _vars = new();
    private readonly WrapPanel _symbolGrid = new() { Margin = new Thickness(10) };
    private readonly WrapPanel _symbolTabs = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _symbolHint = new() { FontSize = 12, Margin = new Thickness(12, 0, 12, 12), TextWrapping = TextWrapping.Wrap };
    private readonly List<string> _history = [];
    private int _historyIndex = -1;
    private int _counter;
    private string _symbolTab = SymbolCatalog.Groups[0].Name;
    private CancellationTokenSource? _running;

    private const string DefaultHint = "Enter: calculate · Tab: next slot · ↑ ↓: history · Esc: clear or stop";

    public AdvancedView(AppServices services)
    {
        _s = services;
        _cardScroll = new ScrollViewer { Content = _cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var cardsPanel = new PixelPanel { Content = _cardScroll, MinHeight = 200 };
        cardsPanel.Bind(PixelPanel.BackgroundProperty, cardsPanel.GetResourceObservable("Calcy.Paper"));
        _cards.Children.Add(Welcome());

        _input = new MathEditor
        {
            PlaceholderText = "Type a calculation: / makes a fraction, ^ a power, sqrt a root",
            FontSize = 24,
            MinHeight = 64,
            Session = _s.Session,
        };
        Avalonia.Automation.AutomationProperties.SetName(_input, "Calculation input");
        _input.Submitted += async (_, _) => await EvaluateAsync();
        _input.TextChanged += (_, _) => UpdatePreview();
        _input.CaretSlotChanged += (_, e) => _hint.Text = string.IsNullOrEmpty(e.Hint) ? DefaultHint : e.Hint;
        // Keys the editor does not use (Up at the top line, Escape without a selection) come here.
        _input.KeyDown += OnInputKeyDown;

        var equals = new PixelButton { Content = "=", Classes = { "primary", "key" }, Width = 88, MinHeight = 64, FontSize = 26 };
        Avalonia.Automation.AutomationProperties.SetName(equals, "Calculate");
        equals.Click += async (_, _) => await EvaluateAsync();

        var inputRow = new DockPanel { Margin = new Thickness(16, 14, 16, 6) };
        DockPanel.SetDock(equals, Dock.Right);
        equals.Margin = new Thickness(14, 0, 0, 0);
        inputRow.Children.Add(equals);
        inputRow.Children.Add(_input);
        _hint.Bind(TextBlock.ForegroundProperty, _hint.GetResourceObservable("Calcy.Muted"));
        _hint.Text = DefaultHint;
        _preview.Bind(TextBlock.ForegroundProperty, _preview.GetResourceObservable("Calcy.Muted"));
        _preview.Bind(TextBlock.FontFamilyProperty, _preview.GetResourceObservable("Calcy.Font.Math"));
        var hintRow = new DockPanel { Margin = new Thickness(16, 0, 16, 12) };
        DockPanel.SetDock(_preview, Dock.Right);
        _preview.MaxWidth = 520;
        hintRow.Children.Add(_preview);
        hintRow.Children.Add(_hint);
        var inputPanel = new PixelPanel
        {
            Content = new StackPanel { Children = { TemplateBar(), inputRow, hintRow } },
            Margin = new Thickness(0, 16, 0, 0),
        };

        var left = new DockPanel();
        DockPanel.SetDock(inputPanel, Dock.Bottom);
        left.Children.Add(inputPanel);
        left.Children.Add(cardsPanel);

        var right = new DockPanel { Width = 380, Margin = new Thickness(20, 0, 0, 0) };
        var symbolPanel = new PixelPanel
        {
            Header = "Symbol map",
            Content = new StackPanel { Children = { TabStrip(), _symbolGrid, _symbolHint } },
        };
        DockPanel.SetDock(symbolPanel, Dock.Top);
        var clearVars = new PixelButton { Content = "CLEAR", Classes = { "small", "paper" } };
        clearVars.Click += (_, _) =>
        {
            _s.Session.Env.Clear();
            _s.ShowStatus("All variables and functions cleared");
        };
        var varsPanel = new PixelPanel
        {
            Header = "Variables",
            HeaderContent = clearVars,
            Margin = new Thickness(0, 16, 0, 0),
            Content = new ScrollViewer { Content = _vars },
        };
        varsPanel.Bind(PixelPanel.HeaderBrushProperty, varsPanel.GetResourceObservable("Calcy.Sel"));
        right.Children.Add(symbolPanel);
        right.Children.Add(varsPanel);

        var root = new DockPanel { Margin = new Thickness(20) };
        DockPanel.SetDock(right, Dock.Right);
        root.Children.Add(right);
        root.Children.Add(left);
        Content = root;

        _s.Session.Env.Changed += (_, _) => Dispatcher.UIThread.Post(RefreshVariables);
        _s.SettingsChanged += (_, _) => UpdatePreview();
        FillSymbols();
        RefreshVariables();
        UpdatePreview();
    }

    // ------------------------------------------------------------------ input

    public void FocusFirst() => _input.Focus();

    public bool IsBusy => _running is not null;

    public void Receive(object payload)
    {
        switch (payload)
        {
            case InputRequest r:
                _input.Text = r.Text;
                _input.Focus();
                if (r.Evaluate) _ = EvaluateAsync();
                break;
            case PlotRequest p when p.Expressions.Count > 0:
                _input.Text = p.Expressions[0];
                break;
        }
    }

    /// <summary>Types text at the caret as if it was typed, so it lands in the current slot.</summary>
    private void InsertText(string text)
    {
        _input.InsertText(text);
        _input.Focus();
    }

    private Control TemplateBar()
    {
        var bar = new WrapPanel { Margin = new Thickness(10, 8) };
        foreach (var t in MathTemplateCatalog.All)
        {
            var b = new PixelButton { Content = t.Glyph, Classes = { "small", "card" }, MinWidth = 46, Margin = new Thickness(0, 0, 6, 6) };
            b.Bind(PixelButton.FontFamilyProperty, b.GetResourceObservable("Calcy.Font.Math"));
            ToolTip.SetTip(b, string.IsNullOrEmpty(t.Shortcut) ? t.ToolTip : $"{t.ToolTip} (type {t.Shortcut})");
            Avalonia.Automation.AutomationProperties.SetName(b, "Insert " + t.Name);
            var kind = t.Kind;
            b.Click += (_, _) =>
            {
                _input.InsertTemplate(kind);
                _input.Focus();
            };
            bar.Children.Add(b);
        }
        var solve = new PixelButton { Content = "solve", Classes = { "small", "card" }, Margin = new Thickness(0, 0, 6, 6) };
        solve.Bind(PixelButton.FontFamilyProperty, solve.GetResourceObservable("Calcy.Font.Math"));
        ToolTip.SetTip(solve, "Solve an equation: solve(x^2 = 2, x)");
        solve.Click += (_, _) =>
        {
            InsertText("solve(");
            _hint.Text = "Type the equation, a comma and the unknown, then ): solve(x^2 = 2, x)";
        };
        bar.Children.Add(solve);
        var border = new Border { Child = bar, BorderThickness = new Thickness(0, 0, 0, 3) };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("Calcy.Key"));
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("Calcy.Line"));
        return border;
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up when _history.Count > 0:
                e.Handled = true;
                _historyIndex = _historyIndex < 0 ? _history.Count - 1 : Math.Max(0, _historyIndex - 1);
                _input.Text = _history[_historyIndex];
                break;
            case Key.Down when _historyIndex >= 0:
                e.Handled = true;
                _historyIndex++;
                _input.Text = _historyIndex >= _history.Count ? "" : _history[_historyIndex];
                if (_historyIndex >= _history.Count) _historyIndex = -1;
                break;
            case Key.Escape:
                e.Handled = true;
                if (_running is not null) _running.Cancel();
                else _input.Text = "";
                break;
        }
    }

    private void UpdatePreview()
    {
        var text = _input.Text ?? "";
        if (string.IsNullOrWhiteSpace(text))
        {
            _preview.Text = "";
            return;
        }
        try
        {
            var stmt = _s.Session.Parse(text);
            _preview.Text = "Reads as: " + stmt switch
            {
                Calcy.Core.Parsing.AssignStatement a => a.Name + " = " + ExprPrinter.ToText(a.Value),
                Calcy.Core.Parsing.FunctionDefStatement f => $"{f.Name}({string.Join(", ", f.Parameters)}) = {ExprPrinter.ToText(f.Body)}",
                Calcy.Core.Parsing.ExprStatement x => ExprPrinter.ToText(x.Expression),
                _ => "",
            };
            _preview.Bind(TextBlock.ForegroundProperty, _preview.GetResourceObservable("Calcy.Muted"));
        }
        catch (CalcException ex)
        {
            _preview.Text = ex.Message;
            _preview.Bind(TextBlock.ForegroundProperty, _preview.GetResourceObservable("Calcy.Err"));
        }
    }

    public async Task EvaluateAsync()
    {
        var text = (_input.Text ?? "").Trim();
        if (text.Length == 0 || _running is not null) return;
        _history.Add(text);
        _historyIndex = -1;
        _running = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = _running.Token;
        Card? pending = null;
        var timer = DispatcherTimer.RunOnce(() =>
        {
            pending = new Card { Title = "WORKING", Meta = "ESC TO STOP", Content = Typeset(text, 16, muted: true, preserve: true) };
            AddCardControl(pending);
        }, TimeSpan.FromMilliseconds(250));
        CalcResult result;
        try
        {
            result = await Task.Run(() => _s.Session.Evaluate(text, token), token);
        }
        catch (OperationCanceledException)
        {
            result = new CalcResult { Input = text, Kind = ResultKind.Error, ErrorMessage = "Stopped: the calculation took too long or was cancelled" };
        }
        finally
        {
            timer.Dispose();
            _running.Dispose();
            _running = null;
        }
        if (pending is not null) _cards.Children.Remove(pending);
        AddResultCard(result);
        if (!result.IsError) _input.Text = "";
        _hint.Text = DefaultHint;
        UpdatePreview();
    }

    // ------------------------------------------------------------------ cards

    private Control Welcome()
    {
        var t = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = "Every calculation becomes a card here, so you can scroll back, reuse and copy results.", Classes = { "body" } },
                new TextBlock { Text = "Try: 0.1 + 0.2,  sqrt(8),  sin(pi/3),  a = 5,  f(x) = x^2 + 1,  normalcdf(-1.96, 1.96)", Classes = { "math", "muted" }, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "In the input, / makes a fraction and ^ a power; Tab moves to the next empty slot.", Classes = { "body", "muted" }, TextWrapping = TextWrapping.Wrap },
            },
        };
        return new Card { Title = "WELCOME", Content = t };
    }

    /// <summary>Typeset maths from text (as typed when preserve is true).</summary>
    private MathView Typeset(string text, double size, bool muted = false, bool preserve = false)
    {
        var v = new MathView { Text = text, FontSize = size, Session = _s.Session, PreserveStructure = preserve, HorizontalAlignment = HorizontalAlignment.Left };
        if (muted) v.Bind(MathView.ForegroundProperty, v.GetResourceObservable("Calcy.Muted"));
        return v;
    }

    /// <summary>Typeset maths from an expression.</summary>
    private MathView Typeset(Expr e, double size, bool muted = false)
    {
        var v = new MathView { Expression = e, FontSize = size, Session = _s.Session, HorizontalAlignment = HorizontalAlignment.Left };
        if (muted) v.Bind(MathView.ForegroundProperty, v.GetResourceObservable("Calcy.Muted"));
        return v;
    }

    private void AddCardControl(Control card)
    {
        _cards.Children.Add(card);
        Dispatcher.UIThread.Post(() => _cardScroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    private void AddResultCard(CalcResult r)
    {
        _counter++;
        var time = DateTime.Now.ToString("HH:mm");
        string kind = r.Kind switch
        {
            ResultKind.Assignment => "stored",
            ResultKind.FunctionDefinition => "function",
            ResultKind.Error => "needs a fix",
            _ => Classify(r.Input),
        };
        var card = new Card { Title = $"#{_counter}  {kind}".ToUpperInvariant(), Meta = time, IsError = r.IsError };
        if (r.Kind is ResultKind.Assignment or ResultKind.FunctionDefinition)
            card.Bind(Card.HeaderBrushProperty, card.GetResourceObservable("Calcy.Sel"));

        if (r.IsError)
        {
            var body = new StackPanel { Spacing = 4 };
            body.Children.Add(new TextBlock { Text = r.Input, Classes = { "math" }, FontSize = 17, TextWrapping = TextWrapping.Wrap });
            if (r.ErrorPosition >= 0 && r.ErrorPosition <= r.Input.Length)
            {
                var caret = new TextBlock { Text = new string(' ', r.ErrorPosition) + "^", Classes = { "math" }, FontSize = 17, Margin = new Thickness(0, -6, 0, 0) };
                caret.Bind(TextBlock.ForegroundProperty, caret.GetResourceObservable("Calcy.Err"));
                body.Children.Add(caret);
            }
            body.Children.Add(new TextBlock { Text = r.ErrorMessage, Classes = { "body" }, FontSize = 15, TextWrapping = TextWrapping.Wrap });
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (r.Hint is { } hint)
            {
                var fix = new PixelButton { Content = "FIX IT", Classes = { "small", "sel" } };
                ToolTip.SetTip(fix, "Use " + hint);
                fix.Click += async (_, _) => { _input.Text = hint; await EvaluateAsync(); };
                actions.Children.Add(fix);
            }
            var edit = new PixelButton { Content = "EDIT", Classes = { "small" } };
            edit.Click += (_, _) => { _input.Text = r.Input; _input.Focus(); };
            actions.Children.Add(edit);
            card.Content = body;
            card.Actions = actions;
            AddCardControl(card);
            return;
        }

        var (primaryText, secondaryText) = _s.Session.Format(r);
        Expr exact = r.Exact!;
        Expr? approx = secondaryText is null ? null : r.Approx;
        bool decimalFirst = approx is not null && (_s.Settings.Results == ResultPreference.DecimalFirst || !r.ExactIsNice);
        Expr Shown(Expr value) => r.Kind switch
        {
            ResultKind.Assignment => new RelationExpr(RelOp.Equal, new SymbolExpr(r.Name!), value),
            ResultKind.FunctionDefinition => new RelationExpr(RelOp.Equal, new CallExpr(r.Name!, [.. r.Parameters.Select(p => (Expr)new SymbolExpr(p))]), value),
            _ => value,
        };

        var mainExpr = decimalFirst ? approx! : exact;
        var otherExpr = approx is null ? null : decimalFirst ? exact : approx;
        var primaryView = Typeset(Shown(mainExpr), 28);
        var secondarySign = new TextBlock { Classes = { "math", "muted" }, FontSize = 17, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 6, 0) };
        var secondaryView = otherExpr is null ? null : Typeset(otherExpr, 18, muted: true);
        bool exactShown = !decimalFirst;
        void SetSign() => secondarySign.Text = exactShown ? "≈" : "=";
        SetSign();

        var resultRow = new WrapPanel { Orientation = Orientation.Horizontal };
        resultRow.Children.Add(primaryView);
        if (secondaryView is not null)
        {
            secondaryView.VerticalAlignment = VerticalAlignment.Center;
            resultRow.Children.Add(secondarySign);
            resultRow.Children.Add(secondaryView);
        }
        card.Content = new StackPanel { Spacing = 8, Children = { Typeset(r.Input, 15, muted: true, preserve: true), resultRow } };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (otherExpr is not null && secondaryView is not null)
        {
            var swap = new PixelButton { Content = "S⇔D", Classes = { "small", "mem" } };
            Avalonia.Automation.AutomationProperties.SetName(swap, "Switch exact and decimal");
            swap.Click += (_, _) =>
            {
                (mainExpr, otherExpr) = (otherExpr, mainExpr);
                exactShown = !exactShown;
                primaryView.Expression = Shown(mainExpr);
                secondaryView.Expression = otherExpr;
                SetSign();
            };
            buttons.Children.Add(swap);
        }
        var use = new PixelButton { Content = "USE", Classes = { "small" } };
        ToolTip.SetTip(use, "Put this result into the input");
        use.Click += (_, _) =>
        {
            var expr = r.Kind == ResultKind.FunctionDefinition ? $"{r.Name}({string.Join(", ", r.Parameters)})" : ExprPrinter.ToInput(mainExpr);
            InsertText(r.Kind == ResultKind.Assignment ? r.Name! : expr);
        };
        buttons.Children.Add(use);
        var copy = new PixelButton { Content = "COPY", Classes = { "small" } };
        ToolTip.SetTip(copy, "Copy as text");
        copy.Click += async (_, _) => await Copy(ExprPrinter.Print(Shown(mainExpr), PrintOptions.Display));
        buttons.Children.Add(copy);
        var tex = new PixelButton { Content = "TEX", Classes = { "small" } };
        ToolTip.SetTip(tex, "Copy as LaTeX");
        tex.Click += async (_, _) => await Copy(LatexPrinter.ToLatex(Shown(mainExpr)));
        buttons.Children.Add(tex);
        var plotText = PlotText(r);
        if (plotText is not null)
        {
            var plot = new PixelButton { Content = "PLOT", Classes = { "small", "head" } };
            plot.Click += (_, _) => _s.Navigate("graphing", new PlotRequest([plotText]));
            buttons.Children.Add(plot);
        }
        card.Actions = buttons;
        AddCardControl(card);
    }

    private async Task Copy(string text)
    {
        var clip = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clip is not null) await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clip, text);
        _s.ShowStatus("Copied " + (text.Length > 60 ? text[..57] + "..." : text));
    }

    private static string Classify(string input)
    {
        var t = input.TrimStart();
        if (t.StartsWith("solve", StringComparison.OrdinalIgnoreCase) || t.StartsWith("nsolve", StringComparison.OrdinalIgnoreCase)) return "solve";
        if (t.StartsWith("diff", StringComparison.OrdinalIgnoreCase) || t.StartsWith("integrate", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("limit", StringComparison.OrdinalIgnoreCase) || t.StartsWith("taylor", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("nint", StringComparison.OrdinalIgnoreCase) || t.StartsWith("nderiv", StringComparison.OrdinalIgnoreCase)) return "calculus";
        if (t.StartsWith("factor", StringComparison.OrdinalIgnoreCase) || t.StartsWith("expand", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("simplify", StringComparison.OrdinalIgnoreCase)) return "algebra";
        return "calculation";
    }

    /// <summary>What to send to Graphing: a function definition, or a result that depends on x.</summary>
    private static string? PlotText(CalcResult r)
    {
        if (r.Kind == ResultKind.FunctionDefinition && r.Parameters.Count == 1) return $"y = {r.Name}({r.Parameters[0]})";
        if (r.Exact is null) return null;
        var free = r.Exact.FreeSymbols();
        if (free.Count == 1 && free[0] == "x" && r.Exact is not RelationExpr and not LogicExpr) return "y = " + ExprPrinter.ToInput(r.Exact);
        if (r.Exact is RelationExpr rel && rel.FreeSymbols().All(n => n is "x" or "y")) return ExprPrinter.ToInput(r.Exact);
        return null;
    }

    // ------------------------------------------------------------------ symbol map and variables

    private Control TabStrip()
    {
        var border = new Border { Child = _symbolTabs, BorderThickness = new Thickness(0, 0, 0, 3) };
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("Calcy.Line"));
        foreach (var group in SymbolCatalog.Groups)
        {
            var tab = new PixelToggle { Content = group.Name.ToUpperInvariant(), IsChecked = group.Name == _symbolTab, BorderThickness = new Thickness(0, 0, 2, 2), FontSize = 10, Padding = new Thickness(7, 4) };
            var n = group.Name;
            tab.Click += (_, _) =>
            {
                _symbolTab = n;
                foreach (var c in _symbolTabs.Children.OfType<PixelToggle>()) c.IsChecked = Equals(c.Content, n.ToUpperInvariant());
                FillSymbols();
            };
            _symbolTabs.Children.Add(tab);
        }
        return border;
    }

    private void FillSymbols()
    {
        _symbolGrid.Children.Clear();
        var group = SymbolCatalog.Groups.First(g => g.Name == _symbolTab);
        foreach (var sym in group.Symbols)
        {
            var b = new PixelButton { Content = sym.Glyph, Classes = { "small", "card", "flat" }, Width = 54, Height = 42, Margin = new Thickness(0, 0, 4, 4), FontSize = sym.Glyph.Length > 3 ? 11 : 16 };
            b.Bind(PixelButton.FontFamilyProperty, b.GetResourceObservable("Calcy.Font.Math"));
            bool typeable = sym.Template is null && sym.Insert != sym.Glyph && sym.Insert.All(ch => ch < 128);
            ToolTip.SetTip(b, typeable ? $"{sym.Name} (or type {sym.Insert.TrimEnd('(')})" : sym.Name);
            Avalonia.Automation.AutomationProperties.SetName(b, sym.Name);
            var s = sym;
            b.Click += (_, _) =>
            {
                _input.InsertSymbol(s);
                _input.Focus();
                _symbolHint.Text = typeable ? $"Inserted {s.Glyph}. You can also type {s.Insert.TrimEnd('(')}." : $"Inserted {s.Glyph}.";
            };
            _symbolGrid.Children.Add(b);
        }
        _symbolHint.Text = "Click a symbol to put it at the cursor. Hover to see what to type instead.";
        _symbolHint.Bind(TextBlock.ForegroundProperty, _symbolHint.GetResourceObservable("Calcy.Muted"));
    }

    private void RefreshVariables()
    {
        _vars.Children.Clear();
        var env = _s.Session.Env;
        var rows = new List<(Expr Shown, string Insert, string Name)>();
        foreach (var (name, f) in env.Functions.OrderBy(k => k.Key))
            rows.Add((new RelationExpr(RelOp.Equal, new CallExpr(name, [.. f.Parameters.Select(p => (Expr)new SymbolExpr(p))]), f.Body), $"{name}(", name));
        foreach (var (name, v) in env.Variables.Where(k => k.Key != "ans").OrderBy(k => k.Key))
            rows.Add((new RelationExpr(RelOp.Equal, new SymbolExpr(name), v), name, name));
        if (env.TryGetVariable("ans", out var ans)) rows.Add((new RelationExpr(RelOp.Equal, new SymbolExpr("ans"), ans), "ans", "ans"));
        if (rows.Count == 0)
        {
            var empty = new TextBlock { Text = "No variables yet. Define one with a = 5 or f(x) = x^2.", Classes = { "body", "muted" }, Margin = new Thickness(14), TextWrapping = TextWrapping.Wrap };
            _vars.Children.Add(empty);
            return;
        }
        foreach (var (shown, insert, name) in rows)
        {
            // Short values are typeset; long ones (a list of regression results, say) would shrink to nothing, so they are cut off as text.
            var text = ExprPrinter.ToText(shown);
            Control label = text.Length <= 36
                ? new MathView { Expression = shown, FontSize = 16, Session = _s.Session, VerticalAlignment = VerticalAlignment.Center, OverflowMode = MathOverflowMode.Scale }
                : new TextBlock { Text = text, Classes = { "math" }, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTip.SetTip(label, text);
            var use = new PixelButton { Content = "USE", Classes = { "small", "flat" } };
            use.Click += (_, _) => InsertText(insert);
            var remove = new PixelButton { Content = new PixelIcon { Kind = "close", Width = 12, Height = 12 }, Classes = { "small", "flat", "paper" }, Width = 32, Padding = new Thickness(0) };
            ((PixelIcon)remove.Content).Bind(PixelIcon.ForegroundProperty, remove.GetResourceObservable("Calcy.Ink"));
            ToolTip.SetTip(remove, "Delete " + name);
            remove.Click += (_, _) => env.Remove(name);
            var row = new DockPanel { Margin = new Thickness(14, 6) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { use, remove } };
            DockPanel.SetDock(buttons, Dock.Right);
            row.Children.Add(buttons);
            row.Children.Add(label);
            var line = new Border { Child = row, BorderThickness = new Thickness(0, 0, 0, 2) };
            line.Bind(Border.BorderBrushProperty, line.GetResourceObservable("Calcy.Key"));
            _vars.Children.Add(line);
        }
    }
}

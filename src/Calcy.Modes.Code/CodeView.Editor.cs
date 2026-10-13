using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Calcy.Modes.Code.Programs;
using Calcy.Modes.Code.Views;
using Calcy.Scripting;
using Calcy.UI.Controls;

namespace Calcy.Modes.Code;

public sealed partial class CodeView
{
    private readonly WrapPanel _tabs = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = Ui.Mono("Ready", 13, "Calcy.Muted");
    private readonly TextBlock _caretStatus = Ui.Mono("Ln 1, Col 1", 13, "Calcy.Muted");
    private PixelButton _runButton = null!;
    private PixelButton _stopButton = null!;
    private TextBlock _runLabel = null!;
    private Control _footerChip = null!;
    private Control _footerModules = null!;
    private Control _footerStatus = null!;
    private Border _findBar = null!;
    private TextBox _findBox = null!;
    private TextBox _replaceBox = null!;
    private TextBlock _findCount = null!;
    private PixelToggle _findCase = null!;

    /// <summary>The file tabs (for tests).</summary>
    internal IEnumerable<PixelToggle> Tabs => _tabs.Children.OfType<PixelToggle>();

    /// <summary>The status text in the editor footer.</summary>
    public string StatusText => _status.Text ?? "";

    private Control BuildEditorSection()
    {
        // Header: file tabs and the + button on the left, RUN, STOP and FILES on the right (design: var(--key) strip).
        _runLabel = Ui.Label("RUN", 13);
        _runButton = new PixelButton
        {
            Classes = { "ok" },
            MinHeight = 44,
            Padding = new Thickness(14, 0),
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Ui.Icon("run", 14), _runLabel } },
        };
        ToolTip.SetTip(_runButton, "Run the program (F5)");
        Avalonia.Automation.AutomationProperties.SetName(_runButton, "Run");
        _runButton.Click += async (_, _) => await RunCurrentAsync();

        _stopButton = new PixelButton
        {
            Classes = { "paper" },
            MinHeight = 44,
            Padding = new Thickness(14, 0),
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Ui.Icon("stop", 14), Ui.Label("STOP", 13) } },
        };
        ToolTip.SetTip(_stopButton, "Stop the program (Esc or Ctrl+Break)");
        Avalonia.Automation.AutomationProperties.SetName(_stopButton, "Stop");
        _stopButton.Click += (_, _) => Stop();

        var files = new PixelButton
        {
            Classes = { "paper", "flat" },
            MinHeight = 44,
            Padding = new Thickness(12, 0),
            Content = Ui.Label("FILES", 13),
        };
        ToolTip.SetTip(files, "Your programs and the examples gallery");
        Avalonia.Automation.AutomationProperties.SetName(files, "Files and examples");
        var filesFlyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight, Content = new Border() };
        filesFlyout.Opening += (_, _) => filesFlyout.Content = BuildFilesPanel(files);
        files.Flyout = filesFlyout;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { _runButton, _stopButton, files } };
        var header = new DockPanel { Margin = new Thickness(10, 8) };
        DockPanel.SetDock(buttons, Dock.Right);
        buttons.Margin = new Thickness(8, 0, 0, 0);
        header.Children.Add(buttons);
        header.Children.Add(_tabs);
        var headerBar = new Border { Child = header, BorderThickness = new Thickness(0, 0, 0, 4) };
        headerBar.Bind(Border.BackgroundProperty, "Calcy.Key");
        headerBar.Bind(Border.BorderBrushProperty, "Calcy.Line");

        _findBar = BuildFindBar();

        // Footer: the sandbox chip, the modules, and the status.
        var chip = new Border
        {
            BorderThickness = new Thickness(2),
            Padding = new Thickness(8, 4),
            Child = Ui.Label("SANDBOX: NO FILES, NO NETWORK, NO SYSTEM", 11),
            VerticalAlignment = VerticalAlignment.Center,
        };
        chip.Bind(Border.BackgroundProperty, "Calcy.Ok");
        chip.Bind(Border.BorderBrushProperty, "Calcy.Line");
        ToolTip.SetTip(chip, "Programs cannot read or write files, use the network or start other programs.");

        var modulesText = Ui.Body("Modules: math, random, time, calc, turtle, ti_plotlib, casioplot", 13, "Calcy.Muted");
        modulesText.TextWrapping = TextWrapping.NoWrap;
        modulesText.TextTrimming = TextTrimming.CharacterEllipsis;
        var modules = new Button
        {
            Content = modulesText,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(4, 2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Classes = { "link" },
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(modules, "Show every module and function you can use");
        Avalonia.Automation.AutomationProperties.SetName(modules, "Module reference");
        var modulesFlyout = new Flyout { Placement = PlacementMode.TopEdgeAlignedLeft, Content = new Border() };
        modulesFlyout.Opening += (_, _) => modulesFlyout.Content = BuildReferencePanel();
        modules.Flyout = modulesFlyout;

        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, VerticalAlignment = VerticalAlignment.Center, Children = { _caretStatus, _status } };
        var footer = new DockPanel { Margin = new Thickness(12, 8) };
        _footerChip = chip;
        _footerModules = modules;
        _footerStatus = statusRow;
        DockPanel.SetDock(chip, Dock.Left);
        DockPanel.SetDock(statusRow, Dock.Right);
        footer.Children.Add(chip);
        footer.Children.Add(statusRow);
        modules.Margin = new Thickness(10, 0);
        footer.Children.Add(modules);
        var footerBar = new Border { Child = footer, BorderThickness = new Thickness(0, 3, 0, 0) };
        footerBar.Bind(Border.BorderBrushProperty, "Calcy.Line");

        var editorHost = new Border { Child = _editor, MinHeight = 200 };
        var dock = new DockPanel();
        DockPanel.SetDock(headerBar, Dock.Top);
        DockPanel.SetDock(_findBar, Dock.Top);
        DockPanel.SetDock(footerBar, Dock.Bottom);
        dock.Children.Add(headerBar);
        dock.Children.Add(_findBar);
        dock.Children.Add(footerBar);
        dock.Children.Add(editorHost);

        var section = new Border
        {
            Child = dock,
            BorderThickness = new Thickness(4),
            ClipToBounds = false,
        };
        section.Bind(Border.BackgroundProperty, "Calcy.Paper");
        section.Bind(Border.BorderBrushProperty, "Calcy.Line");
        section.Bind(Border.BoxShadowProperty, "Calcy.ShadowLarge");
        Avalonia.Automation.AutomationProperties.SetName(section, "Editor");
        return section;
    }

    // ------------------------------------------------------------------ tabs

    private void RebuildTabs()
    {
        _tabs.Children.Clear();
        foreach (var p in _store.Programs)
        {
            var program = p;
            var tab = new PixelToggle
            {
                Content = program.Name,
                IsChecked = program == _current,
                MinHeight = 40,
                Padding = new Thickness(12, 0),
                Margin = new Thickness(0, 0, -2, 0),
                FontSize = 14,
                FontWeight = FontWeight.Normal,
                Tag = program,
            };
            tab.Bind(TemplatedControl.FontFamilyProperty, "Calcy.Font.Math");
            Avalonia.Automation.AutomationProperties.SetName(tab, program.Name);
            ToolTip.SetTip(tab, "Double-click to rename; right-click for more");
            tab.Click += (_, _) =>
            {
                if (program != _current) Open(program);
                else tab.IsChecked = true;
            };
            tab.DoubleTapped += (_, _) => BeginRename(tab, program);
            tab.ContextMenu = TabMenu(tab, program);
            _tabs.Children.Add(tab);
        }
        var add = new PixelButton
        {
            Content = "+",
            Classes = { "paper", "flat" },
            Width = 40,
            MinHeight = 40,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(2),
            FontSize = 14,
        };
        ToolTip.SetTip(add, "New program");
        Avalonia.Automation.AutomationProperties.SetName(add, "New file");
        add.Click += (_, _) => NewProgram();
        _tabs.Children.Add(add);
    }

    private ContextMenu TabMenu(PixelToggle tab, ProgramFile program)
    {
        MenuItem Item(string header, Action action)
        {
            var m = new MenuItem { Header = header };
            m.Click += (_, _) => action();
            return m;
        }
        return new ContextMenu
        {
            Items =
            {
                Item("Rename", () => BeginRename(tab, program)),
                Item("Duplicate", () => Duplicate(program)),
                Item("Delete", () => ConfirmDelete(tab, program)),
                new Separator(),
                Item("New program", () => NewProgram()),
            },
        };
    }

    private void BeginRename(PixelToggle tab, ProgramFile program)
    {
        var box = new TextBox
        {
            Text = program.Name,
            Classes = { "find" },
            FontSize = 14,
            MinHeight = 30,
            MinWidth = 120,
            Padding = new Thickness(4, 2),
            BorderThickness = new Thickness(0, 0, 0, 2),
        };
        box.Bind(TextBox.FontFamilyProperty, "Calcy.Font.Math");
        Avalonia.Automation.AutomationProperties.SetName(box, "Program name");
        bool done = false;
        void Commit(bool keep)
        {
            if (done) return;
            done = true;
            if (keep && !Rename(program, box.Text ?? "")) _s.ShowStatus("That name cannot be used");
            RebuildTabs();
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Commit(true); e.Handled = true; }
            else if (e.Key == Key.Escape) { Commit(false); e.Handled = true; }
        };
        box.LostFocus += (_, _) => Commit(true);
        tab.Content = box;
        Dispatcher.UIThread.Post(() =>
        {
            box.Focus();
            int stem = Math.Max(0, (box.Text ?? "").Length - 3);
            box.SelectionStart = 0;
            box.SelectionEnd = stem;
        }, DispatcherPriority.Input);
    }

    private void ConfirmDelete(Control anchor, ProgramFile program)
    {
        var flyout = new Flyout { Placement = PlacementMode.Bottom };
        var yes = Ui.SmallButton("DELETE", $"Delete {program.Name}", "err");
        var no = Ui.SmallButton("KEEP", "Keep the program");
        yes.Click += (_, _) =>
        {
            flyout.Hide();
            Delete(program);
        };
        no.Click += (_, _) => flyout.Hide();
        flyout.Content = Ui.Box(new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(12),
            Children =
            {
                Ui.Body($"Delete {program.Name}? This cannot be undone.", 13),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { yes, no } },
            },
        }, "Calcy.Paper", 0);
        flyout.ShowAt(anchor);
    }

    // ------------------------------------------------------------------ files and examples

    internal Control BuildFilesPanel(Button owner)
    {
        var list = new StackPanel { Spacing = 6, Margin = new Thickness(12) };
        var newButton = Ui.SmallButton("NEW", "New program", "ok");
        newButton.Click += (_, _) =>
        {
            owner.Flyout?.Hide();
            NewProgram();
        };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(newButton, Dock.Right);
        head.Children.Add(newButton);
        head.Children.Add(Ui.Label("YOUR PROGRAMS", 12));
        list.Children.Add(head);
        foreach (var p in _store.Programs)
        {
            var program = p;
            var name = Ui.Mono(program.Name, 14, bold: program == _current);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            var open = Ui.SmallButton("OPEN", $"Open {program.Name}");
            open.Click += (_, _) =>
            {
                owner.Flyout?.Hide();
                Open(program);
            };
            var copy = Ui.SmallButton("COPY", $"Duplicate {program.Name}");
            copy.Click += (_, _) =>
            {
                owner.Flyout?.Hide();
                Duplicate(program);
            };
            var rename = Ui.SmallButton("RENAME", $"Rename {program.Name}");
            rename.Click += (_, _) =>
            {
                owner.Flyout?.Hide();
                var tab = Tabs.FirstOrDefault(t => t.Tag == program);
                if (tab is not null) BeginRename(tab, program);
            };
            var delete = Ui.SmallButton("DELETE", $"Delete {program.Name}", "err");
            delete.Click += (_, _) =>
            {
                owner.Flyout?.Hide();
                var tab = Tabs.FirstOrDefault(t => t.Tag == program);
                ConfirmDelete((Control?)tab ?? _tabs, program);
            };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { open, rename, copy, delete } };
            var row = new DockPanel();
            DockPanel.SetDock(actions, Dock.Right);
            row.Children.Add(actions);
            row.Children.Add(name);
            list.Children.Add(row);
        }
        list.Children.Add(new Border { Height = 3, Margin = new Thickness(0, 8) }.Bind(Border.BackgroundProperty, "Calcy.Line"));
        list.Children.Add(Ui.Label("EXAMPLES", 12));
        foreach (var e in Examples.All)
        {
            var example = e;
            var open = Ui.SmallButton("OPEN", $"Open a copy of {example.Title}", "sel");
            open.Click += (_, _) =>
            {
                owner.Flyout?.Hide();
                OpenExample(example);
            };
            var text = new StackPanel
            {
                Spacing = 1,
                Children = { Ui.Body(example.Title, 14), Ui.Body(example.Description, 12, "Calcy.Muted") },
            };
            ((TextBlock)text.Children[0]).FontWeight = FontWeight.Bold;
            var row = new DockPanel { Margin = new Thickness(0, 2) };
            DockPanel.SetDock(open, Dock.Right);
            open.VerticalAlignment = VerticalAlignment.Center;
            open.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(open);
            row.Children.Add(text);
            list.Children.Add(row);
        }
        var scroll = new ScrollViewer { Content = list, MaxHeight = 560, Width = 480, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        return Ui.Box(scroll, "Calcy.Paper", 0);
    }

    internal static Control BuildReferencePanel()
    {
        var list = new StackPanel { Spacing = 2, Margin = new Thickness(12) };
        list.Children.Add(Ui.Body("Every module also has help(): try help(turtle.forward) in the REPL. Standard modules: math, cmath, random, time, string, itertools, functools, collections, operator.", 12, "Calcy.Muted"));
        foreach (var group in ScriptHost.Reference.GroupBy(d => d.Module))
        {
            var title = Ui.Label(group.Key.ToUpperInvariant(), 12);
            title.Margin = new Thickness(0, 10, 0, 2);
            list.Children.Add(title);
            foreach (var d in group)
            {
                var sig = Ui.Mono(d.Signature.Contains('(') && d.Name.Contains('.') ? d.Name.Split('.')[0] + "." + d.Signature : d.Signature, 13, bold: true);
                sig.TextWrapping = TextWrapping.Wrap;
                list.Children.Add(sig);
                var summary = Ui.Body(d.Summary, 12, "Calcy.Muted");
                summary.Margin = new Thickness(12, 0, 0, 4);
                list.Children.Add(summary);
            }
        }
        var scroll = new ScrollViewer { Content = list, MaxHeight = 520, Width = 520, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        return Ui.Box(scroll, "Calcy.Paper", 0);
    }

    // ------------------------------------------------------------------ find

    private Border BuildFindBar()
    {
        _findBox = new TextBox { Classes = { "find" }, Width = 200, MinHeight = 30, FontSize = 14, Padding = new Thickness(4, 2), BorderThickness = new Thickness(0, 0, 0, 2), PlaceholderText = "Find" };
        _findBox.Bind(TextBox.FontFamilyProperty, "Calcy.Font.Math");
        Avalonia.Automation.AutomationProperties.SetName(_findBox, "Find");
        _replaceBox = new TextBox { Classes = { "find" }, Width = 160, MinHeight = 30, FontSize = 14, Padding = new Thickness(4, 2), BorderThickness = new Thickness(0, 0, 0, 2), PlaceholderText = "Replace with" };
        _replaceBox.Bind(TextBox.FontFamilyProperty, "Calcy.Font.Math");
        Avalonia.Automation.AutomationProperties.SetName(_replaceBox, "Replace with");
        _findCount = Ui.Mono("", 13, "Calcy.Muted");
        _findCount.MinWidth = 70;
        _findCase = new PixelToggle { Content = "Aa", MinHeight = 30, Padding = new Thickness(8, 0) };
        ToolTip.SetTip(_findCase, "Match case");
        Avalonia.Automation.AutomationProperties.SetName(_findCase, "Match case");
        var prev = Ui.SmallButton("PREV", "Previous match (Shift+F3)");
        var next = Ui.SmallButton("NEXT", "Next match (F3 or Enter)");
        var replace = Ui.SmallButton("REPLACE", "Replace this match");
        var all = Ui.SmallButton("ALL", "Replace every match");
        var close = Ui.SmallButton("×", "Close (Esc)");
        _findBox.TextChanged += (_, _) =>
        {
            _editor.FindText = _findBox.Text ?? "";
            UpdateFindCount();
        };
        _findBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _editor.FindPrevious();
                else _editor.FindNext();
                e.Handled = true;
            }
        };
        _findCase.IsCheckedChanged += (_, _) =>
        {
            _editor.FindMatchCase = _findCase.IsChecked == true;
            UpdateFindCount();
        };
        prev.Click += (_, _) => _editor.FindPrevious();
        next.Click += (_, _) => _editor.FindNext();
        replace.Click += (_, _) =>
        {
            _editor.ReplaceNext(_replaceBox.Text ?? "");
            UpdateFindCount();
        };
        all.Click += (_, _) =>
        {
            int n = _editor.ReplaceAll(_replaceBox.Text ?? "");
            _s.ShowStatus(n == 1 ? "Replaced 1 match" : $"Replaced {n} matches");
            UpdateFindCount();
        };
        close.Click += (_, _) => HideFind();
        var row = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 6),
            ItemSpacing = 6,
            LineSpacing = 6,
            Children = { Ui.Label("FIND", 11), _findBox, _findCount, _findCase, prev, next, _replaceBox, replace, all, close },
        };
        foreach (var c in row.Children) c.VerticalAlignment = VerticalAlignment.Center;
        var bar = new Border { Child = row, BorderThickness = new Thickness(0, 0, 0, 3), IsVisible = false };
        bar.Bind(Border.BackgroundProperty, "Calcy.Paper");
        bar.Bind(Border.BorderBrushProperty, "Calcy.Line");
        return bar;
    }

    /// <summary>Shows the find bar with the selected text (Ctrl+F).</summary>
    public void ShowFind()
    {
        _findBar.IsVisible = true;
        var selected = _editor.SelectedText;
        if (selected.Length > 0 && !selected.Contains('\n')) _findBox.Text = selected;
        _editor.FindText = _findBox.Text ?? "";
        UpdateFindCount();
        Dispatcher.UIThread.Post(() =>
        {
            _findBox.Focus();
            _findBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    /// <summary>Hides the find bar and the match highlights.</summary>
    public void HideFind()
    {
        _findBar.IsVisible = false;
        _editor.FindText = "";
        _editor.FocusEditor();
    }

    /// <summary>True while the find bar is shown.</summary>
    public bool IsFindVisible => _findBar.IsVisible;

    private void UpdateFindCount()
    {
        if (string.IsNullOrEmpty(_findBox.Text))
        {
            _findCount.Text = "";
            return;
        }
        int n = _editor.FindMatchCount;
        _findCount.Text = n == 0 ? "no match" : n == 1 ? "1 match" : $"{n} matches";
    }

    // ------------------------------------------------------------------ status

    private void UpdateCaretStatus()
    {
        var c = _editor.Caret;
        _caretStatus.Text = $"Ln {c.Line + 1}, Col {c.Column + 1}";
    }

    private void SetStatus(string text) => _status.Text = text;

    /// <summary>On very narrow screens the footer puts the status under the sandbox chip.</summary>
    private void ArrangeFooter(bool tiny)
    {
        DockPanel.SetDock(_footerChip, tiny ? Dock.Top : Dock.Left);
        DockPanel.SetDock(_footerStatus, tiny ? Dock.Top : Dock.Right);
        _footerChip.HorizontalAlignment = tiny ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        _footerStatus.Margin = tiny ? new Thickness(0, 6, 0, 0) : new Thickness(0);
        _footerStatus.HorizontalAlignment = tiny ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        _footerModules.IsVisible = !tiny;
    }
}

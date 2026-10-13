using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Calcy.Core;
using Calcy.UI.Controls;
using Calcy.UI.Services;
using Calcy.UI.Theming;

namespace Calcy.App;

/// <summary>Settings: results and input conventions, angles and number format, colour scheme, sandbox and AI connections.</summary>
internal sealed class SettingsView : UserControl
{
    private readonly AppServices _s;
    private readonly WrapPanel _schemes = new() { Orientation = Orientation.Horizontal };

    public SettingsView(AppServices services, Action back)
    {
        _s = services;
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(24, 24, 24, 16) };

        // The settings button, Ctrl+, and Esc close Settings too; this button is for anyone who doesn't know that.
        var close = new PixelButton { Content = "CLOSE", Classes = { "primary" }, MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 12, 0) };
        ToolTip.SetTip(close, "Close Settings (Esc)");
        close.Click += (_, _) => back();
        Grid.SetRow(close, 1);
        root.Children.Add(close);

        var content = new StackPanel { Spacing = 22 };
        content.Children.Add(ResultsPanel());
        content.Children.Add(LookPanel());
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 22 };
        var sandbox = SandboxPanel();
        var ai = AiPanel();
        Grid.SetColumn(sandbox, 0);
        Grid.SetColumn(ai, 1);
        row.Children.Add(sandbox);
        row.Children.Add(ai);
        content.Children.Add(row);
        content.Children.Add(AboutPanel());
        root.Children.Add(new ScrollViewer { Content = content, Padding = new Thickness(0, 0, 12, 12) });
        Content = root;
    }

    private Control Section(string title, Control body) => new StackPanel
    {
        Spacing = 8,
        Children = { new TextBlock { Text = title.ToUpperInvariant(), Classes = { "label" } }, body },
    };

    private Control Radio<T>(string group, (T Value, string Label, string Example)[] options, T current, Action<T> set)
    {
        var stack = new StackPanel { Spacing = 6 };
        foreach (var (value, label, example) in options)
        {
            var rb = new RadioButton
            {
                GroupName = group,
                IsChecked = EqualityComparer<T>.Default.Equals(value, current),
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = label, FontSize = 15, TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Text = example, FontSize = 13, Classes = { "math", "muted" } },
                    },
                },
            };
            rb.IsCheckedChanged += (_, _) => { if (rb.IsChecked == true) set(value); };
            stack.Children.Add(rb);
        }
        var b = new Border { BorderThickness = new Thickness(3), Padding = new Thickness(12), Child = stack };
        b.Bind(Border.BorderBrushProperty, b.GetResourceObservable("Calcy.Line"));
        return b;
    }

    private Control Segmented<T>(T[] values, Func<T, string> label, T current, Action<T> set)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var toggles = new List<PixelToggle>();
        foreach (var v in values)
        {
            var t = new PixelToggle { Content = label(v), IsChecked = EqualityComparer<T>.Default.Equals(v, current), MinWidth = 72, Margin = new Thickness(0, 0, -2, 0) };
            var value = v;
            t.Click += (_, _) =>
            {
                foreach (var o in toggles) o.IsChecked = o == t;
                set(value);
            };
            toggles.Add(t);
            panel.Children.Add(t);
        }
        return panel;
    }

    private Control ResultsPanel()
    {
        var s = _s.Settings;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), ColumnSpacing = 22, RowSpacing = 18, Margin = new Thickness(18) };
        void Put(Control c, int r, int col) { Grid.SetRow(c, r); Grid.SetColumn(c, col); grid.Children.Add(c); }
        Put(Section("Decimals in sums show as", Radio("results", [
            (ResultPreference.ExactFirst, "Fractions first (Casio style, default)", "0.1 + 0.2 = 3/10  ≈ 0.3"),
            (ResultPreference.DecimalFirst, "Decimals first (Desmos style)", "0.1 + 0.2 = 0.3  (S⇔D gives 3/10)")],
            s.Results, v => { s.Results = v; _s.NotifySettingsChanged(); })), 0, 0);
        Put(Section("1/2x means", Radio("implicit", [
            (ImplicitMultiplication.BindsTighter, "1/(2x): implied × binds tighter (Casio, default)", "1/2x = 1/(2x)"),
            (ImplicitMultiplication.LeftToRight, "x/2: left to right (TI, Desmos)", "1/2x = (1/2)·x")],
            s.Implicit, v => { s.Implicit = v; _s.NotifySettingsChanged(); })), 0, 1);
        Put(Section("Angle unit", Segmented([AngleMode.Radian, AngleMode.Degree, AngleMode.Gradian],
            a => a switch { AngleMode.Degree => "DEG", AngleMode.Gradian => "GRAD", _ => "RAD" }, s.Angle,
            v => { s.Angle = v; _s.NotifySettingsChanged(); })), 1, 0);
        var digits = new NumericUpDown { Minimum = 1, Maximum = 30, Value = s.Digits, Width = 130, FormatString = "0" };
        digits.ValueChanged += (_, e) => { if (e.NewValue is { } v) { s.Digits = (int)v; _s.NotifySettingsChanged(); } };
        var format = new ComboBox { ItemsSource = new[] { "Float", "Fix", "Sci", "Eng" }, SelectedIndex = (int)s.Format, Width = 120 };
        format.SelectionChanged += (_, _) => { s.Format = (NumberFormatMode)Math.Max(0, format.SelectedIndex); _s.NotifySettingsChanged(); };
        Put(Section("Number format", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { format, new TextBlock { Text = "digits", VerticalAlignment = VerticalAlignment.Center }, digits },
        }), 1, 1);
        var complex = new ComboBox
        {
            ItemsSource = new[] { "Auto: cube roots stay real, √-4 = 2i", "Real only: show an error", "Complex: principal branch" },
            SelectedIndex = (int)s.Complex,
            MinWidth = 320,
        };
        complex.SelectionChanged += (_, _) => { s.Complex = (ComplexMode)Math.Max(0, complex.SelectedIndex); _s.NotifySettingsChanged(); };
        Put(Section("Complex results", complex), 2, 0);
        Put(Section("Fixed rules", new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = "log is base 10, ln is natural", FontSize = 15 },
                new TextBlock { Text = "ax means a·x, and a = 5 defines a", FontSize = 15 },
            },
        }), 2, 1);
        return new PixelPanel { Header = "Results and input", HeaderBrush = Brush("Calcy.Sel"), Content = grid };
    }

    private Control LookPanel()
    {
        RebuildSchemes();
        _s.Theme.SchemeChanged += (_, _) => RebuildSchemes();
        var dither = new CheckBox { Content = "Dither background", IsChecked = _s.Prefs.Dither };
        dither.IsCheckedChanged += (_, _) => { _s.Prefs.Dither = dither.IsChecked == true; _s.Prefs.Save(); _s.ShowStatus("Restart Calcy to apply the background change"); };
        var sounds = new CheckBox { Content = "8-bit key sounds", IsChecked = _s.Prefs.KeySounds };
        sounds.IsCheckedChanged += (_, _) => { _s.Prefs.KeySounds = sounds.IsChecked == true; _s.Prefs.Save(); };
        var body = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 14,
            Children =
            {
                Section("Colour scheme", _schemes),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, Children = { dither, sounds } },
            },
        };
        return new PixelPanel { Header = "Look", Content = body };
    }

    private void RebuildSchemes()
    {
        _schemes.Children.Clear();
        foreach (var scheme in ColorSchemes.All)
        {
            var preview = new Border
            {
                Height = 40,
                BorderThickness = new Thickness(2),
                Background = new SolidColorBrush(scheme["Ground"]),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 4,
                    Children =
                    {
                        new Border { Width = 46, Height = 18, Background = new SolidColorBrush(scheme["Paper"]), BorderBrush = new SolidColorBrush(scheme["Line"]), BorderThickness = new Thickness(2) },
                        new Border { Width = 16, Height = 18, Background = new SolidColorBrush(scheme["Sel"]), BorderBrush = new SolidColorBrush(scheme["Line"]), BorderThickness = new Thickness(2) },
                        new Border { Width = 16, Height = 18, Background = new SolidColorBrush(scheme["Primary"]), BorderBrush = new SolidColorBrush(scheme["Line"]), BorderThickness = new Thickness(2) },
                    },
                },
            };
            preview.Bind(Border.BorderBrushProperty, preview.GetResourceObservable("Calcy.Line"));
            var tile = new PixelToggle
            {
                Width = 150,
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(6),
                IsChecked = scheme.Id == _s.Theme.Current.Id,
                Content = new StackPanel { Spacing = 6, Children = { preview, new TextBlock { Text = scheme.Name, FontSize = 13, Classes = { "label" } } } },
            };
            var id = scheme.Id;
            tile.Click += (_, _) => _s.SetScheme(id);
            _schemes.Children.Add(tile);
        }
    }

    private Control SandboxPanel() => new PixelPanel
    {
        Header = "Code sandbox",
        HeaderBrush = Brush("Calcy.Ok"),
        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Python runs inside Calcy with no access to files, the network or the system.", Classes = { "body" } },
                new TextBlock { Text = "Programs stop after 10 seconds or 50 million steps. Press Stop or Esc any time.", Classes = { "body", "muted" } },
            },
        },
    };

    private Control AiPanel()
    {
        var mcp = new CheckBox { Content = "Let AI apps use Calcy (MCP)", IsChecked = _s.Prefs.McpEnabled };
        mcp.IsCheckedChanged += (_, _) => { _s.Prefs.McpEnabled = mcp.IsChecked == true; _s.Prefs.Save(); };
        return new PixelPanel
        {
            Header = "AI connections",
            HeaderBrush = Brush("Calcy.Mem"),
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 8,
                Children =
                {
                    mcp,
                    new TextBlock { Text = "Off by default. AI apps connect to the Calcy command line server: calcy-cli mcp. Each tool runs in a sandbox and never sees your files.", Classes = { "body", "muted" } },
                },
            },
        };
    }

    private Control AboutPanel() => new PixelPanel
    {
        Header = "About Calcy",
        HeaderBrush = Brush("Calcy.Key"),
        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = AppInfo.Description + ", an 8-bit graphing calculator for school and science.", Classes = { "body" }, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = AppInfo.License + ". " + AppInfo.Copyright + ".", Classes = { "body", "muted" }, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "All maths is implemented in Calcy itself (exact fractions, radicals, algebra, statistics, numerics). Runs on Windows, macOS and Linux.", Classes = { "body", "muted" } },
                new TextBlock { Text = $"Settings are stored in {Preferences.DefaultPath()}", Classes = { "body", "muted" }, TextWrapping = TextWrapping.Wrap },
                ReportButton(),
            },
        },
    };

    private Control ReportButton()
    {
        var report = new PixelButton { Content = "REPORT A BUG", Classes = { "sel" }, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        ToolTip.SetTip(report, "Opens a bug report form on GitHub with the version and your system filled in. You review it before anything is sent.");
        report.Click += async (_, _) =>
        {
            bool opened = await BugReport.OpenAsync(this, "settings");
            _s.ShowStatus(opened ? "Opened the bug report form in your browser" : "Could not open a browser; the form's link is on the clipboard");
        };
        return report;
    }

    private IBrush? Brush(string key) => Application.Current?.FindResource(key) as IBrush;
}

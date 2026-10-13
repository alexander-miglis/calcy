using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Calcy.Core;
using Calcy.UI.Controls;
using Calcy.UI.Services;
using Calcy.UI.Theming;

namespace Calcy.App;

/// <summary>The top bar: logo, mode dropdown, quick setting chips, colour scheme menu, settings and help.</summary>
internal sealed class TopBar : Border
{
    private readonly AppServices _services;
    private readonly PixelText _modeLabel;
    private readonly PixelButton _modeButton;
    private readonly PixelButton _angleChip;
    private readonly PixelButton _resultChip;
    private readonly PixelButton _formatChip;
    private readonly Flyout _modeFlyout;
    private readonly Flyout _colourFlyout;
    private string _currentMode = "advanced";

    public event EventHandler<string>? ModeSelected;
    public event EventHandler? SettingsRequested;
    public event EventHandler? HelpRequested;

    public TopBar(AppServices services)
    {
        _services = services;
        this.Bind(BackgroundProperty, this.GetResourceObservable("Calcy.Bar"));
        this.Bind(BorderBrushProperty, this.GetResourceObservable("Calcy.BarAccent"));
        BorderThickness = new Thickness(0, 0, 0, 4);
        Padding = new Thickness(14, 8);
        MinHeight = 64;

        var logo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        var logoIcon = new LogoIcon { Width = 32, Height = 32 };
        var logoText = new PixelText { Text = "CALCY", PixelSize = 4, VerticalAlignment = VerticalAlignment.Center };
        logoText.Bind(PixelText.ForegroundProperty, logoText.GetResourceObservable("Calcy.BarInk"));
        logoText.Bind(PixelText.ShadowBrushProperty, logoText.GetResourceObservable("Calcy.BarAccent"));
        logo.Children.Add(logoIcon);
        logo.Children.Add(logoText);
        var logoButton = new Button
        {
            Content = logo,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        ToolTip.SetTip(logoButton, "Calcy: back to Advanced");
        logoButton.Click += (_, _) => ModeSelected?.Invoke(this, "advanced");

        _modeLabel = new PixelText { PixelSize = 2, VerticalAlignment = VerticalAlignment.Center };
        _modeLabel.Bind(PixelText.ForegroundProperty, _modeLabel.GetResourceObservable("Calcy.Ink"));
        var chevron = new PixelIcon { Kind = "chevron", Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        chevron.Bind(PixelIcon.ForegroundProperty, chevron.GetResourceObservable("Calcy.Ink"));
        _modeButton = new PixelButton
        {
            Classes = { "sel" },
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { _modeLabel, chevron } },
            Padding = new Thickness(14, 8),
            MinHeight = 44,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _modeButton.Bind(BorderBrushProperty, _modeButton.GetResourceObservable("Calcy.BarInk"));
        _modeFlyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft, ShowMode = FlyoutShowMode.Standard };
        _modeButton.Flyout = _modeFlyout;
        _modeButton.Click += (_, _) => _modeFlyout.Content = BuildModeMenu();
        ToolTip.SetTip(_modeButton, "Pick a mode (Ctrl+1 to Ctrl+9)");

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(logoButton);
        left.Children.Add(_modeButton);

        _angleChip = Chip("head");
        _angleChip.Click += (_, _) =>
        {
            services.Settings.Angle = services.Settings.Angle switch
            {
                AngleMode.Radian => AngleMode.Degree,
                AngleMode.Degree => AngleMode.Gradian,
                _ => AngleMode.Radian,
            };
            services.NotifySettingsChanged();
        };
        ToolTip.SetTip(_angleChip, "Angle unit: click to switch between radians, degrees and gradians");
        _resultChip = Chip("mem");
        _resultChip.Click += (_, _) =>
        {
            services.Settings.Results = services.Settings.Results == ResultPreference.ExactFirst ? ResultPreference.DecimalFirst : ResultPreference.ExactFirst;
            services.NotifySettingsChanged();
        };
        ToolTip.SetTip(_resultChip, "Results: fractions first (Casio style) or decimals first");
        _formatChip = Chip(null);
        _formatChip.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        ToolTip.SetTip(_formatChip, "Number format (open Settings)");

        _colourFlyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        var colours = new PixelButton { Padding = new Thickness(10, 4), MinHeight = 44, VerticalAlignment = VerticalAlignment.Center };
        colours.Bind(BackgroundProperty, colours.GetResourceObservable("Calcy.Bar"));
        colours.Bind(BorderBrushProperty, colours.GetResourceObservable("Calcy.BarInk"));
        colours.Classes.Add("flat");
        var colourLabel = new PixelText { Text = "COLOURS", PixelSize = 2, VerticalAlignment = VerticalAlignment.Center };
        colourLabel.Bind(PixelText.ForegroundProperty, colourLabel.GetResourceObservable("Calcy.BarInk"));
        colours.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new SchemeSwatchIcon { Width = 20, Height = 20 }, colourLabel } };
        colours.Flyout = _colourFlyout;
        colours.Click += (_, _) => _colourFlyout.Content = BuildColourMenu();
        ToolTip.SetTip(colours, "Colour scheme (applies everywhere)");

        var settings = IconButton("settings", "Settings (Ctrl+,)");
        settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        var help = IconButton("help", "Help and function catalog (F1)");
        help.Click += (_, _) => HelpRequested?.Invoke(this, EventArgs.Empty);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(_angleChip);
        right.Children.Add(_resultChip);
        right.Children.Add(_formatChip);
        right.Children.Add(colours);
        right.Children.Add(settings);
        right.Children.Add(help);

        var dock = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        dock.Children.Add(left);
        Child = dock;

        services.SettingsChanged += (_, _) => RefreshChips();
        RefreshChips();
        SetMode("advanced");
    }

    private PixelButton Chip(string? cls)
    {
        var b = new PixelButton { Classes = { "small", "flat" }, VerticalAlignment = VerticalAlignment.Center, MinWidth = 54 };
        if (cls is not null) b.Classes.Add(cls);
        b.Bind(BorderBrushProperty, b.GetResourceObservable("Calcy.BarInk"));
        return b;
    }

    private PixelButton IconButton(string icon, string tip)
    {
        var glyph = new PixelIcon { Kind = icon, Width = 22, Height = 22 };
        glyph.Bind(PixelIcon.ForegroundProperty, glyph.GetResourceObservable("Calcy.BarInk"));
        var b = new PixelButton { Content = glyph, Width = 44, Height = 44, Padding = new Thickness(0), Classes = { "flat" }, VerticalAlignment = VerticalAlignment.Center };
        b.Bind(BackgroundProperty, b.GetResourceObservable("Calcy.Bar"));
        b.Bind(BorderBrushProperty, b.GetResourceObservable("Calcy.BarInk"));
        ToolTip.SetTip(b, tip);
        Avalonia.Automation.AutomationProperties.SetName(b, tip);
        return b;
    }

    private void RefreshChips()
    {
        var s = _services.Settings;
        _angleChip.Content = s.Angle switch { AngleMode.Degree => "DEG", AngleMode.Gradian => "GRAD", _ => "RAD" };
        _resultChip.Content = s.Results == ResultPreference.ExactFirst ? "FRAC" : "DEC";
        _formatChip.Content = $"{s.Format.ToString().ToUpperInvariant()} {s.Digits}";
    }

    public void SetMode(string id)
    {
        _currentMode = id;
        var mode = _services.Modes.FirstOrDefault(m => m.Id == id);
        _modeLabel.Text = "MODE: " + (mode?.Title ?? (id == "settings" ? "Settings" : id)).ToUpperInvariant();
    }

    private Control BuildModeMenu()
    {
        var stack = new StackPanel { Width = 440 };
        var header = new Border { Padding = new Thickness(12, 8), BorderThickness = new Thickness(0, 0, 0, 3) };
        header.Bind(BackgroundProperty, header.GetResourceObservable("Calcy.Head"));
        header.Bind(BorderBrushProperty, header.GetResourceObservable("Calcy.Line"));
        var ht = new PixelText { Text = "PICK A MODE. CARDS AND VARIABLES COME WITH YOU.", PixelSize = 1.5 };
        ht.Bind(PixelText.ForegroundProperty, ht.GetResourceObservable("Calcy.Ink"));
        header.Child = ht;
        stack.Children.Add(header);
        foreach (var m in _services.Modes)
        {
            var dot = new Border { Width = 14, Height = 14, BorderThickness = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
            dot.Bind(BackgroundProperty, dot.GetResourceObservable("Calcy." + m.AccentToken));
            dot.Bind(BorderBrushProperty, dot.GetResourceObservable("Calcy.Line"));
            var title = new TextBlock { Text = m.Title, FontSize = 17, FontWeight = FontWeight.SemiBold };
            title.Bind(TextBlock.FontFamilyProperty, title.GetResourceObservable("Calcy.Font.Body"));
            var desc = new TextBlock { Text = m.Description, FontSize = 12, Classes = { "muted" } };
            desc.Bind(TextBlock.FontFamilyProperty, desc.GetResourceObservable("Calcy.Font.Body"));
            var shortcut = new TextBlock { Text = m.Shortcut, FontSize = 12, Classes = { "muted", "math" }, VerticalAlignment = VerticalAlignment.Center };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("26,*,Auto") };
            Grid.SetColumn(dot, 0);
            var texts = new StackPanel { Spacing = 2, Children = { title, desc } };
            Grid.SetColumn(texts, 1);
            Grid.SetColumn(shortcut, 2);
            grid.Children.Add(dot);
            grid.Children.Add(texts);
            grid.Children.Add(shortcut);
            var item = new Button
            {
                Content = grid,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(12, 8),
                BorderThickness = new Thickness(0, 0, 0, 2),
                CornerRadius = new CornerRadius(0),
            };
            item.Bind(BackgroundProperty, item.GetResourceObservable(m.Id == _currentMode ? "Calcy.Sel" : "Calcy.Paper"));
            item.Bind(BorderBrushProperty, item.GetResourceObservable("Calcy.Key"));
            item.Bind(TemplatedControl.ForegroundProperty, item.GetResourceObservable("Calcy.Ink"));
            Avalonia.Automation.AutomationProperties.SetName(item, $"{m.Title}: {m.Description}");
            var id = m.Id;
            item.Click += (_, _) =>
            {
                _modeFlyout.Hide();
                ModeSelected?.Invoke(this, id);
            };
            stack.Children.Add(item);
        }
        return Framed(stack);
    }

    private Control BuildColourMenu()
    {
        var stack = new StackPanel { Width = 360 };
        var header = new Border { Padding = new Thickness(12, 8), BorderThickness = new Thickness(0, 0, 0, 3) };
        header.Bind(BackgroundProperty, header.GetResourceObservable("Calcy.Head"));
        header.Bind(BorderBrushProperty, header.GetResourceObservable("Calcy.Line"));
        var ht = new PixelText { Text = "COLOUR SCHEME", PixelSize = 1.5 };
        ht.Bind(PixelText.ForegroundProperty, ht.GetResourceObservable("Calcy.Ink"));
        header.Child = ht;
        stack.Children.Add(header);
        foreach (var scheme in ColorSchemes.All)
        {
            var swatch = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var token in new[] { "Ground", "Paper", "Head", "Sel", "Primary", "G1" })
                swatch.Children.Add(new Border { Width = 16, Height = 22, Background = new SolidColorBrush(scheme[token]) });
            var frame = new Border { Child = swatch, BorderThickness = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
            frame.Bind(BorderBrushProperty, frame.GetResourceObservable("Calcy.Line"));
            var name = new TextBlock { Text = scheme.Name, FontSize = 16, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            name.Bind(TextBlock.FontFamilyProperty, name.GetResourceObservable("Calcy.Font.Body"));
            var mark = new TextBlock { Text = scheme.Id == _services.Theme.Current.Id ? "SELECTED" : "", FontSize = 10, Classes = { "label" }, VerticalAlignment = VerticalAlignment.Center };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
            Grid.SetColumn(frame, 0);
            Grid.SetColumn(name, 1);
            Grid.SetColumn(mark, 2);
            grid.Children.Add(frame);
            grid.Children.Add(name);
            grid.Children.Add(mark);
            var item = new Button
            {
                Content = grid,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(12, 8),
                BorderThickness = new Thickness(0, 0, 0, 2),
                CornerRadius = new CornerRadius(0),
            };
            item.Bind(BackgroundProperty, item.GetResourceObservable(scheme.Id == _services.Theme.Current.Id ? "Calcy.Sel" : "Calcy.Paper"));
            item.Bind(BorderBrushProperty, item.GetResourceObservable("Calcy.Key"));
            item.Bind(TemplatedControl.ForegroundProperty, item.GetResourceObservable("Calcy.Ink"));
            var id = scheme.Id;
            item.Click += (_, _) =>
            {
                _colourFlyout.Hide();
                _services.SetScheme(id);
            };
            stack.Children.Add(item);
        }
        return Framed(stack);
    }

    private static Control Framed(Control content)
    {
        var b = new Border { Child = content, BorderThickness = new Thickness(4) };
        b.Bind(BackgroundProperty, b.GetResourceObservable("Calcy.Paper"));
        b.Bind(BorderBrushProperty, b.GetResourceObservable("Calcy.Line"));
        b.Bind(Border.BoxShadowProperty, b.GetResourceObservable("Calcy.ShadowLarge"));
        return b;
    }

}

/// <summary>The calculator logo icon: body in the primary colour, screen in the LCD colour.</summary>
internal sealed class LogoIcon : Control
{
    public override void Render(DrawingContext context)
    {
        double s = Math.Min(Bounds.Width, Bounds.Height) / 16.0;
        var body = this.FindResource("Calcy.Primary") as IBrush ?? Brushes.Orange;
        var lcd = this.FindResource("Calcy.Lcd") as IBrush ?? Brushes.YellowGreen;
        using (context.PushTransform(Matrix.CreateScale(s, s)))
        {
            context.DrawGeometry(body, null, PixelIcon.GetGeometry("calc")!);
            context.FillRectangle(lcd, new Rect(4, 3, 8, 3));
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        if (Application.Current is { } app) app.ResourcesChanged += (_, _) => InvalidateVisual();
    }
}

/// <summary>Four squares in the current scheme's accent colours.</summary>
internal sealed class SchemeSwatchIcon : Control
{
    public override void Render(DrawingContext context)
    {
        double s = Math.Min(Bounds.Width, Bounds.Height) / 16.0;
        string[] keys = ["Calcy.Sel", "Calcy.Head", "Calcy.Primary", "Calcy.Mem"];
        Rect[] rects = [new(2, 2, 5, 5), new(9, 2, 5, 5), new(2, 9, 5, 5), new(9, 9, 5, 5)];
        using (context.PushTransform(Matrix.CreateScale(s, s)))
        {
            for (int i = 0; i < 4; i++)
                context.FillRectangle(this.FindResource(keys[i]) as IBrush ?? Brushes.Gray, rects[i]);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Application.Current is { } app) app.ResourcesChanged += (_, _) => InvalidateVisual();
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Calcy.UI.Controls;
using Calcy.UI.Services;

namespace Calcy.App;

/// <summary>The main window: top bar, the current mode's screen and a status line.</summary>
public sealed class MainWindow : Window
{
    private readonly AppServices _services;
    private readonly TopBar _top;
    private readonly ContentControl _host = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly TextBlock _status = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<string, Control> _views = [];
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private string _current = "";

    public MainWindow(AppServices services)
    {
        _services = services;
        Title = "Calcy";
        try
        {
            Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://Calcy/Assets/calcy.png")));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // A missing icon never stops the app.
        }
        Width = services.Prefs.WindowWidth;
        Height = services.Prefs.WindowHeight;
        MinWidth = 900;
        MinHeight = 600;
        this.Bind(BackgroundProperty, this.GetResourceObservable("Calcy.Ground"));
        this.Bind(FontFamilyProperty, this.GetResourceObservable("Calcy.Font.Body"));

        _top = new TopBar(services);
        _top.ModeSelected += (_, id) => ShowMode(id);
        _top.SettingsRequested += (_, _) => ShowMode("settings");
        _top.HelpRequested += (_, _) => ShowMode("help");

        var dither = new DitherBackground();
        dither.Bind(DitherBackground.BackgroundProperty, dither.GetResourceObservable("Calcy.Ground"));
        dither.Bind(DitherBackground.Background2Property, dither.GetResourceObservable("Calcy.Ground2"));
        dither.IsVisible = services.Prefs.Dither;

        _status.Bind(TextBlock.ForegroundProperty, _status.GetResourceObservable("Calcy.OnGround"));
        _status.Bind(TextBlock.FontFamilyProperty, _status.GetResourceObservable("Calcy.Font.Body"));
        var statusBar = new Border { Padding = new Thickness(16, 4), Child = _status, MinHeight = 26 };
        _statusTimer.Tick += (_, _) => { _status.Text = DefaultStatus; _statusTimer.Stop(); };
        services.StatusMessage += (_, msg) =>
        {
            _status.Text = msg;
            _statusTimer.Stop();
            _statusTimer.Start();
        };
        _status.Text = DefaultStatus;

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        Grid.SetRowSpan(dither, 3);
        grid.Children.Add(dither);
        Grid.SetRow(_top, 0);
        grid.Children.Add(_top);
        Grid.SetRow(_host, 1);
        grid.Children.Add(_host);
        Grid.SetRow(statusBar, 2);
        grid.Children.Add(statusBar);
        Content = grid;

        services.NavigationRequested += (_, req) =>
        {
            ShowMode(req.ModeId);
            if (req.Payload is not null && _views.TryGetValue(req.ModeId, out var view) && view is IReceivesPayload receiver)
                receiver.Receive(req.Payload);
        };
        KeyDown += OnKeyDown;
        Closing += (_, _) =>
        {
            services.Prefs.WindowWidth = Width;
            services.Prefs.WindowHeight = Height;
            services.Prefs.LastMode = _current;
            services.Prefs.Save();
        };
        ShowMode(string.IsNullOrEmpty(services.Prefs.LastMode) ? "advanced" : services.Prefs.LastMode);
    }

    private const string DefaultStatus = "Ctrl+1 to Ctrl+9: switch mode · Tab: next slot · Enter: calculate · F1: help";

    public void ShowMode(string id)
    {
        if (!_views.TryGetValue(id, out var view))
        {
            view = id switch
            {
                "settings" => new SettingsView(_services, () => ShowMode(_previous)),
                "help" => new HelpView(_services, () => ShowMode(_previous), _previous),
                _ => (_services.Modes.FirstOrDefault(m => m.Id == id) ?? _services.Modes[0]).CreateView(_services),
            };
            _views[id] = view;
        }
        if (id is "settings" or "help")
        {
            if (_current.Length > 0 && _current is not ("settings" or "help")) _previous = _current;
        }
        _current = id;
        _host.Content = view;
        _top.SetMode(id);
        if (id is not ("settings" or "help")) _services.Prefs.LastMode = id;
        Dispatcher.UIThread.Post(() => (view as IFocusFirst)?.FocusFirst(), DispatcherPriority.Background);
    }

    private string _previous = "advanced";

    /// <summary>For screenshots and tests: send text to the current mode's input.</summary>
    public void SendInput(string text)
    {
        if (!_views.TryGetValue(_current, out var view) || view is not IReceivesPayload r) return;
        r.Receive(new InputRequest(text, Evaluate: true));
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (view is IBusy { IsBusy: true } && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(15);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            int index = e.Key switch
            {
                Key.D1 or Key.NumPad1 => 0, Key.D2 or Key.NumPad2 => 1, Key.D3 or Key.NumPad3 => 2, Key.D4 or Key.NumPad4 => 3,
                Key.D5 or Key.NumPad5 => 4, Key.D6 or Key.NumPad6 => 5, Key.D7 or Key.NumPad7 => 6, Key.D8 or Key.NumPad8 => 7,
                Key.D9 or Key.NumPad9 => 8, Key.D0 or Key.NumPad0 => 9,
                _ => -1,
            };
            if (index >= 0 && index < _services.Modes.Count && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                ShowMode(_services.Modes[index].Id);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.E) { ShowMode("code"); e.Handled = true; return; }
            if (e.Key == Key.N && e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { ShowMode("notebook"); e.Handled = true; return; }
            if (e.Key == Key.OemComma) { ShowMode("settings"); e.Handled = true; return; }
        }
        if (e.Key == Key.F1) { ShowMode("help"); e.Handled = true; }
    }
}


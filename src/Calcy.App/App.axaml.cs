using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Calcy.Core;
using Calcy.UI.Services;
using Calcy.UI.Theming;

namespace Calcy.App;

public partial class App : Application
{
    public AppServices? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Services = CreateServices(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow(Services!);
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => Services!.Prefs.Save();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static AppServices CreateServices(Application app)
    {
        var prefs = Preferences.Load();
        var theme = new ThemeManager(app);
        ThemeManager.WriteFonts(app.Resources, FontLoader.Pixel, FontLoader.Label, FontLoader.Math, FontLoader.Lcd);
        theme.Apply(prefs.SchemeId);
        var session = new CalcSession(new CalcSettings(), EngineFactory.CreateRegistry());
        var services = new AppServices(session, theme, prefs);
        services.Modes = ModeCatalog.Create();
        return services;
    }
}

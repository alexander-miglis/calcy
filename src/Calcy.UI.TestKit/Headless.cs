using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Calcy.Core;
using Calcy.UI.Controls;
using Calcy.UI.Services;
using Calcy.UI.Theming;

namespace Calcy.UI.TestKit;

/// <summary>A minimal application with the Calcy theme, for headless tests.</summary>
public sealed class TestApp : Application
{
    /// <summary>Picked up by the headless session: real Skia rendering so frames can be captured and saved.</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new CalcyTheme());
    }
}

/// <summary>
/// Renders Calcy screens without a display (Windows, macOS, Linux), so tests can interact with views and save PNGs.
/// All calls must run on the thread that called <see cref="EnsureStarted"/>; use <see cref="Run"/> from tests.
/// </summary>
public static class Headless
{
    private static readonly object Gate = new();
    private static HeadlessUnitTestSession? _session;

    /// <summary>Runs an action on the headless UI thread and waits for it.</summary>
    public static void Run(Action action)
    {
        EnsureStarted();
        _session!.Dispatch(action, CancellationToken.None).GetAwaiter().GetResult();
    }

    public static T Run<T>(Func<T> func)
    {
        EnsureStarted();
        return _session!.Dispatch(func, CancellationToken.None).GetAwaiter().GetResult();
    }

    public static void EnsureStarted()
    {
        lock (Gate)
        {
            _session ??= HeadlessUnitTestSession.StartNew(typeof(TestApp));
        }
    }

    /// <summary>Creates services like the app does (full engine, theme, in-memory preferences).</summary>
    public static AppServices CreateServices(string scheme = "nes")
    {
        var app = Application.Current!;
        var theme = new ThemeManager(app);
        ThemeManager.WriteFonts(app.Resources);
        theme.Apply(scheme);
        var prefs = new Preferences();
        var session = Calcy.Engine.CalcyEngine.CreateSession(new CalcSettings());
        return new AppServices(session, theme, prefs);
    }

    /// <summary>Hosts a view in a window like the app (dithered background), lays it out and renders a frame.</summary>
    public static Window Show(Control view, double width = 1360, double height = 820)
    {
        var dither = new DitherBackground();
        dither.Bind(DitherBackground.BackgroundProperty, dither.GetResourceObservable("Calcy.Ground"));
        dither.Bind(DitherBackground.Background2Property, dither.GetResourceObservable("Calcy.Ground2"));
        var grid = new Grid();
        grid.Children.Add(dither);
        grid.Children.Add(view);
        var w = new Window { Width = width, Height = height, Content = grid };
        w.Show();
        Pump();
        return w;
    }

    /// <summary>Processes pending UI work and renders.</summary>
    public static void Pump(int frames = 3)
    {
        for (int i = 0; i < frames; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Pumps the UI until a view that runs work in the background (IBusy) is idle, or the timeout passes.</summary>
    public static bool WaitForIdle(object view, TimeSpan? timeout = null)
    {
        var until = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (DateTime.UtcNow < until)
        {
            Pump(1);
            if (view is not IBusy busy || !busy.IsBusy) return true;
            Thread.Sleep(10);
        }
        return false;
    }

    /// <summary>Saves the window's current frame as a PNG and returns the path.</summary>
    public static string Save(Window w, string fileName)
    {
        Pump();
        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        using WriteableBitmap? frame = w.CaptureRenderedFrame();
        frame?.Save(path, new PngBitmapEncoderOptions());
        return path;
    }
}

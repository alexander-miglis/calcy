using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;

namespace Calcy.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] is "--version" or "-v")
        {
            Console.WriteLine(AppInfo.Description);

            return 0;
        }
        if (args.Length > 0 && args[0] == "--screenshot") return Screenshot(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    /// <summary>
    /// Renders a screen without showing a window, for checks and documentation:
    /// Calcy --screenshot &lt;mode&gt; &lt;file.png&gt; [--scheme id] [--size 1360x880] [--input "text"]...
    /// </summary>
    private static int Screenshot(string[] args)
    {
        string mode = args.Length > 1 ? args[1] : "advanced";
        string file = args.Length > 2 ? args[2] : "calcy.png";
        string? scheme = null;
        double w = 1360, h = 880;
        var inputs = new List<string>();
        for (int i = 3; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scheme" when i + 1 < args.Length: scheme = args[++i]; break;
                case "--size" when i + 1 < args.Length:
                    var parts = args[++i].Split('x');
                    w = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
                    h = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--input" when i + 1 < args.Length: inputs.Add(args[++i]); break;
            }
        }
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        var app = (App)Application.Current!;
        var services = app.Services!;
        services.Prefs.Disable();
        if (scheme is not null) services.Theme.Apply(scheme);
        var window = new MainWindow(services) { Width = w, Height = h };
        window.Show();
        window.ShowMode(mode);
        Dispatcher.UIThread.RunJobs();
        foreach (var input in inputs) window.SendInput(input);
        for (int k = 0; k < 5; k++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        var frame = window.CaptureRenderedFrame();
        frame?.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        Console.WriteLine(frame is null ? "no frame" : $"saved {file}");
        return frame is null ? 1 : 0;
    }
}

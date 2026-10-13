using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(Calcy.UI.Math.Tests.TestAppBuilder))]

namespace Calcy.UI.Math.Tests;

/// <summary>Minimal application for headless rendering tests.</summary>
public sealed class TestApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}

/// <summary>Entry point used by the headless test session.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>Runs code on the headless UI thread and renders controls to PNG files.</summary>
public static class UI
{
    private static HeadlessUnitTestSession Session => HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UI).Assembly);

    /// <summary>Folder where rendered PNGs are written (under the test output folder).</summary>
    public static string OutputFolder { get; } = Path.Combine(AppContext.BaseDirectory, "MathRenders");

    static UI()
    {
        MathEditor.AnimationsEnabled = false;
        Directory.CreateDirectory(OutputFolder);
    }

    public static void Run(Action action) => Session.Dispatch(action, CancellationToken.None).GetAwaiter().GetResult();

    public static T Run<T>(Func<T> func) => Session.Dispatch(func, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Shows a control in a window sized to its content, captures a frame and optionally saves it.</summary>
    public static WriteableBitmap Capture(Control control, string? fileName = null, double width = double.NaN, Action<Window>? beforeCapture = null)
    {
        var window = new Window
        {
            SizeToContent = double.IsNaN(width) ? SizeToContent.WidthAndHeight : SizeToContent.Height,
            Background = Avalonia.Media.Brushes.White,
            Content = new Border { Padding = new Thickness(8), Child = control },
        };
        if (!double.IsNaN(width)) window.Width = width;
        window.Show();
        beforeCapture?.Invoke(window);
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame was rendered");
        if (fileName is not null) frame.Save(Path.Combine(OutputFolder, Sanitize(fileName) + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        window.Close();
        return frame;
    }

    public static string Sanitize(string name)
    {
        var bad = Path.GetInvalidFileNameChars().Concat(['\'', ' ', '^', '*', '|', '<', '>', '"', '?']).ToHashSet();
        var chars = name.Select(c => bad.Contains(c) ? '_' : c).ToArray();
        var s = new string(chars);
        return s.Length > 60 ? s[..60] : s;
    }

    /// <summary>Counts pixels that differ clearly from white.</summary>
    public static unsafe int CountInkPixels(WriteableBitmap bmp, Func<byte, byte, byte, bool>? predicate = null)
    {
        using var fb = bmp.Lock();
        bool rgba = fb.Format == Avalonia.Platform.PixelFormat.Rgba8888;
        int count = 0;
        for (int y = 0; y < fb.Size.Height; y++)
        {
            var row = (byte*)fb.Address + y * fb.RowBytes;
            for (int x = 0; x < fb.Size.Width; x++)
            {
                byte c0 = row[x * 4], g = row[x * 4 + 1], c2 = row[x * 4 + 2];
                byte r = rgba ? c0 : c2, b = rgba ? c2 : c0;
                bool hit = predicate?.Invoke(r, g, b) ?? (r < 160 && g < 160 && b < 160);
                if (hit) count++;
            }
        }
        return count;
    }
}

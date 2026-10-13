using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Calcy.UI.TestKit;

namespace Calcy.UI.Graph.Tests;

/// <summary>
/// Starts the shared headless session with Skia so frames contain pixels (the TestKit app falls back to
/// headless drawing otherwise), and offers small helpers for waiting and reading pixels.
/// </summary>
internal static class TestSupport
{
    private static readonly object Gate = new();
    private static bool _done;

    public static void EnsureSkia()
    {
        lock (Gate)
        {
            if (_done) return;
            _done = true;
            var field = typeof(Headless).GetField("_session", BindingFlags.NonPublic | BindingFlags.Static);
            if (field is null || field.GetValue(null) is not null) return;
            field.SetValue(null, HeadlessUnitTestSession.StartNew(typeof(SkiaEntry)));
        }
    }

    /// <summary>Entry point used by HeadlessUnitTestSession: the TestKit app on Skia.</summary>
    public static class SkiaEntry
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    /// <summary>Runs dispatcher work until the condition is false (or the timeout passes).</summary>
    public static void WaitWhile(Func<bool> busy, int timeoutMs = 15000)
    {
        var sw = Stopwatch.StartNew();
        while (busy() && sw.ElapsedMilliseconds < timeoutMs)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(5);
        }
        Headless.Pump();
    }

    /// <summary>The colour of one pixel of a captured frame.</summary>
    public static Color PixelAt(WriteableBitmap bmp, int x, int y)
    {
        using var fb = bmp.Lock();
        var row = new byte[fb.RowBytes];
        Marshal.Copy(fb.Address + y * fb.RowBytes, row, 0, fb.RowBytes);
        int i = x * 4;
        return fb.Format == Avalonia.Platform.PixelFormat.Rgba8888
            ? Color.FromArgb(row[i + 3], row[i], row[i + 1], row[i + 2])
            : Color.FromArgb(row[i + 3], row[i + 2], row[i + 1], row[i]);
    }

    /// <summary>Counts pixels inside a rectangle that satisfy a test on (r, g, b).</summary>
    public static int CountWhere(WriteableBitmap bmp, PixelRect area, Func<byte, byte, byte, bool> test)
    {
        using var fb = bmp.Lock();
        var row = new byte[fb.RowBytes];
        int n = 0;
        bool rgba = fb.Format == Avalonia.Platform.PixelFormat.Rgba8888;
        for (int y = area.Y; y < area.Bottom; y++)
        {
            Marshal.Copy(fb.Address + y * fb.RowBytes, row, 0, fb.RowBytes);
            for (int x = area.X; x < area.Right; x++)
            {
                int i = x * 4;
                if (test(rgba ? row[i] : row[i + 2], row[i + 1], rgba ? row[i + 2] : row[i])) n++;
            }
        }
        return n;
    }

    /// <summary>Counts pixels close to a colour inside a rectangle.</summary>
    public static int Count(WriteableBitmap bmp, Color c, PixelRect area, int tolerance = 24)
    {
        using var fb = bmp.Lock();
        var row = new byte[fb.RowBytes];
        int n = 0;
        bool rgba = fb.Format == Avalonia.Platform.PixelFormat.Rgba8888;
        for (int y = area.Y; y < area.Bottom; y++)
        {
            Marshal.Copy(fb.Address + y * fb.RowBytes, row, 0, fb.RowBytes);
            for (int x = area.X; x < area.Right; x++)
            {
                int i = x * 4;
                byte r = rgba ? row[i] : row[i + 2], g = row[i + 1], b = rgba ? row[i + 2] : row[i];
                if (Math.Abs(r - c.R) <= tolerance && Math.Abs(g - c.G) <= tolerance && Math.Abs(b - c.B) <= tolerance) n++;
            }
        }
        return n;
    }
}

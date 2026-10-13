using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Calcy.UI.TestKit;

namespace Calcy.Modes.Graphing.Tests;

/// <summary>Starts the shared headless session with Skia (so frames have pixels) and waits for background work.</summary>
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
            // One app for the whole assembly: rebuilding it for every test raced with the render loop now and then.
            field.SetValue(null, HeadlessUnitTestSession.StartNew(typeof(SkiaEntry), AvaloniaTestIsolationLevel.PerAssembly));
        }
    }

    /// <summary>Entry point used by HeadlessUnitTestSession: the TestKit app on Skia.</summary>
    public static class SkiaEntry
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    /// <summary>Pumps the dispatcher until the condition is false (or the timeout passes), then renders.</summary>
    public static void WaitWhile(Func<bool> busy, int timeoutMs = 20000)
    {
        var sw = Stopwatch.StartNew();
        // Give debounced work a chance to start before checking.
        while (sw.ElapsedMilliseconds < 200 || (busy() && sw.ElapsedMilliseconds < timeoutMs))
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(5);
        }
        Headless.Pump();
    }
}

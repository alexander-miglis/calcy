using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Calcy.UI.TestKit;

namespace Calcy.Modes.Simple.Tests;

/// <summary>
/// Runs UI code through <see cref="Headless"/>, making sure the headless session renders with Skia so
/// <see cref="Headless.Save"/> writes real pixels.
/// </summary>
/// <remarks>
/// Calcy.UI.TestKit starts its session from <see cref="TestApp"/>, which has no BuildAvaloniaApp method, so Avalonia
/// falls back to headless drawing and captured frames are empty. Until the TestKit configures Skia itself, this
/// helper starts the session with Skia (exactly as Calcy --screenshot does) before the TestKit would start its own.
/// When TestApp gains a BuildAvaloniaApp method this helper does nothing.
/// </remarks>
internal static class Screens
{
    private static readonly object Gate = new();
    private static bool _ready;

    public static void Run(Action action)
    {
        EnsureSkia();
        Headless.Run(action);
    }

    public static T Run<T>(Func<T> func)
    {
        EnsureSkia();
        return Headless.Run(func);
    }

    private static void EnsureSkia()
    {
        lock (Gate)
        {
            if (_ready) return;
            _ready = true;
            if (typeof(TestApp).GetMethod("BuildAvaloniaApp", BindingFlags.Public | BindingFlags.Static) is not null) return;
            var field = typeof(Headless).GetField("_session", BindingFlags.NonPublic | BindingFlags.Static);
            if (field is null || field.GetValue(null) is not null) return;
            field.SetValue(null, HeadlessUnitTestSession.StartNew(typeof(SkiaTestApp)));
        }
    }
}

/// <summary>The TestKit application with the Skia renderer.</summary>
public static class SkiaTestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

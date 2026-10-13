using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Calcy.UI.Services;
using Calcy.UI.TestKit;

namespace Calcy.Modes.Code.Tests;

/// <summary>Builds the Code screen headlessly and waits for runs to finish.</summary>
internal static class TestSupport
{
    public static (Window Window, CodeView View, AppServices Services) Create(string scheme = "nes", double width = 1360, double height = 820, AppServices? services = null)
    {
        var s = services ?? Headless.CreateServices(scheme);
        var view = (CodeView)new CodeMode().CreateView(s);
        var w = Headless.Show(view, width, height);
        return (w, view, s);
    }

    /// <summary>Pumps the UI until the condition holds or the timeout passes.</summary>
    public static bool WaitUntil(Func<bool> condition, int timeoutMs = 20000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) return false;
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(5);
        }
        Headless.Pump();
        return true;
    }

    /// <summary>Starts the open program and waits until it has finished.</summary>
    public static Calcy.Scripting.ScriptRunResult? Run(CodeView view, int timeoutMs = 30000)
    {
        var task = view.RunCurrentAsync();
        Assert.True(WaitUntil(() => task.IsCompleted, timeoutMs), "the run did not finish");
        WaitUntil(() => !view.IsBusy, 5000);
        view.CanvasView.Refresh();
        Headless.Pump();
        return task.Result;
    }

    /// <summary>Opens a new program with the given source.</summary>
    public static void OpenSource(CodeView view, string name, string source)
    {
        var p = view.Store.Add(name, source);
        view.Open(p);
        Headless.Pump();
    }
}

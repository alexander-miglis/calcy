using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Calcy.UI.Math;
using Calcy.UI.Services;
using Calcy.UI.TestKit;

namespace Calcy.Modes.Notebook.Tests;

/// <summary>Builds the Notebook screen headlessly and waits for its background work.</summary>
internal static class TestSupport
{
    /// <summary>The mode area of the app at its default window size (the window minus the top bar and status line).</summary>
    public const double Width = 1360, Height = 812;

    public static (Window Window, NotebookView View, AppServices Services) Create(string scheme = "nes", double width = Width, double height = Height, AppServices? services = null)
    {
        MathEditor.AnimationsEnabled = false;
        var s = services ?? Headless.CreateServices(scheme);
        var view = (NotebookView)new NotebookMode().CreateView(s);
        var w = Headless.Show(view, width, height);
        Assert.True(WaitIdle(view), "the notebook did not settle");
        return (w, view, s);
    }

    /// <summary>Pumps until the view is idle.</summary>
    public static bool WaitIdle(NotebookView view, int timeoutMs = 30000)
    {
        bool ok = WaitUntil(() => !view.IsBusy, timeoutMs);
        Headless.Pump(4);
        return ok;
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

    /// <summary>Waits for a task started on the UI thread.</summary>
    public static T Await<T>(Task<T> task, int timeoutMs = 30000)
    {
        Assert.True(WaitUntil(() => task.IsCompleted, timeoutMs), "the task did not finish");
        return task.Result;
    }

    /// <summary>Waits for a task started on the UI thread.</summary>
    public static void Await(Task task, int timeoutMs = 30000)
    {
        Assert.True(WaitUntil(() => task.IsCompleted, timeoutMs), "the task did not finish");
        task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Asserts that every visible input, button, slider and graph inside <paramref name="container"/> lies within its
    /// width (nothing clipped at the right edge or pushed out to the left).
    /// </summary>
    public static void AssertInside(Control container, Func<Control, bool>? filter = null)
    {
        double width = container.Bounds.Width;
        Assert.True(width > 50, "the container has no size");
        var bad = new List<string>();
        foreach (var c in container.GetVisualDescendants().OfType<Control>())
        {
            if (!c.IsEffectivelyVisible || c.Bounds.Width <= 0) continue;
            if (c is not (TextBox or MathEditor or Button or Calcy.UI.Graph.GraphCanvas or Avalonia.Controls.Primitives.RangeBase)) continue;
            if (filter is not null && !filter(c)) continue;
            if (c.FindAncestorOfType<ScrollViewer>() is { } sv && sv != container && container.IsVisualAncestorOf(sv) && sv.HorizontalScrollBarVisibility != Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled) continue;
            var p = c.TranslatePoint(default, container);
            if (p is null) continue;
            if (p.Value.X < -1 || p.Value.X + c.Bounds.Width > width + 1)
                bad.Add($"{c.GetType().Name} '{Avalonia.Automation.AutomationProperties.GetName(c)}' at x={p.Value.X:0} w={c.Bounds.Width:0} (container {width:0})");
        }
        Assert.True(bad.Count == 0, "Outside their parent:\n" + string.Join("\n", bad));
    }
}

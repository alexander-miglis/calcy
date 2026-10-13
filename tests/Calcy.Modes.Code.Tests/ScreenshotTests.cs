using Calcy.UI.TestKit;
using Xunit.Abstractions;

namespace Calcy.Modes.Code.Tests;

public class ScreenshotTests(ITestOutputHelper output)
{
    public static readonly TheoryData<string> Schemes = new() { "nes", "pico", "gameboy", "amber", "paper" };

    private const string ErrorProgram = """
# Average of a list
def average(values):
    return sum(values) / len(values)

print("average:", average([3, 4, 5]))
print("empty:", average([]))
""";

    [Theory]
    [MemberData(nameof(Schemes))]
    public void ExampleRunWithTurtleDrawing(string scheme)
    {
        var path = Headless.Run(() =>
        {
            var (w, view, _) = TestSupport.Create(scheme);
            view.Open("primes.py");
            var primes = TestSupport.Run(view);
            Assert.True(primes!.Success, primes.Output);
            view.Open("star.py");
            var star = TestSupport.Run(view);
            Assert.True(star!.Success, star.Output);
            Assert.True(view.CanvasView.HasDrawing);
            return Headless.Save(w, $"code-{scheme}.png");
        });
        output.WriteLine(path);
    }

    [Theory]
    [MemberData(nameof(Schemes))]
    public void TurtleSpiral(string scheme)
    {
        var path = Headless.Run(() =>
        {
            var (w, view, _) = TestSupport.Create(scheme);
            view.Host.Options = view.Host.Options with { AnimateTurtle = false };
            view.OpenExample(Calcy.Modes.Code.Programs.Examples.Find("spiral.py")!);
            var r = TestSupport.Run(view);
            Assert.True(r!.Success, r.Output);
            return Headless.Save(w, $"code-{scheme}-turtle.png");
        });
        output.WriteLine(path);
    }

    [Theory]
    [MemberData(nameof(Schemes))]
    public void ErrorWithMarker(string scheme)
    {
        var path = Headless.Run(() =>
        {
            var (w, view, _) = TestSupport.Create(scheme);
            TestSupport.OpenSource(view, "average.py", ErrorProgram);
            var r = TestSupport.Run(view);
            Assert.False(r!.Success);
            Assert.Equal(3, view.Editor.ErrorLine);
            return Headless.Save(w, $"code-{scheme}-error.png");
        });
        output.WriteLine(path);
    }

    [Theory]
    [MemberData(nameof(Schemes))]
    public void NarrowWindow(string scheme)
    {
        var path = Headless.Run(() =>
        {
            var (w, view, _) = TestSupport.Create(scheme, 760, 1000);
            Assert.True(view.IsNarrow);
            view.Open("roots.py");
            var r = TestSupport.Run(view);
            Assert.True(r!.Success, r.Output);
            return Headless.Save(w, $"code-{scheme}-narrow.png");
        });
        output.WriteLine(path);
    }
}

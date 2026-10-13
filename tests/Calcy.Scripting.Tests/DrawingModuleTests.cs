using Calcy.Scripting.Drawing;

namespace Calcy.Scripting.Tests;

public class DrawingModuleTests
{
    private static List<string> Commands(TestHost t) => t.Host.Canvas.Commands.Select(c => c.ToString()).ToList();

    [Fact]
    public void TurtleDrawsLinesFromTheCentre()
    {
        using var t = new TestHost();
        var r = t.Run("""
import turtle
t = turtle.Turtle()
t.forward(100)
t.left(90)
t.color("red")
t.forward(50)
t.penup()
t.goto(-100, 0)
t.pendown()
t.pensize(3)
t.setheading(270)
t.forward(10)
print(t.position(), t.heading(), t.isdown())
""");
        Assert.True(r.Success, r.Output);
        Assert.Equal("(-100.0, -10.0) 270.0 True\n", t.Output);
        Assert.Equal(320, t.Host.Canvas.Width);
        Assert.Equal(220, t.Host.Canvas.Height);
        Assert.Equal(["Line 160,110 260,110 Ink", "Color #FF0000", "Line 260,110 260,60 #FF0000", "Line 60,110 60,120 #FF0000 w3"], Commands(t));
        var sprite = t.Host.Canvas.Sprite!;
        Assert.Equal(60, sprite.X, 6);
        Assert.Equal(120, sprite.Y, 6);
    }

    [Fact]
    public void TurtleModuleFunctionsCirclesFillsAndText()
    {
        using var t = new TestHost();
        var r = t.Run("""
from turtle import *
speed(0)
circle(50)
dot(10, "blue")
begin_fill()
fillcolor("green")
for i in range(3):
    forward(30)
    left(120)
end_fill()
write("Hi", align="center")
hideturtle()
bgcolor("black")
done()
""");
        Assert.True(r.Success, r.Output);
        var cmds = t.Host.Canvas.Commands;
        Assert.True(cmds.Count(c => c.Kind == DrawKind.Line) >= 20);
        Assert.Contains(cmds, c => c is { Kind: DrawKind.Circle, Fill: true, Size: 5 } && c.Color == DrawColor.FromRgb(0, 0, 255));
        Assert.Contains(cmds, c => c is { Kind: DrawKind.Polygon, Fill: true } && c.Color == DrawColor.FromRgb(0, 128, 0));
        Assert.Contains(cmds, c => c is { Kind: DrawKind.Text, Text: "Hi" });
        Assert.Equal(DrawKind.Background, cmds[^1].Kind);
        Assert.Null(t.Host.Canvas.Sprite);
        // The circle closes on itself back at the start.
        var frame = t.Host.Canvas.Capture();
        Assert.Equal(DrawColor.Ink, frame[160, 110]);
    }

    [Fact]
    public void TiPlotlibPlotsInWindowCoordinates()
    {
        using var t = new TestHost();
        var r = t.Run("""
import ti_plotlib as plt
plt.cls()
plt.window(-10, 10, -10, 10)
plt.color(255, 0, 0)
plt.scatter([0, 5], [0, 5], "o")
plt.title("Data")
plt.show_plot()
""");
        Assert.True(r.Success, r.Output);
        var cmds = Commands(t);
        Assert.Equal(320, t.Host.Canvas.Width);
        Assert.Equal(210, t.Host.Canvas.Height);
        Assert.Equal("Clear Background", cmds[0]);
        Assert.Contains("Line 0,104.5 319,104.5 Ink", cmds);
        Assert.Contains("Line 159.5,0 159.5,209 Ink", cmds);
        Assert.Contains("Circle fill 159.5,104.5 r2 #FF0000", cmds);
        Assert.Contains("Circle fill 239.25,52.25 r2 #FF0000", cmds);
        Assert.Contains(cmds, c => c.StartsWith("Text ") && c.Contains("\"Data\""));
        Assert.Equal("Show", cmds[^1]);
        Assert.True(t.Host.Canvas.IsBuffered);
    }

    [Fact]
    public void TiPlotlibLinePlotConnectsPoints()
    {
        using var t = new TestHost();
        var r = t.Run("""
import ti_plotlib as plt
plt.window(0, 2, 0, 2)
plt.axes("off")
plt.plot([0, 1, 2], [0, 2, 0], ".")
a, b = plt.lin_reg([1, 2, 3], [2, 4, 6])
print(a, b)
""");
        Assert.True(r.Success, r.Output);
        Assert.Equal("2.0 0.0\n", t.Output);
        var cmds = Commands(t);
        Assert.Contains("Line 0,209 159.5,0 Accent1", cmds);
    }

    [Fact]
    public void TiDrawUsesScreenPixels()
    {
        using var t = new TestHost();
        var r = t.Run("""
from ti_draw import *
set_color(0, 0, 255)
fill_rect(10, 20, 30, 40)
draw_circle(100, 100, 25)
set_pen("thick", "solid")
draw_line(0, 0, 317, 211)
draw_text(5, 200, "Hi")
draw_poly([0, 10, 5], [0, 0, 8])
print(get_screen_dim())
""");
        Assert.True(r.Success, r.Output);
        Assert.Equal("(318, 212)\n", t.Output);
        Assert.Equal([
            "Color #0000FF",
            "Rect fill 10,20 30x40 #0000FF",
            "Circle 100,100 r25 #0000FF",
            "Line 0,0 317,211 #0000FF w3",
            "Text 5,192 \"Hi\" #0000FF",
            "Polygon [0,0 10,0 5,8] #0000FF",
        ], Commands(t));
    }

    [Fact]
    public void TiDrawBufferShowsOnlyWhenPainted()
    {
        using var t = new TestHost();
        var r = t.Run("""
from ti_draw import *
use_buffer()
fill_rect(0, 0, 10, 10)
""");
        Assert.True(r.Success, r.Output);
        // Unpainted buffers are shown when the program ends.
        Assert.Equal(DrawColor.Ink, t.Host.Canvas.Capture()[5, 5]);
    }

    [Fact]
    public void CasioplotSetsAndReadsPixels()
    {
        using var t = new TestHost();
        var r = t.Run("""
from casioplot import *
set_pixel(10, 10, (255, 0, 0))
print(get_pixel(10, 10), get_pixel(11, 10), get_pixel(500, 0))
draw_string(0, 0, "OK", (0, 0, 0), "small")
show_screen()
clear_screen()
""");
        Assert.True(r.Success, r.Output);
        Assert.Equal("(255, 0, 0) (255, 255, 255) None\n", t.Output);
        Assert.Equal(384, t.Host.Canvas.Width);
        Assert.Equal(192, t.Host.Canvas.Height);
        Assert.Equal(["Clear Background", "Show"], Commands(t));
    }

    [Fact]
    public void CasioplotShowsTheBufferAtShowScreen()
    {
        using var t = new TestHost();
        var r = t.Run("""
from casioplot import *
set_pixel(1, 1, (255, 0, 0))
draw_string(0, 10, "OK", (0, 0, 0), "small")
show_screen()
""");
        Assert.True(r.Success, r.Output);
        Assert.Equal(["Pixel 1,1 #FF0000", "Text 0,10 \"OK\" Ink", "Show"], Commands(t));
        var frame = t.Host.Canvas.Capture();
        Assert.Equal(DrawColor.FromRgb(255, 0, 0), frame[1, 1]);
        // The first column of "O" is lit on rows 1 to 5 of the glyph.
        Assert.Equal(DrawColor.Ink, frame[0, 12]);
        Assert.Equal(DrawColor.Background, frame[0, 10]);
    }

    [Fact]
    public void CalcydrawShapesAndFrameLoop()
    {
        using var t = new TestHost();
        var r = t.Run("""
import calcydraw as d
d.clear("navy")
d.line(0, 0, 10, 10, "yellow", 2)
d.fill_circle(50, 50, 5, (0, 255, 0))
d.text(1, 1, "A", size=2)
d.polygon([(0, 0), (10, 0), (5, 5)], fill=True)
d.rect(20, 20, 5, 5, "accent2")
count = []
def frame(n):
    d.clear()
    d.pixel(n, 0, "red")
    count.append(n)
print(d.run(frame, fps=60, frames=3), count, d.size())
""");
        Assert.True(r.Success, r.Output);
        Assert.Equal("3 [0, 1, 2] (320, 220)\n", t.Output);
        Assert.True(t.Host.Canvas.IsBuffered);
        Assert.Equal(["Clear Background", "Pixel 2,0 #FF0000", "Show"], Commands(t));
        var frame = t.Host.Canvas.Capture();
        Assert.Equal(DrawColor.FromRgb(255, 0, 0), frame[2, 0]);
        Assert.Equal(DrawColor.Background, frame[0, 0]);
    }

    [Fact]
    public void CalcydrawRecordsEveryPrimitive()
    {
        using var t = new TestHost();
        var r = t.Run("""
import calcydraw as d
d.clear("navy")
d.line(0, 0, 10, 10, "yellow", 2)
d.fill_circle(50, 50, 5, (0, 255, 0))
d.text(1, 1, "A", size=2)
d.polygon([(0, 0), (10, 0), (5, 5)], fill=True)
d.rect(20, 20, 5, 5, "accent2")
d.color("white")
d.pixel(3, 3)
""");
        Assert.True(r.Success, r.Output);
        Assert.Equal([
            "Clear #000080",
            "Line 0,0 10,10 #FFFF00 w2",
            "Circle fill 50,50 r5 #00FF00",
            "Text 1,1 \"A\" Ink",
            "Polygon fill [0,0 10,0 5,5] Ink",
            "Rect 20,20 5x5 Accent2",
            "Color Background",
            "Pixel 3,3 Background",
        ], Commands(t));
    }

    [Fact]
    public void ColoursAreValidated()
    {
        using var t = new TestHost();
        var output = t.Out("""
import calcydraw as d
for c in ["nocolour", (1, 2), "#12345"]:
    try:
        d.pixel(0, 0, c)
    except (ValueError, TypeError) as e:
        print(type(e).__name__)
""");
        Assert.Equal("ValueError\nTypeError\nValueError\n", output);
    }

    [Fact]
    public void RasterizerIsPixelExact()
    {
        var canvas = new DrawCanvas();
        canvas.Reset(10, 10);
        canvas.Execute(new DrawCommand { Kind = DrawKind.Line, X = 0, Y = 0, X2 = 9, Y2 = 9, Color = DrawColor.Ink });
        canvas.Execute(new DrawCommand { Kind = DrawKind.Rect, X = 6, Y = 0, X2 = 4, Y2 = 2, Fill = true, Color = DrawColor.FromRgb(1, 2, 3) });
        canvas.Execute(new DrawCommand { Kind = DrawKind.Line, X = -100, Y = 5, X2 = 1e9, Y2 = 5, Color = DrawColor.FromToken(DrawToken.Accent1) });
        var f = canvas.Capture();
        for (int i = 0; i < 10; i++)
            if (i != 5) Assert.Equal(DrawColor.Ink, f[i, i]);
        Assert.Equal(DrawColor.FromRgb(1, 2, 3), f[9, 1]);
        Assert.Equal(DrawColor.Background, f[5, 2]);
        for (int x = 0; x < 10; x++) Assert.Equal(DrawColor.FromToken(DrawToken.Accent1), f[x, 5]);
        canvas.Execute(new DrawCommand { Kind = DrawKind.Background, Color = DrawColor.FromRgb(9, 9, 9) });
        Assert.Equal(DrawColor.FromRgb(9, 9, 9), canvas.Capture()[0, 9]);
        Assert.Equal(DrawColor.Ink, canvas.Capture()[0, 0]);
    }

    [Fact]
    public void FilledCircleIsSymmetric()
    {
        var canvas = new DrawCanvas();
        canvas.Reset(21, 21);
        canvas.Execute(new DrawCommand { Kind = DrawKind.Circle, X = 10, Y = 10, Size = 6, Fill = true, Color = DrawColor.Ink });
        var f = canvas.Capture();
        int count = 0;
        for (int y = 0; y < 21; y++)
        {
            for (int x = 0; x < 21; x++)
            {
                if (f[x, y] != DrawColor.Ink) continue;
                count++;
                Assert.Equal(DrawColor.Ink, f[20 - x, y]);
                Assert.Equal(DrawColor.Ink, f[x, 20 - y]);
                Assert.True((x - 10) * (x - 10) + (y - 10) * (y - 10) <= 6.5 * 6.5);
            }
        }
        Assert.InRange(count, 100, 140);
    }

    [Fact]
    public void BlackAndWhiteFollowTheSchemeUnlessDisabled()
    {
        using var t = new TestHost();
        t.Out("import calcydraw as d\nd.pixel(0, 0, (0, 0, 0))\nd.pixel(1, 0, 'white')\nd.pixel(2, 0, (1, 1, 1))");
        var f = t.Host.Canvas.Capture();
        Assert.Equal(DrawColor.Ink, f[0, 0]);
        Assert.Equal(DrawColor.Background, f[1, 0]);
        Assert.Equal(DrawColor.FromRgb(1, 1, 1), f[2, 0]);

        using var raw = new TestHost(ScriptHostOptions.Default with { SchemeBlackAndWhite = false });
        raw.Out("import calcydraw as d\nd.pixel(0, 0, (0, 0, 0))");
        Assert.Equal(DrawColor.FromRgb(0, 0, 0), raw.Host.Canvas.Capture()[0, 0]);
    }
}

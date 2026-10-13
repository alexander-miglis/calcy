using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Calcy.Graphing;
using Calcy.UI.TestKit;
using Xunit.Abstractions;

namespace Calcy.UI.Graph.Tests;

public class GraphCanvasTests(ITestOutputHelper output)
{
    private static readonly Viewport View = new(-6, 6, -4.5, 4.5);

    private static (Window Window, GraphCanvas Canvas, GraphDocument Doc) Show(string scheme, params string[] lines)
    {
        var s = Headless.CreateServices(scheme);
        var doc = new GraphDocument(s.Session);
        foreach (var l in lines) doc.Add(l);
        var canvas = new GraphCanvas { Items = doc.Items.ToList(), Parameters = doc.Parameters, Session = s.Session, Viewport = View };
        var w = Headless.Show(canvas, 760, 570);
        TestSupport.WaitWhile(() => canvas.IsBusy);
        return (w, canvas, doc);
    }

    [Fact]
    public void DrawsCurvesInTheSchemeInks()
    {
        TestSupport.EnsureSkia();
        var path = Headless.Run(() =>
        {
            var (w, canvas, _) = Show("nes", "y = x^2 - 2", "y = 1.5sin(x)");
            var frame = w.CaptureRenderedFrame()!;
            Assert.Equal(Color.Parse("#FFFFFF"), TestSupport.PixelAt(frame, 30, 30));
            // y1 at x = 1 is -1: pixel (443, 348).
            int g1 = TestSupport.Count(frame, Color.Parse("#0058F8"), new PixelRect(436, 340, 16, 16));
            Assert.True(g1 > 20, $"only {g1} G1 pixels near (1, -1)");
            int g2 = TestSupport.Count(frame, Color.Parse("#D82800"), new PixelRect(0, 0, 760, 570));
            Assert.True(g2 > 500, $"only {g2} G2 pixels");
            // Axes in Ink.
            Assert.Equal(Color.Parse("#000000"), TestSupport.PixelAt(frame, 100, 285));
            return Headless.Save(w, "canvas-nes.png");
        });
        output.WriteLine(path);
    }

    [Fact]
    public void ShadingAndImplicitCurves()
    {
        TestSupport.EnsureSkia();
        Headless.Run(() =>
        {
            var (w, canvas, _) = Show("nes", "y < x/2", "x^2 + y^2 = 9");
            var frame = w.CaptureRenderedFrame()!;
            // Hatch below y = x/2 (G1 dots) and not above it.
            static bool Bluish(byte r, byte g, byte b) => b - r > 80;
            int below = TestSupport.CountWhere(frame, new PixelRect(500, 480, 120, 60), Bluish);
            int above = TestSupport.CountWhere(frame, new PixelRect(100, 20, 120, 60), Bluish);
            Assert.True(below > 100, $"{below} hatch pixels below");
            Assert.Equal(0, above);
            // The circle of radius 3 passes through (3, 0): pixel (570, 285).
            int circle = TestSupport.Count(frame, Color.Parse("#D82800"), new PixelRect(562, 250, 16, 30));
            Assert.True(circle > 10, $"{circle} circle pixels");
            Headless.Save(w, "canvas-shading.png");
        });
    }

    [Fact]
    public void FindsAndShowsPointsOfInterest()
    {
        TestSupport.EnsureSkia();
        Headless.Run(() =>
        {
            var (w, canvas, doc) = Show("nes", "y = x^2 - 2", "y = a*sin(x)");
            Assert.Contains(canvas.PointsOfInterest, p => p.Kind == PoiKind.Minimum && Math.Abs(p.Y + 2) < 1e-9);
            Assert.Equal(2, canvas.PointsOfInterest.Count(p => p.Kind == PoiKind.Intersection));
            canvas.FocusItem = doc.Items[0];
            canvas.SelectedPoint = canvas.PointsOfInterest.First(p => p.Kind == PoiKind.Minimum);
            Headless.Pump();
            var frame = w.CaptureRenderedFrame()!;
            int sel = TestSupport.Count(frame, Color.Parse("#F8B800"), new PixelRect(380, 300, 230, 110));
            Assert.True(sel > 2000, $"tooltip not drawn ({sel} Sel pixels)");
            Headless.Save(w, "canvas-poi.png");
        });
    }

    [Fact]
    public void SchemeChangeRecolours()
    {
        TestSupport.EnsureSkia();
        Headless.Run(() =>
        {
            var s = Headless.CreateServices("nes");
            var doc = new GraphDocument(s.Session);
            doc.Add("y = x");
            var canvas = new GraphCanvas { Items = doc.Items.ToList(), Viewport = View };
            var w = Headless.Show(canvas, 760, 570);
            Assert.Equal(Color.Parse("#FFFFFF"), TestSupport.PixelAt(w.CaptureRenderedFrame()!, 30, 30));
            s.SetScheme("amber");
            Headless.Pump();
            Assert.Equal(Color.Parse("#241A02"), TestSupport.PixelAt(w.CaptureRenderedFrame()!, 30, 30));
            Headless.Save(w, "canvas-amber.png");
        });
    }

    [Fact]
    public void PanZoomAndBoxZoomWithTheMouse()
    {
        TestSupport.EnsureSkia();
        Headless.Run(() =>
        {
            var (w, canvas, _) = Show("nes", "y = x^2");
            var changes = 0;
            canvas.ViewportChanged += (_, _) => changes++;
            w.MouseDown(new Point(200, 450), MouseButton.Left);
            w.MouseMove(new Point(263, 450));
            w.MouseUp(new Point(263, 450), MouseButton.Left);
            Assert.Equal(-7, canvas.Viewport.XMin, 1);
            Assert.True(changes > 0);
            Assert.True(canvas.GoBack());
            Assert.Equal(View, canvas.Viewport);

            w.MouseWheel(new Point(380, 285), new Vector(0, 1));
            Assert.True(canvas.Viewport.Width < View.Width);
            Assert.Equal(0, canvas.Viewport.CenterX, 9);

            canvas.Viewport = View;
            w.MouseDown(new Point(380, 285), MouseButton.Left, RawInputModifiers.Shift);
            w.MouseMove(new Point(443, 222), RawInputModifiers.Shift);
            w.MouseUp(new Point(443, 222), MouseButton.Left, RawInputModifiers.Shift);
            Assert.Equal(0, canvas.Viewport.XMin, 1);
            Assert.Equal(1, canvas.Viewport.XMax, 1);
            Assert.Equal(1, canvas.Viewport.YMax, 1);
        });
    }

    [Fact]
    public void KeyboardZoomTraceAndPointStepping()
    {
        TestSupport.EnsureSkia();
        Headless.Run(() =>
        {
            var (w, canvas, _) = Show("nes", "y = x^2 - 2");
            canvas.Focus();
            w.KeyPress(Key.Add, RawInputModifiers.None, PhysicalKey.NumPadAdd, "+");
            Assert.True(canvas.Viewport.Width < View.Width);
            w.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
            Assert.Equal(Viewport.Standard, canvas.Viewport);
            Point2? traced = null;
            canvas.TraceMoved += (_, e) => traced = e?.Position;
            w.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
            Assert.True(canvas.IsTracing);
            w.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Assert.NotNull(traced);
            Assert.Equal(traced!.Value.X * traced.Value.X - 2, traced.Value.Y, 9);
            Headless.Save(w, "canvas-trace.png");
            w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.False(canvas.IsTracing);
            TestSupport.WaitWhile(() => canvas.IsBusy);
            w.KeyPress(Key.N, RawInputModifiers.None, PhysicalKey.N, "n");
            Assert.NotNull(canvas.SelectedPoint);
        });
    }

    [Fact]
    public void ClickingAPointOfInterestPinsIt()
    {
        TestSupport.EnsureSkia();
        Headless.Run(() =>
        {
            var (w, canvas, _) = Show("nes", "y = x^2 - 2");
            var min = canvas.PointsOfInterest.First(p => p.Kind == PoiKind.Minimum);
            PointPickedEventArgs? picked = null;
            canvas.PointPicked += (_, e) => picked = e;
            var view = new ViewTransform(canvas.Viewport, 760, 570);
            var at = new Point(view.ToPixelX(min.X), view.ToPixelY(min.Y));
            w.MouseDown(at, MouseButton.Left);
            w.MouseUp(at, MouseButton.Left);
            Assert.Equal(min.Id, canvas.SelectedPoint?.Id);
            Assert.Equal(min.Id, picked?.Point?.Id);
        });
    }

    [Fact]
    public void SliderChangesRedraw()
    {
        TestSupport.EnsureSkia();
        Headless.Run(() =>
        {
            var (w, canvas, doc) = Show("nes", "y = a");
            int Row(double y) => (int)Math.Round((4.5 - y) / 9 * 570);
            var frame = w.CaptureRenderedFrame()!;
            Assert.True(TestSupport.Count(frame, Color.Parse("#0058F8"), new PixelRect(100, Row(1) - 3, 50, 6)) > 50);
            doc.Parameters["a"]!.Value = 3;
            TestSupport.WaitWhile(() => canvas.IsBusy);
            frame = w.CaptureRenderedFrame()!;
            Assert.True(TestSupport.Count(frame, Color.Parse("#0058F8"), new PixelRect(100, Row(3) - 3, 50, 6)) > 50);
            Assert.Equal(0, TestSupport.Count(frame, Color.Parse("#0058F8"), new PixelRect(100, Row(1) - 3, 50, 6)));
        });
    }

    [Fact]
    public void ExportsSvgAndPng()
    {
        TestSupport.EnsureSkia();
        Headless.Run(() =>
        {
            var (w, canvas, _) = Show("pico", "y = x^2 - 2", "x^2 + y^2 = 9", "y < x/2", "A = (2, 2)");
            var svg = canvas.ExportSvg();
            var doc = XDocument.Parse(svg);
            Assert.Equal("svg", doc.Root!.Name.LocalName);
            Assert.Contains("#1E5AC8", svg); // Pico Night G1
            using var ms = new MemoryStream();
            canvas.SavePng(ms, 2);
            var bytes = ms.ToArray();
            Assert.True(bytes.Length > 1000);
            Assert.Equal([0x89, 0x50, 0x4E, 0x47], bytes[..4]);
            var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "canvas-export.png"), bytes);
            File.WriteAllText(Path.Combine(dir, "canvas-export.svg"), svg);
        });
    }

    [Fact]
    public void PinchZoomsAroundTheFingers()
    {
        TestSupport.EnsureSkia();
        Headless.Run(() =>
        {
            var (w, canvas, _) = Show("nes", "y = x^2");
            var a = w.TouchBegin(new Point(300, 285));
            var b = w.TouchBegin(new Point(460, 285));
            for (int i = 1; i <= 5; i++)
            {
                w.TouchMove(a, new Point(300 - i * 12, 285));
                w.TouchMove(b, new Point(460 + i * 12, 285));
            }
            w.TouchEnd(a, new Point(240, 285));
            w.TouchEnd(b, new Point(520, 285));
            Assert.True(canvas.Viewport.Width < View.Width * 0.9, $"pinch did not zoom: {canvas.Viewport}");
        });
    }

    [Fact]
    public void TypicalSceneSamplesQuickly()
    {
        TestSupport.EnsureSkia();
        Headless.Run(() =>
        {
            var s = Headless.CreateServices("nes");
            var doc = new GraphDocument(s.Session);
            foreach (var l in new[] { "y = x^2 - 2", "y = a*sin(x)", "y < x/2", "x^2 + y^2 = 9", "A = (2, 2)" }) doc.Add(l);
            var view = new ViewTransform(View, 760, 570);
            foreach (var item in doc.Items) GraphSampler.Sample(item, doc.ParameterValues(item), view);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            const int frames = 20;
            for (int f = 0; f < frames; f++)
            {
                var v = new ViewTransform(View.Pan(f * 0.05, 0), 760, 570);
                foreach (var item in doc.Items) GraphSampler.Sample(item, doc.ParameterValues(item), v);
            }
            double ms = sw.Elapsed.TotalMilliseconds / frames;
            output.WriteLine($"Full resample of the design scene: {ms:F1} ms");
            Assert.True(ms < 250, $"{ms} ms per full resample");
        });
    }

    [Fact]
    public void PiTicksAndSquareWindow()
    {
        TestSupport.EnsureSkia();
        Headless.Run(() =>
        {
            var (w, canvas, _) = Show("gameboy", "y = sin(x)", "y = cos(x)");
            canvas.TickStyle = TickStyle.Pi;
            canvas.Viewport = Viewport.Trig();
            TestSupport.WaitWhile(() => canvas.IsBusy);
            Headless.Save(w, "canvas-trig.png");
            canvas.Square();
            Assert.Equal(canvas.Viewport.Width / 760, canvas.Viewport.Height / 570, 9);
        });
    }
}

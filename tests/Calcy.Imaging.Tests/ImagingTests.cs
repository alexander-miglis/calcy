using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Calcy.Imaging;

namespace Calcy.Imaging.Tests;

public class ImagingTests
{
    /// <summary>Decodes our own PNGs (RGBA 8-bit, any filter) so the encoder can be checked end to end.</summary>
    private static (int W, int H, byte[] Rgba) Decode(byte[] png)
    {
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        int pos = 8, w = 0, h = 0;
        using var idat = new MemoryStream();
        while (pos < png.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            var type = Encoding.ASCII.GetString(png, pos + 4, 4);
            var data = png.AsSpan(pos + 8, len);
            uint crc = Png.Crc32(png.AsSpan(pos + 4, 4 + len), 0xFFFFFFFFu) ^ 0xFFFFFFFFu;
            Assert.Equal(crc, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos + 8 + len)));
            if (type == "IHDR")
            {
                w = BinaryPrimitives.ReadInt32BigEndian(data);
                h = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                Assert.Equal(8, data[8]);
                Assert.Equal(6, data[9]);
            }
            else if (type == "IDAT") idat.Write(data);
            pos += 12 + len;
            if (type == "IEND") break;
        }
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        z.CopyTo(raw);
        var f = raw.ToArray();
        int stride = w * 4;
        var outPx = new byte[h * stride];
        for (int y = 0; y < h; y++)
        {
            int type = f[y * (stride + 1)];
            for (int i = 0; i < stride; i++)
            {
                int a = i >= 4 ? outPx[y * stride + i - 4] : 0;
                int b = y > 0 ? outPx[(y - 1) * stride + i] : 0;
                int c = i >= 4 && y > 0 ? outPx[(y - 1) * stride + i - 4] : 0;
                int p = a + b - c;
                int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                int paeth = pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
                int pred = type switch { 1 => a, 2 => b, 3 => (a + b) >> 1, 4 => paeth, _ => 0 };
                outPx[y * stride + i] = (byte)(f[y * (stride + 1) + 1 + i] + pred);
            }
        }
        return (w, h, outPx);
    }

    [Fact]
    public void Png_RoundTripsPixelsExactly()
    {
        var img = new RasterImage(37, 23);
        img.Clear(Rgba.Parse("#f4ecd8"));
        img.DrawPolyline([(2, 20), (10, 3), (20, 18), (35, 5)], 3, Rgba.Parse("#d23c3c"));
        img.DrawText("Y=X2", 3, 3, 1, Rgba.Parse("#202020"));
        var png = img.ToPng();
        var (w, h, px) = Decode(png);
        Assert.Equal(37, w);
        Assert.Equal(23, h);
        Assert.Equal(img.Pixels.ToArray(), px);
    }

    [Fact]
    public void Rgba_Parses()
    {
        Assert.Equal(new Rgba(0x12, 0x34, 0x56), Rgba.Parse("#123456"));
        Assert.Equal(new Rgba(0xff, 0x00, 0xff, 0x80), Rgba.Parse("ff00ff80"));
        Assert.Equal(new Rgba(0xaa, 0xbb, 0xcc), Rgba.Parse("#abc"));
        Assert.Throws<FormatException>(() => Rgba.Parse("#12"));
        Assert.Equal("#123456", new Rgba(0x12, 0x34, 0x56).ToString());
    }

    [Fact]
    public void Lines_CoverTheirPathAndNotFarAway()
    {
        var img = new RasterImage(50, 50);
        var white = new Rgba(255, 255, 255);
        var red = new Rgba(255, 0, 0);
        img.Clear(white);
        img.DrawLine(5, 25, 45, 25, 4, red);
        Assert.Equal(red, img.GetPixel(25, 25));
        Assert.Equal(red, img.GetPixel(25, 24));
        Assert.Equal(white, img.GetPixel(25, 10));
        Assert.Equal(white, img.GetPixel(25, 30));
    }

    [Fact]
    public void Polyline_JointsAreNotDoubleBlended()
    {
        var img = new RasterImage(40, 40);
        img.Clear(new Rgba(255, 255, 255));
        var half = new Rgba(0, 0, 0, 128);
        img.DrawPolyline([(5, 20), (20, 20), (35, 20)], 4, half);
        // The joint pixel and a mid-segment pixel get the same colour.
        Assert.Equal(img.GetPixel(12, 20), img.GetPixel(20, 20));
    }

    [Fact]
    public void Text_HasThePixelFontShape()
    {
        var img = new RasterImage(20, 10);
        var white = new Rgba(255, 255, 255);
        var ink = new Rgba(0, 0, 0);
        img.Clear(white);
        img.DrawText("1", 0, 0, 1, ink);
        // '1' is 0x04 in the top row: only the middle column is set.
        Assert.Equal(ink, img.GetPixel(2, 0));
        Assert.Equal(white, img.GetPixel(0, 0));
        Assert.Equal(11, RasterImage.TextWidth("AB", 1));
    }

    [Fact]
    public void FillRect_AntiAliasesFractionalEdges()
    {
        var img = new RasterImage(10, 10);
        img.Clear(new Rgba(255, 255, 255));
        img.FillRect(2.5, 2, 3, 3, new Rgba(0, 0, 0));
        Assert.Equal(new Rgba(0, 0, 0), img.GetPixel(3, 3));
        var edge = img.GetPixel(2, 3);
        Assert.InRange(edge.R, 120, 135);
    }

    [Fact]
    public void InvalidSizes_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RasterImage(0, 10));
        Assert.Throws<ArgumentException>(() => Png.Encode(2, 2, new byte[3]));
    }

    [Fact]
    public void SampleImage_IsWritten()
    {
        var img = new RasterImage(320, 200);
        img.Clear(Rgba.Parse("#f4ecd8"));
        var grid = Rgba.Parse("#e2d6bc");
        for (int x = 0; x < 320; x += 20) img.VLine(x, 0, 199, grid);
        for (int y = 0; y < 200; y += 20) img.HLine(0, 319, y, grid);
        img.HLine(0, 319, 100, Rgba.Parse("#202020"), 2);
        img.VLine(160, 0, 199, Rgba.Parse("#202020"), 2);
        var pts = new List<(double, double)>();
        for (double x = -8; x <= 8; x += 0.05) pts.Add((160 + x * 20, 100 - Math.Sin(x) * 60));
        img.DrawPolyline(pts, 3.5, Rgba.Parse("#d23c3c"));
        img.Square(160 + Math.PI / 2 * 20, 40, 8, Rgba.Parse("#3c64d2"));
        img.DrawText("Y = SIN(X)", 8, 8, 2, Rgba.Parse("#202020"));
        var path = Path.Combine(AppContext.BaseDirectory, "imaging-sample.png");
        File.WriteAllBytes(path, img.ToPng());
        Assert.True(new FileInfo(path).Length > 100);
    }
}

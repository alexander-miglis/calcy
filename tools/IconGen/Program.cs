// Draws the Calcy app icon (the pixel calculator of the top bar) and writes it as PNG, Windows .ico and macOS .icns.
// Usage: dotnet run --project tools/IconGen -- <output folder>
using System.Buffers.Binary;
using System.Text;
using Calcy.Imaging;

var outDir = args.Length > 0 ? args[0] : Path.Combine("build", "icons");
Directory.CreateDirectory(outDir);

// 16 x 16 pixel art: '.' transparent, 'K' outline, 'B' body, 'L' display, 'D' display text, 'W' key, 'Y' accent key.
string[] art =
[
    ". K K K K K K K K K K K K K K .",
    "K B B B B B B B B B B B B B B K",
    "K B B L L L L L L L L L L B B K",
    "K B B L L L L L L L D L D B B K",
    "K B B L L L L L L L L L L B B K",
    "K B B B B B B B B B B B B B B K",
    "K B B W W B B W W B B Y Y B B K",
    "K B B W W B B W W B B Y Y B B K",
    "K B B B B B B B B B B B B B B K",
    "K B B W W B B W W B B Y Y B B K",
    "K B B W W B B W W B B Y Y B B K",
    "K B B B B B B B B B B B B B B K",
    "K B B W W B B W W B B Y Y B B K",
    "K B B W W B B W W B B Y Y B B K",
    "K B B B B B B B B B B B B B B K",
    ". K K K K K K K K K K K K K K .",
];
var colors = new Dictionary<char, Rgba>
{
    ['.'] = new(0, 0, 0, 0),
    ['K'] = Rgba.Parse("#1C1C1C"),
    ['B'] = Rgba.Parse("#F83800"),
    ['L'] = Rgba.Parse("#C4D0A0"),
    ['D'] = Rgba.Parse("#1C1C1C"),
    ['W'] = Rgba.Parse("#FCFCFC"),
    ['Y'] = Rgba.Parse("#F8B800"),
};
var grid = art.Select(row => row.Split(' ').Select(s => s[0]).ToArray()).ToArray();
if (grid.Length != 16 || grid.Any(r => r.Length != 16)) throw new InvalidOperationException("The icon art must be 16 x 16.");

byte[] Render(int size)
{
    var argb = new uint[16 * 16];
    for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            var c = colors[grid[y][x]];
            argb[y * 16 + x] = ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
        }
    // Nearest neighbour keeps the pixels crisp; sizes that are not multiples of 16 get a transparent margin.
    int scale = Math.Max(1, size / 16);
    var big = RasterImage.FromArgb(16, 16, argb, scale);
    if (big.Width == size) return big.ToPng();
    var canvas = new RasterImage(size, size);
    int offset = (size - big.Width) / 2;
    for (int y = 0; y < big.Height; y++)
        for (int x = 0; x < big.Width; x++)
            canvas.Blend(x + offset, y + offset, big.GetPixel(x, y), 1);
    return canvas.ToPng();
}

int[] pngSizes = [16, 24, 32, 48, 64, 128, 256, 512, 1024];
var pngs = pngSizes.ToDictionary(s => s, Render);
foreach (var (size, png) in pngs) File.WriteAllBytes(Path.Combine(outDir, $"calcy-{size}.png"), png);
File.WriteAllBytes(Path.Combine(outDir, "calcy.png"), pngs[256]);

// Windows .ico: a directory of PNG images (supported since Windows Vista).
int[] icoSizes = [16, 24, 32, 48, 64, 128, 256];
using (var ico = new MemoryStream())
{
    var header = new byte[6];
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), (ushort)icoSizes.Length);
    ico.Write(header);
    int offset = 6 + 16 * icoSizes.Length;
    foreach (var s in icoSizes)
    {
        var entry = new byte[16];
        entry[0] = (byte)(s >= 256 ? 0 : s);
        entry[1] = (byte)(s >= 256 ? 0 : s);
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(6), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(8), (uint)pngs[s].Length);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(12), (uint)offset);
        ico.Write(entry);
        offset += pngs[s].Length;
    }
    foreach (var s in icoSizes) ico.Write(pngs[s]);
    File.WriteAllBytes(Path.Combine(outDir, "calcy.ico"), ico.ToArray());
}

// macOS .icns: big-endian chunks of PNG data.
(string Type, int Size)[] icns = [("icp4", 16), ("icp5", 32), ("icp6", 64), ("ic07", 128), ("ic08", 256), ("ic09", 512), ("ic10", 1024)];
using (var body = new MemoryStream())
{
    foreach (var (type, size) in icns)
    {
        var head = new byte[8];
        Encoding.ASCII.GetBytes(type).CopyTo(head, 0);
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(4), (uint)(8 + pngs[size].Length));
        body.Write(head);
        body.Write(pngs[size]);
    }
    var file = new byte[8 + body.Length];
    Encoding.ASCII.GetBytes("icns").CopyTo(file, 0);
    BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4), (uint)file.Length);
    body.ToArray().CopyTo(file, 8);
    File.WriteAllBytes(Path.Combine(outDir, "calcy.icns"), file);
}

Console.WriteLine($"Wrote icons to {Path.GetFullPath(outDir)}");
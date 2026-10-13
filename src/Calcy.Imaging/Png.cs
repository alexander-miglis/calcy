using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Calcy.Imaging;

/// <summary>A minimal PNG encoder (8-bit RGBA, adaptive filtering, zlib compression from the base library).</summary>
public static class Png
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Encodes straight-alpha RGBA pixels, row by row from the top.</summary>
    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "The image must have a size.");
        if (rgba.Length != width * height * 4) throw new ArgumentException("The pixel buffer does not match the size.", nameof(rgba));

        using var png = new MemoryStream();
        png.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // colour type: RGBA
        ihdr[10] = 0; // compression
        ihdr[11] = 0; // filter
        ihdr[12] = 0; // no interlace
        WriteChunk(png, "IHDR", ihdr);

        WriteChunk(png, "IDAT", Compress(Filter(width, height, rgba)));
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>Chooses per row the filter with the smallest sum of absolute values (the usual heuristic).</summary>
    private static byte[] Filter(int width, int height, ReadOnlySpan<byte> rgba)
    {
        int stride = width * 4;
        var outBuf = new byte[height * (stride + 1)];
        var candidate = new byte[stride];
        var best = new byte[stride];
        for (int y = 0; y < height; y++)
        {
            var row = rgba.Slice(y * stride, stride);
            var prev = y > 0 ? rgba.Slice((y - 1) * stride, stride) : ReadOnlySpan<byte>.Empty;
            long bestScore = long.MaxValue;
            byte bestType = 0;
            for (byte type = 0; type <= 4; type++)
            {
                if (y == 0 && type is 2 or 3 or 4) continue;
                long score = 0;
                for (int i = 0; i < stride; i++)
                {
                    int a = i >= 4 ? row[i - 4] : 0;
                    int b = prev.IsEmpty ? 0 : prev[i];
                    int c = i >= 4 && !prev.IsEmpty ? prev[i - 4] : 0;
                    int predicted = type switch
                    {
                        1 => a,
                        2 => b,
                        3 => (a + b) >> 1,
                        4 => Paeth(a, b, c),
                        _ => 0,
                    };
                    byte v = (byte)(row[i] - predicted);
                    candidate[i] = v;
                    score += v < 128 ? v : 256 - v;
                }
                if (score < bestScore)
                {
                    bestScore = score;
                    bestType = type;
                    (best, candidate) = (candidate, best);
                }
            }
            int o = y * (stride + 1);
            outBuf[o] = bestType;
            best.CopyTo(outBuf, o + 1);
        }
        return outBuf;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        return pb <= pc ? b : c;
    }

    private static byte[] Compress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(data);
        return ms.ToArray();
    }

    private static void WriteChunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        uint crc = Crc32(typeBytes, 0xFFFFFFFFu);
        crc = Crc32(data, crc) ^ 0xFFFFFFFFu;
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        s.Write(crcBytes);
    }

    /// <summary>Continues a CRC-32 (the PNG polynomial); start with 0xFFFFFFFF and invert at the end.</summary>
    public static uint Crc32(ReadOnlySpan<byte> data, uint crc)
    {
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }
}

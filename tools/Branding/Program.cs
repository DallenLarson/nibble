using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Text;

// Turns the supplied artwork into everything Nibble actually ships:
//   * the app icon (multi-size .ico) and its 256 px .png
//   * a small transparent mark for the window header and the new tab page
//   * the wordmark, trimmed, for the README
//
// The sources are 2.5k PNGs with baked-in shadows and a lot of empty canvas, so the
// first thing this does is find the real content box and crop to it.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        var glyphSource = args.Length > 0 ? args[0] : Path.Combine(downloads, "nibble-ico.png");
        var wordSource = args.Length > 1 ? args[1] : Path.Combine(downloads, "nibble-logo.png");
        var assets = args.Length > 2
            ? args[2]
            : Path.Combine("src", "Nibble", "Assets");
        var shipped = args.Length > 3 ? args[3] : "dist";

        foreach (var dir in new[] { assets, shipped }) Directory.CreateDirectory(dir);

        using var glyph = Load(glyphSource);
        using var word = Load(wordSource);

        var glyphBox = ContentBox(glyph);
        var wordBox = ContentBox(word);
        Console.WriteLine($"glyph: {glyph.Width}x{glyph.Height}, content {glyphBox}");
        Console.WriteLine($"word : {word.Width}x{word.Height}, content {wordBox}");

        // The mark is tall: fit it by height into a square canvas with a little breathing room.
        var icon256 = RenderIcon(glyph, glyphBox, 256, padding: 0.06f);
        Write(Path.Combine(assets, "nibble.png"), Png(256, 256, Rgba(icon256)));
        Write(Path.Combine(shipped, "nibble.png"), Png(256, 256, Rgba(icon256)));

        Write(Path.Combine(assets, "nibble.ico"), Ico(
        [
            (256, Png(256, 256, Rgba(icon256))),
            (64, Bmp(64, Rgba(RenderIcon(glyph, glyphBox, 64, 0.05f)))),
            (48, Bmp(48, Rgba(RenderIcon(glyph, glyphBox, 48, 0.05f)))),
            (32, Bmp(32, Rgba(RenderIcon(glyph, glyphBox, 32, 0.04f)))),
            (24, Bmp(24, Rgba(RenderIcon(glyph, glyphBox, 24, 0.03f)))),
            (16, Bmp(16, Rgba(RenderIcon(glyph, glyphBox, 16, 0.02f))))
        ]));

        // Header / new tab mark: transparent, sized for a 26 px tall slot at 2x.
        Write(Path.Combine(assets, "nibble-mark.png"),
            Png(104, 104, Rgba(RenderIcon(glyph, glyphBox, 104, 0.01f))));

        // Wordmark: trimmed and transparent, written twice — a generous one for the README
        // and a compact one for the window header and the new tab page.
        var wordWide = 900;
        var wordTall = (int)Math.Round(wordWide * (double)wordBox.Height / wordBox.Width);
        using (var scaled = new Bitmap(wordWide, wordTall, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(scaled))
        {
            Configure(g);
            g.DrawImage(word, new Rectangle(0, 0, wordWide, wordTall), wordBox, GraphicsUnit.Pixel);
            Write(Path.Combine(shipped, "nibble-logo.png"), Png(wordWide, wordTall, Rgba(scaled)));
        }

        var markWide = 420;
        var markTall = (int)Math.Round(markWide * (double)wordBox.Height / wordBox.Width);
        using (var scaled = new Bitmap(markWide, markTall, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(scaled))
        {
            Configure(g);
            g.DrawImage(word, new Rectangle(0, 0, markWide, markTall), wordBox, GraphicsUnit.Pixel);
            Write(Path.Combine(assets, "nibble-wordmark.png"), Png(markWide, markTall, Rgba(scaled)));
        }

        Console.WriteLine("done");
        return 0;
    }

    private static Bitmap Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var loaded = new Bitmap(stream);
        return new Bitmap(loaded);
    }

    /// <summary>Bounding box of the pixels that are actually visible.</summary>
    private static Rectangle ContentBox(Bitmap source)
    {
        var data = source.LockBits(
            new Rectangle(0, 0, source.Width, source.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        int minX = source.Width, minY = source.Height, maxX = -1, maxY = -1;
        try
        {
            unsafe
            {
                for (var y = 0; y < source.Height; y++)
                {
                    var row = (byte*)data.Scan0 + y * data.Stride;
                    for (var x = 0; x < source.Width; x++)
                    {
                        var alpha = row[x * 4 + 3];
                        if (alpha <= 10) continue;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
        }
        finally
        {
            source.UnlockBits(data);
        }

        if (maxX < 0) return new Rectangle(0, 0, source.Width, source.Height);
        return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    /// <summary>Draws the content box centred in a square canvas of the given size.</summary>
    private static Bitmap RenderIcon(Bitmap source, Rectangle content, int size, float padding)
    {
        var inner = size * (1 - padding * 2);
        var scale = Math.Min(inner / content.Width, inner / content.Height);
        var w = (int)Math.Round(content.Width * scale);
        var h = (int)Math.Round(content.Height * scale);

        var target = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(target);
        Configure(g);
        g.DrawImage(source, new Rectangle((size - w) / 2, (size - h) / 2, w, h), content, GraphicsUnit.Pixel);
        return target;
    }

    private static void Configure(Graphics g)
    {
        g.CompositingMode = CompositingMode.SourceCopy;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
    }

    private static void Write(string path, byte[] bytes)
    {
        File.WriteAllBytes(path, bytes);
        Console.WriteLine($"wrote {path} ({bytes.Length:N0} bytes)");
    }

    // ---- encoders, same as the app-icon tool so the format is known-good ----

    /// <summary>GDI+ hands back BGRA for 32bppArgb; everything here wants real RGBA.</summary>
    private static byte[] Rgba(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[bitmap.Width * bitmap.Height * 4];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            for (var i = 0; i < bytes.Length; i += 4)
                (bytes[i], bytes[i + 2]) = (bytes[i + 2], bytes[i]);
            return bytes;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static byte[] Png(int width, int height, byte[] rgba)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new byte[13];
        BeInt(ihdr, 0, width);
        BeInt(ihdr, 4, height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 6;   // RGBA
        Chunk(ms, "IHDR", ihdr);

        var stride = width * 4;
        var raw = new byte[height * (stride + 1)];
        for (var y = 0; y < height; y++)
        {
            raw[y * (stride + 1)] = 0;
            Buffer.BlockCopy(rgba, y * stride, raw, y * (stride + 1) + 1, stride);
        }

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw);
        Chunk(ms, "IDAT", compressed.ToArray());
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static byte[] Bmp(int size, byte[] rgba)
    {
        var stride = size * 4;
        var maskStride = (size + 31) / 32 * 4;
        var outBytes = new byte[40 + stride * size + maskStride * size];

        LeInt(outBytes, 0, 40);
        LeInt(outBytes, 4, size);
        LeInt(outBytes, 8, size * 2);
        LeShort(outBytes, 12, 1);
        LeShort(outBytes, 14, 32);
        LeInt(outBytes, 20, stride * size);

        var offset = 40;
        for (var y = size - 1; y >= 0; y--)
        {
            for (var x = 0; x < size; x++)
            {
                var i = (y * size + x) * 4;
                outBytes[offset++] = rgba[i + 2];
                outBytes[offset++] = rgba[i + 1];
                outBytes[offset++] = rgba[i];
                outBytes[offset++] = rgba[i + 3];
            }
        }
        return outBytes;
    }

    private static byte[] Ico(List<(int Size, byte[] Data)> entries)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.Default, leaveOpen: true);
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)entries.Count);

        var offset = 6 + entries.Count * 16;
        foreach (var (size, data) in entries)
        {
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write(data.Length);
            w.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in entries) w.Write(data);
        w.Flush();
        return ms.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        BeInt(len, 0, data.Length);
        s.Write(len);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crc = Crc32([.. typeBytes, .. data]);
        var crcBytes = new byte[4];
        BeInt(crcBytes, 0, unchecked((int)crc));
        s.Write(crcBytes);
    }

    private static void BeInt(byte[] b, int i, int v)
    {
        b[i] = (byte)(v >> 24); b[i + 1] = (byte)(v >> 16); b[i + 2] = (byte)(v >> 8); b[i + 3] = (byte)v;
    }

    private static void LeInt(byte[] b, int i, int v)
    {
        b[i] = (byte)v; b[i + 1] = (byte)(v >> 8); b[i + 2] = (byte)(v >> 16); b[i + 3] = (byte)(v >> 24);
    }

    private static void LeShort(byte[] b, int i, int v)
    {
        b[i] = (byte)v; b[i + 1] = (byte)(v >> 8);
    }

    private static uint Crc32(byte[] data)
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }

        var crc = 0xFFFFFFFFu;
        foreach (var b in data) crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}

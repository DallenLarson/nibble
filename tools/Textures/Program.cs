using System.IO;
using System.IO.Compression;
using System.Text;

// Draws the block textures the Minecraft-style theme uses: 16x16 pixel art, generated
// here rather than lifted from the game, so nothing in Nibble is somebody else's art.
// A fixed seed keeps every run byte-identical.
internal static class Program
{
    private const int Size = 16;

    private static int Main(string[] args)
    {
        var outDir = args.Length > 0 ? args[0] : Path.Combine("src", "Nibble", "Assets", "Themes", "minecraft", "blocks");
        Directory.CreateDirectory(outDir);

        Write(outDir, "dirt.png", Dirt(Seed(11)));
        Write(outDir, "grass_top.png", GrassTop(Seed(12)));
        Write(outDir, "grass_side.png", GrassSide(Seed(13)));
        Write(outDir, "stone.png", Stone(Seed(14)));
        Write(outDir, "cobble.png", Cobble(Seed(15)));
        Write(outDir, "planks.png", Planks(Seed(16)));
        Write(outDir, "stone_bricks.png", Bricks(Seed(17)));
        Console.WriteLine($"wrote {outDir}");
        return 0;
    }

    /// <summary>Small deterministic generator: the same textures every build.</summary>
    private static Random Seed(int value) => new(value * 7919 + 13);

    private static readonly uint[] Packed = new uint[Size * Size];

    private static byte[] Build(Random rng, Func<int, int, Random, (byte R, byte G, byte B, byte A)> pixel)
    {
        var rgba = new byte[Size * Size * 4];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var (r, g, b, a) = pixel(x, y, rng);
                var i = (y * Size + x) * 4;
                rgba[i] = r; rgba[i + 1] = g; rgba[i + 2] = b; rgba[i + 3] = a;
            }
        }
        return rgba;
    }

    private static (byte, byte, byte, byte) Shade(int r, int g, int b, double amount) =>
        ((byte)Math.Clamp(r * amount, 0, 255), (byte)Math.Clamp(g * amount, 0, 255), (byte)Math.Clamp(b * amount, 0, 255), 255);

    private static byte[] Dirt(Random rng)
    {
        var clods = new (int X, int Y, double Shade)[26];
        for (var i = 0; i < clods.Length; i++)
            clods[i] = (rng.Next(Size), rng.Next(Size), 0.78 + rng.NextDouble() * 0.42);

        return Build(rng, (x, y, r) =>
        {
            var shade = 1.0;
            foreach (var clod in clods)
            {
                var dx = Math.Min(Math.Abs(x - clod.X), Size - Math.Abs(x - clod.X));
                var dy = Math.Min(Math.Abs(y - clod.Y), Size - Math.Abs(y - clod.Y));
                if (dx * dx + dy * dy > 6) continue;
                shade = clod.Shade;
                if (r.NextDouble() < 0.28) shade *= 0.9;
                break;
            }
            if (r.NextDouble() < 0.12) shade *= 0.86;
            if (r.NextDouble() < 0.08) shade *= 1.12;
            return Shade(0x8A, 0x63, 0x45, shade);
        });
    }

    private static byte[] GrassTop(Random rng) => Build(rng, (x, y, r) =>
    {
        var shade = 0.92 + r.NextDouble() * 0.2;
        // a few bladed tufts
        if ((x * 3 + y * 5) % 17 == 0) shade *= 0.86;
        if ((x * 5 + y * 2) % 23 == 0) shade *= 1.1;
        return Shade(0x6E, 0xA8, 0x4B, shade);
    });

    private static byte[] GrassSide(Random rng)
    {
        var fringe = new int[Size];
        for (var x = 0; x < Size; x++) fringe[x] = 3 + (rng.NextDouble() < 0.45 ? 1 : 0) + (rng.NextDouble() < 0.2 ? 1 : 0);

        var clods = new (int X, int Y, double Shade)[22];
        for (var i = 0; i < clods.Length; i++)
            clods[i] = (rng.Next(Size), rng.Next(Size), 0.76 + rng.NextDouble() * 0.44);

        return Build(rng, (x, y, r) =>
        {
            if (y < fringe[x])
            {
                var shade = 0.9 + r.NextDouble() * 0.22;
                if (y == fringe[x] - 1) shade *= 0.82;   // the dark lip where grass meets dirt
                return Shade(0x6E, 0xA8, 0x4B, shade);
            }

            var dirtShade = 1.0;
            foreach (var clod in clods)
            {
                var dx = Math.Min(Math.Abs(x - clod.X), Size - Math.Abs(x - clod.X));
                var dy = Math.Min(Math.Abs(y - clod.Y), Size - Math.Abs(y - clod.Y));
                if (dx * dx + dy * dy > 6) continue;
                dirtShade = clod.Shade;
                break;
            }
            if (r.NextDouble() < 0.12) dirtShade *= 0.87;
            if (r.NextDouble() < 0.08) dirtShade *= 1.1;
            return Shade(0x8A, 0x63, 0x45, dirtShade);
        });
    }

    private static byte[] Stone(Random rng) => Build(rng, (x, y, r) =>
    {
        var shade = 0.9 + r.NextDouble() * 0.18;
        if ((x + y * 7) % 11 == 0) shade *= 0.9;
        if ((x * 5 + y * 3) % 19 == 0) shade *= 1.09;
        return Shade(0x8C, 0x8C, 0x8C, shade);
    });

    private static byte[] Cobble(Random rng)
    {
        // chunky stones with dark mortar between them
        var stones = new List<(int X, int Y, int W, int H, double Shade)>();
        for (var row = 0; row < 4; row++)
        {
            var x = row % 2 == 0 ? -2 : -5;
            while (x < Size)
            {
                var w = 5 + rng.Next(3);
                stones.Add((x, row * 4, w, 4, 0.86 + rng.NextDouble() * 0.22));
                x += w + 1;
            }
        }

        return Build(rng, (x, y, r) =>
        {
            foreach (var s in stones)
            {
                if (x < s.X || x >= s.X + s.W || y < s.Y || y >= s.Y + s.H) continue;
                if (x == s.X || x == s.X + s.W - 1 || y == s.Y || y == s.Y + s.H - 1)
                    return Shade(0x50, 0x50, 0x50, 1);
                var shade = s.Shade * (0.96 + r.NextDouble() * 0.08);
                if (x == s.X + 1 && y == s.Y + 1) shade *= 1.08;
                return Shade(0x8C, 0x8C, 0x8C, shade);
            }
            return Shade(0x50, 0x50, 0x50, 1);
        });
    }

    private static byte[] Planks(Random rng) => Build(rng, (x, y, r) =>
    {
        var shade = 0.94 + r.NextDouble() * 0.12;
        var plank = y / 4;
        if (y % 4 == 3) return Shade(0x6B, 0x4B, 0x2B, 1);            // the groove between planks
        if ((x + plank * 5) % 16 == 0) shade *= 0.9;                    // end-grain seams
        if (r.NextDouble() < 0.1) shade *= 0.92;
        return Shade(0xB2, 0x82, 0x4C, shade);
    });

    private static byte[] Bricks(Random rng)
    {
        return Build(rng, (x, y, r) =>
        {
            var row = y / 4;
            var offset = row % 2 == 0 ? 0 : 4;
            var inMortar = y % 4 == 3 || (x + offset) % 8 == 0;
            if (inMortar) return Shade(0x6E, 0x6E, 0x6E, 1);
            var shade = 0.92 + r.NextDouble() * 0.16;
            if ((x * 3 + y) % 7 == 0) shade *= 0.94;
            return Shade(0x9A, 0x9A, 0x9A, shade);
        });
    }

    private static void Write(string dir, string name, byte[] rgba)
    {
        File.WriteAllBytes(Path.Combine(dir, name), Png(rgba));
        Console.WriteLine($"  {name}");
    }

    // ---- minimal PNG writer (same shape as the branding tool) ----

    private static byte[] Png(byte[] rgba)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new byte[13];
        BeInt(ihdr, 0, Size);
        BeInt(ihdr, 4, Size);
        ihdr[8] = 8;
        ihdr[9] = 6;
        Chunk(ms, "IHDR", ihdr);

        var stride = Size * 4;
        var raw = new byte[Size * (stride + 1)];
        for (var y = 0; y < Size; y++)
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

    private static uint Crc32(byte[] data)
    {
        var table = new uint[256];
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

using System.Windows;
using System.Windows.Media;

namespace Nibble.Services;

/// <summary>
/// The accent color is the one knob that re-tints the entire browser: chrome, controls,
/// toasts and the built-in pages all read it from these app resources.
/// </summary>
public static class Accent
{
    public const string DefaultHex = "#A3E635";

    public static readonly (string Name, string Hex)[] Palette =
    [
        ("Lime", "#A3E635"),
        ("Sky", "#37A6F0"),
        ("Grape", "#8B5CF6"),
        ("Coral", "#FF6B6B"),
        ("Mango", "#FFB020"),
        ("Mint", "#2DD4A7"),
        ("Bubblegum", "#FF6BB5"),
        ("Berry", "#E0457B"),
        ("Ink", "#1D1D1F")
    ];

    public static Color Current { get; private set; } = Parse(DefaultHex);

    public static string CurrentHex { get; private set; } = DefaultHex;

    public static void Apply(string? hex)
    {
        var color = Parse(hex);
        Current = color;
        CurrentHex = ToHex(color);

        var dark = Theme.IsDark;
        var baseColor = dark ? Color.FromRgb(0x2C, 0x2C, 0x2E) : Colors.White;
        var soft = Blend(color, baseColor, dark ? 0.24 : 0.16);
        var ink = Luminance(color) > 0.62 ? Color.FromRgb(0x16, 0x25, 0x0A) : Colors.White;

        Set("Accent", color);
        Set("AccentSoft", soft);
        Set("AccentInk", ink);
    }

    public static Color Parse(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                if (ColorConverter.ConvertFromString(hex.Trim()) is Color parsed) return parsed;
            }
            catch
            {
                // fall through to the default
            }
        }
        return (Color)ColorConverter.ConvertFromString(DefaultHex)!;
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>Hex string for the pages, so their CSS accent matches the shell.</summary>
    public static string SoftHex => ToHex(((SolidColorBrush)Application.Current.Resources["AccentSoft"]).Color);

    public static string InkHex => ToHex(((SolidColorBrush)Application.Current.Resources["AccentInk"]).Color);

    public static bool IsLightAccent => Luminance(Current) > 0.62;

    /// <summary>True when text drawn on top of this color should be dark.</summary>
    public static bool IsLight(Color color) => Luminance(color) > 0.62;

    private static void Set(string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Application.Current.Resources[key] = brush;
    }

    private static Color Blend(Color from, Color to, double amount)
    {
        byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
        return Color.FromRgb(Mix(from.R, to.R), Mix(from.G, to.G), Mix(from.B, to.B));
    }

    private static double Luminance(Color color) =>
        (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;
}

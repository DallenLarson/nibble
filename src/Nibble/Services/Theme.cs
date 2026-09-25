using System.Windows;

namespace Nibble.Services;

public static class Theme
{
    public static string Current { get; private set; } = "Light";

    public static bool IsDark => Current == "Dark";

    /// <summary>
    /// The keys the theme on screen painted over the palette. They live in the app's own
    /// resources, which sit in front of the merged palette, so they have to be lifted off
    /// again before another palette goes in - otherwise "Nibble" still looks like the
    /// theme you just switched away from.
    /// </summary>
    private static readonly List<string> PaintedKeys = [];

    /// <summary>Swaps dictionary 0 (the palette) so every DynamicResource updates at once.</summary>
    public static void Apply(string theme, IReadOnlyDictionary<string, string>? overrides = null, string? basePalette = null)
    {
        // A theme names the palette it is built on, then paints over it.
        var resolved = basePalette is null
            ? Resolve(theme)
            : Resolve(basePalette.Equals("dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light");
        Current = resolved;

        var app = Application.Current;
        if (app is null) return;

        foreach (var key in PaintedKeys) app.Resources.Remove(key);
        PaintedKeys.Clear();

        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Nibble;component/Themes/{resolved}.xaml", UriKind.Absolute)
        };

        var merged = app.Resources.MergedDictionaries;
        if (merged.Count > 0) merged[0] = dict;
        else merged.Add(dict);

        if (overrides is null || overrides.Count == 0) return;

        foreach (var (key, value) in overrides)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (key.Equals("ButtonStrokeThickness", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var thickness))
                {
                    app.Resources[key] = thickness;
                    PaintedKeys.Add(key);
                }
                continue;
            }

            if (Themes.Brush(value) is { } brush)
            {
                app.Resources[key] = brush;
                PaintedKeys.Add(key);
            }
        }
    }

    private static string Resolve(string theme) => theme switch
    {
        "Dark" => "Dark",
        "Light" => "Light",
        _ => IsSystemDark() ? "Dark" : "Light"
    };

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }
}

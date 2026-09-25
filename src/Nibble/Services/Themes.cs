using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace Nibble.Services;

/// <summary>What a theme says about itself. Every field is optional except id and name.</summary>
public sealed class ThemeManifest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Tagline { get; set; } = string.Empty;

    /// <summary>Which built-in palette to start from: "light" or "dark".</summary>
    public string Base { get; set; } = "light";

    /// <summary>Accent the theme applies while it is on (the user's own choice comes back after).</summary>
    public string Accent { get; set; } = string.Empty;

    /// <summary>Palette overrides, keyed by the resource names in Themes/Light.xaml.</summary>
    public Dictionary<string, string> Colors { get; set; } = [];

    public ThemePage Page { get; set; } = new();

    public ThemePreview Preview { get; set; } = new();

    /// <summary>Where this theme was read from, so it can be exported or removed again.</summary>
    public string Source { get; set; } = string.Empty;

    public bool BuiltIn { get; set; }
}

public sealed class ThemePage
{
    public string Css { get; set; } = "theme.css";
    public string Js { get; set; } = "theme.js";
}

/// <summary>The four-ish colours the shop draws a card with, plus an optional block texture.</summary>
public sealed class ThemePreview
{
    public string Sky { get; set; } = "#F4F4F7";
    public string Ground { get; set; } = "#D8D8DE";
    public string Button { get; set; } = "#FFFFFF";
    public string Text { get; set; } = "#1D1D1F";
    public string Accent { get; set; } = "#A3E635";
    public string Texture { get; set; } = string.Empty;
}

/// <summary>
/// The theme shop's catalogue. Built-in themes ship inside the app and are extracted next
/// to the built-in pages; themes a person installs live in %AppData%\Nibble\themes. Both
/// are read through the same parser, and a user theme with the same id wins.
/// </summary>
public static class Themes
{
    public const string DefaultId = "default";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>Where the shop keeps imported themes.</summary>
    public static string Folder => Path.Combine(Store.DataDir, "themes");

    /// <summary>Where the new tab page looks for them (next to the built-in pages).</summary>
    public static string AssetFolder => Path.Combine(Pages.Folder, "themes");

    private static List<ThemeManifest>? _catalog;

    /// <summary>Every theme on offer: built-ins first, then whatever has been installed.</summary>
    public static IReadOnlyList<ThemeManifest> All()
    {
        _catalog ??= Load();
        return _catalog;
    }

    public static void Refresh()
    {
        _catalog = null;
        SyncAssets();
    }

    public static ThemeManifest? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return All().FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public static ThemeManifest? Current => Find(App.Settings.ThemeId);

    public static string CurrentId =>
        Current is null ? DefaultId : Current.Id;

    /// <summary>The compact form the built-in page reads from its init message.</summary>
    public static object PageMessage()
    {
        var theme = Current;
        return theme is null
            ? new { id = DefaultId, css = string.Empty, js = string.Empty }
            : new { id = theme.Id, css = theme.Page.Css, js = theme.Page.Js };
    }

    /// <summary>Switches theme: palette, accent, settings, and every open tab's page.</summary>
    public static void Apply(string? id)
    {
        var theme = Find(id);
        App.Settings.ThemeId = theme?.Id ?? DefaultId;
        // A theme names the palette it was drawn on; the person can still switch light/dark
        // afterwards and keep the theme's colours on top.
        if (theme is { Base.Length: > 0 }) App.Settings.Theme = theme.Base;
        Store.SaveSettings(App.Settings);

        Theme.Apply(App.Settings.Theme, theme?.Colors);

        // The theme's accent wins while it is on; the person's own accent is remembered in
        // settings and comes straight back when they switch to the default theme.
        Accent.Apply(theme is { Accent.Length: > 0 } ? theme.Accent : App.Settings.AccentColor);

        foreach (var window in Application.Current?.Windows.OfType<MainWindow>() ?? [])
        {
            Native.SetDarkFrame(window, Theme.IsDark);
            window.SendThemeToPages();
        }
    }

    private static List<ThemeManifest> Load()
    {
        // The stock look is a theme too: without it there is no way back from a theme you
        // tried in the shop. It carries no colour overrides, so applying it hands the
        // palette back to the person's own light/dark choice and their own accent.
        var byId = new Dictionary<string, ThemeManifest>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultId] = new ThemeManifest
            {
                Id = DefaultId,
                Name = "Nibble",
                Author = "Nibble",
                Tagline = "The stock look — your accent, light or dark, pixel corners.",
                BuiltIn = true,
                Preview = new ThemePreview
                {
                    Sky = "#F4F4F7",
                    Ground = "#D8D8DE",
                    Button = "#FFFFFF",
                    Text = "#1D1D1F",
                    Accent = "#A3E635"
                }
            }
        };

        foreach (var folder in new[] { (Path: AssetFolder, BuiltIn: true), (Path: Folder, BuiltIn: false) })
        {
            if (!Directory.Exists(folder.Path)) continue;
            foreach (var dir in Directory.EnumerateDirectories(folder.Path))
            {
                var manifestPath = Path.Combine(dir, "theme.json");
                if (!File.Exists(manifestPath)) continue;
                try
                {
                    var theme = Parse(File.ReadAllText(manifestPath));
                    if (theme is null || string.IsNullOrWhiteSpace(theme.Id)) continue;
                    theme.Id = theme.Id.Trim().ToLowerInvariant();
                    theme.Source = dir;
                    theme.BuiltIn = folder.BuiltIn;

                    // A folder that is only there because of the id must not shadow the id.
                    if (folder.BuiltIn && byId.ContainsKey(theme.Id)) continue;
                    byId[theme.Id] = theme;
                }
                catch
                {
                    // A broken theme file simply does not appear in the shop.
                }
            }
        }

        return
        [
            .. byId.Values
                .OrderBy(t => t.Id.Equals(DefaultId, StringComparison.OrdinalIgnoreCase) ? 0 : t.BuiltIn ? 1 : 2)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>Copies every theme's page assets where the new tab page can reach them.</summary>
    public static void SyncAssets()
    {
        try
        {
            Directory.CreateDirectory(AssetFolder);
            var known = new HashSet<string>(All().Select(t => t.Id), StringComparer.OrdinalIgnoreCase);

            // drop folders for themes that are gone
            foreach (var dir in Directory.EnumerateDirectories(AssetFolder))
            {
                var id = Path.GetFileName(dir);
                if (known.Contains(id)) continue;
                try { Directory.Delete(dir, true); } catch { /* still in use; harmless */ }
            }

            foreach (var theme in All())
            {
                var target = Path.Combine(AssetFolder, theme.Id);
                Directory.CreateDirectory(target);

                // built-ins are already extracted by Pages.Ensure; user themes are copied over
                if (theme.BuiltIn || string.IsNullOrEmpty(theme.Source)) continue;
                foreach (var file in Directory.EnumerateFiles(theme.Source, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(theme.Source, file);
                    var destination = Path.Combine(target, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(file, destination, true);
                }
            }
        }
        catch
        {
            // Themes are a nicety: a failure here must not stop the browser starting.
        }
    }

    /// <summary>Reads a .json / .nibbletheme file as a theme and installs it.</summary>
    public static (bool Ok, string Message) Import(string path)
    {
        try
        {
            var theme = Parse(File.ReadAllText(path));
            if (theme is null || string.IsNullOrWhiteSpace(theme.Name))
                return (false, "That file does not look like a Nibble theme.");

            theme.Id = string.IsNullOrWhiteSpace(theme.Id)
                ? new string(theme.Name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray())
                : theme.Id.Trim().ToLowerInvariant();
            if (theme.Id.Length == 0) theme.Id = "theme";

            var target = Path.Combine(Folder, theme.Id);
            Directory.CreateDirectory(target);

            // A theme can be a single json file, or a json file sitting next to its css/js.
            var source = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Folder;
            File.WriteAllText(Path.Combine(target, "theme.json"), JsonSerializer.Serialize(theme, Json));
            foreach (var file in Directory.EnumerateFiles(source))
            {
                var name = Path.GetFileName(file);
                if (name.Equals("theme.json", StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(file, Path.Combine(target, name), true);
            }
            foreach (var dir in new[] { "blocks", "assets" })
            {
                var from = Path.Combine(source, dir);
                if (!Directory.Exists(from)) continue;
                var to = Path.Combine(target, dir);
                Directory.CreateDirectory(to);
                foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
                    File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), true);
            }

            Refresh();
            return (true, $"Installed “{theme.Name}”.");
        }
        catch (Exception ex)
        {
            return (false, $"Could not read that theme: {ex.Message}");
        }
    }

    /// <summary>Writes the current theme out as one file, for sharing or editing.</summary>
    public static bool Export(string? id, string path)
    {
        var theme = Find(id) ?? Current;
        if (theme is null) return false;
        try
        {
            var copy = new ThemeManifest
            {
                Id = theme.Id,
                Name = theme.Name,
                Author = theme.Author,
                Tagline = theme.Tagline,
                Base = theme.Base,
                Accent = theme.Accent,
                Colors = theme.Colors,
                Page = theme.Page,
                Preview = theme.Preview
            };
            File.WriteAllText(path, JsonSerializer.Serialize(copy, Json));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool Remove(string id)
    {
        var theme = Find(id);
        if (theme is null || theme.BuiltIn) return false;
        try
        {
            Directory.Delete(Path.Combine(Folder, theme.Id), true);
            if (string.Equals(App.Settings.ThemeId, theme.Id, StringComparison.OrdinalIgnoreCase))
                Apply(DefaultId);
            Refresh();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reads a theme file by hand rather than by attribute binding, so a theme someone
    /// edits stays loadable: comments and trailing commas are fine, and a colour written
    /// as a number ("ButtonStrokeThickness": 2) is not a fatal type error.
    /// </summary>
    public static ThemeManifest? Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        var theme = new ThemeManifest
        {
            Id = Str(root, "id") ?? string.Empty,
            Name = Str(root, "name") ?? string.Empty,
            Author = Str(root, "author") ?? string.Empty,
            Tagline = Str(root, "tagline") ?? string.Empty,
            Accent = Str(root, "accent") ?? string.Empty,
            Base = Str(root, "base") ?? "light"
        };

        if (root.TryGetProperty("colors", out var colors) && colors.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in colors.EnumerateObject())
                theme.Colors[entry.Name] = Value(entry.Value);
        }

        if (root.TryGetProperty("page", out var page) && page.ValueKind == JsonValueKind.Object)
        {
            theme.Page.Css = Str(page, "css") ?? "theme.css";
            theme.Page.Js = Str(page, "js") ?? "theme.js";
        }

        if (root.TryGetProperty("preview", out var preview) && preview.ValueKind == JsonValueKind.Object)
        {
            theme.Preview.Sky = Str(preview, "sky") ?? theme.Preview.Sky;
            theme.Preview.Ground = Str(preview, "ground") ?? theme.Preview.Ground;
            theme.Preview.Button = Str(preview, "button") ?? theme.Preview.Button;
            theme.Preview.Text = Str(preview, "text") ?? theme.Preview.Text;
            theme.Preview.Accent = Str(preview, "accent") ?? theme.Preview.Accent;
            theme.Preview.Texture = Str(preview, "texture") ?? string.Empty;
        }

        return string.IsNullOrWhiteSpace(theme.Name) ? null : theme;
    }

    private static string? Str(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value)) return null;
        return Value(value);
    }

    /// <summary>Anything a hand-edited theme might put in a colour slot, as text.</summary>
    private static string Value(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => string.Empty
    };

    /// <summary>Turns a hex string into a brush, or null when it is not usable.</summary>
    public static SolidColorBrush? Brush(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try
        {
            if (ColorConverter.ConvertFromString(hex.Trim()) is not Color color) return null;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return null;
        }
    }
}

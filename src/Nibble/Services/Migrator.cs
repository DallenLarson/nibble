using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nibble.Services;

/// <summary>Another browser we can pull data out of, and what we can honestly take.</summary>
public sealed record DetectedBrowser(string Name, string Kind, string ProfileDirectory, string Summary);

/// <summary>What one import actually did.</summary>
public sealed record ImportResult(int Bookmarks, string? SearchEngine, string[] Skipped);

/// <summary>
/// One-click migration, scoped to what is actually safe and feasible while riding the
/// shared engine: bookmarks and the default search engine, imported directly from the
/// other browser's own profile files. History, cookies, autofill and passwords live in
/// SQLite databases and DPAPI-encrypted stores, which need a much deeper (and riskier)
/// importer - those are reported as skipped rather than quietly faked.
/// </summary>
public static partial class Migrator
{
    private static readonly string[] ChromiumBrowsers =
    [
        "Google\\Chrome\\User Data",
        "Microsoft\\Edge\\User Data",
        "BraveSoftware\\Brave-Browser\\User Data",
        "Vivaldi\\User Data",
        "Chromium\\User Data"
    ];

    public static List<DetectedBrowser> Detect()
    {
        var found = new List<DetectedBrowser>();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        foreach (var relative in ChromiumBrowsers)
        {
            try
            {
                var userData = Path.Combine(local, relative);
                var profile = Path.Combine(userData, "Default");
                if (!Directory.Exists(profile)) continue;

                var name = Path.GetFileName(Path.GetDirectoryName(userData)!) ?? "Chromium browser";
                var bookmarks = CountChromiumBookmarks(Path.Combine(profile, "Bookmarks"));
                if (bookmarks == 0 && !File.Exists(Path.Combine(profile, "Preferences"))) continue;

                found.Add(new DetectedBrowser(
                    name,
                    "chromium",
                    profile,
                    bookmarks == 0 ? "search engine" : $"{bookmarks:N0} bookmarks"));
            }
            catch
            {
                // A missing or locked profile is not an error worth reporting.
            }
        }

        // Firefox keeps rotating JSON bookmark backups, which need no SQLite reader.
        try
        {
            var profiles = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Mozilla", "Firefox", "Profiles");

            if (Directory.Exists(profiles))
            {
                foreach (var profile in Directory.GetDirectories(profiles))
                {
                    var backups = Path.Combine(profile, "bookmarkbackups");
                    if (!Directory.Exists(backups)) continue;

                    var newest = Directory.GetFiles(backups, "*.json")
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault();
                    if (newest is null) continue;

                    var count = CountFirefoxBookmarks(newest);
                    found.Add(new DetectedBrowser(
                        "Firefox",
                        "firefox",
                        profile,
                        count == 0 ? "search engine" : $"{count:N0} bookmarks"));
                    break;
                }
            }
        }
        catch
        {
            // Same: optional.
        }

        return found;
    }

    public static ImportResult Import(DetectedBrowser browser)
    {
        var urls = browser.Kind == "firefox"
            ? ReadFirefoxBookmarks(browser.ProfileDirectory)
            : ReadChromiumBookmarks(Path.Combine(browser.ProfileDirectory, "Bookmarks"));

        var added = 0;
        foreach (var (url, title) in urls)
        {
            if (string.IsNullOrWhiteSpace(url)) continue;
            if (url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) continue;
            if (Store.IsBookmarked(url)) continue;

            Store.Bookmarks.Insert(0, new Bookmark { Url = url, Title = title });
            added++;
        }
        if (added > 0) Store.SaveBookmarks();

        var engine = browser.Kind == "firefox"
            ? ReadFirefoxSearchEngine(browser.ProfileDirectory)
            : ReadChromiumSearchEngine(Path.Combine(browser.ProfileDirectory, "Preferences"));

        var skipped = new[]
        {
            "history", "cookies", "autofill", "passwords", "open tabs"
        };

        return new ImportResult(added, engine, skipped);
    }

    // =====================================================================
    //  chromium profiles
    // =====================================================================

    private static IEnumerable<(string Url, string Title)> ReadChromiumBookmarks(string path)
    {
        if (!File.Exists(path)) yield break;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch
        {
            yield break;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("roots", out var roots)) yield break;
            foreach (var root in roots.EnumerateObject())
            {
                foreach (var item in WalkChromium(root.Value)) yield return item;
            }
        }
    }

    private static IEnumerable<(string Url, string Title)> WalkChromium(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object) yield break;

        if (node.TryGetProperty("type", out var type))
        {
            if (type.GetString() == "url" && node.TryGetProperty("url", out var url))
            {
                var title = node.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty;
                var value = url.GetString() ?? string.Empty;
                if (value.Length > 0) yield return (value, title);
                yield break;
            }
        }

        if (!node.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array) yield break;
        foreach (var child in children.EnumerateArray())
        {
            foreach (var item in WalkChromium(child)) yield return item;
        }
    }

    private static int CountChromiumBookmarks(string path)
    {
        try
        {
            return ReadChromiumBookmarks(path).Count();
        }
        catch
        {
            return 0;
        }
    }

    private static string? ReadChromiumSearchEngine(string preferencesPath)
    {
        if (!File.Exists(preferencesPath)) return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(preferencesPath));
            var root = document.RootElement;

            foreach (var property in new[] { "default_search_provider_data", "default_search_provider" })
            {
                if (!root.TryGetProperty(property, out var provider)) continue;
                if (!provider.TryGetProperty("template_url_data", out var data)) data = provider;

                var name = data.TryGetProperty("short_name", out var shortName) ? shortName.GetString() : null;
                var keyword = data.TryGetProperty("keyword", out var key) ? key.GetString() : null;
                var mapped = MapEngine(name) ?? MapEngine(keyword);
                if (mapped is not null) return mapped;
            }
        }
        catch
        {
            // Preferences is a big file; a failed parse just means "no engine found".
        }

        return null;
    }

    // =====================================================================
    //  firefox profiles
    // =====================================================================

    private static string? NewestFirefoxBackup(string profileDirectory)
    {
        var backups = Path.Combine(profileDirectory, "bookmarkbackups");
        if (!Directory.Exists(backups)) return null;
        return Directory.GetFiles(backups, "*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static IEnumerable<(string Url, string Title)> ReadFirefoxBookmarks(string profileDirectory)
    {
        var path = NewestFirefoxBackup(profileDirectory);
        if (path is null) yield break;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch
        {
            yield break;
        }

        using (document)
        {
            foreach (var item in WalkFirefox(document.RootElement)) yield return item;
        }
    }

    private static IEnumerable<(string Url, string Title)> WalkFirefox(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object) yield break;

        if (node.TryGetProperty("uri", out var uri) && uri.ValueKind == JsonValueKind.String)
        {
            var url = uri.GetString() ?? string.Empty;
            var title = node.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            if (url.Length > 0) yield return (url, title);
        }

        if (!node.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array) yield break;
        foreach (var child in children.EnumerateArray())
        {
            foreach (var item in WalkFirefox(child)) yield return item;
        }
    }

    private static int CountFirefoxBookmarks(string backupPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(backupPath));
            return WalkFirefox(document.RootElement).Count();
        }
        catch
        {
            return 0;
        }
    }

    private static string? ReadFirefoxSearchEngine(string profileDirectory)
    {
        try
        {
            var prefs = Path.Combine(profileDirectory, "prefs.js");
            if (!File.Exists(prefs)) return null;

            var match = EnginePref().Match(File.ReadAllText(prefs));
            return match.Success ? MapEngine(match.Groups[1].Value) : null;
        }
        catch
        {
            return null;
        }
    }

    [GeneratedRegex("user_pref\\(\"browser\\.search\\.defaultenginename\",\\s*\"([^\"]+)\"\\)")]
    private static partial Regex EnginePref();

    // =====================================================================
    //  mapping
    // =====================================================================

    private static string? MapEngine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.ToLowerInvariant();

        if (text.Contains("duck") || text.Contains("ddg")) return "duckduckgo";
        if (text.Contains("brave")) return "brave";
        if (text.Contains("bing") || text.Contains("microsoft")) return "bing";
        if (text.Contains("google")) return "google";
        return null;
    }
}

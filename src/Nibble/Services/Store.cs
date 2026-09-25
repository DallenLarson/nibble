using System.IO;
using System.Text.Json;

namespace Nibble.Services;

public sealed class Settings
{
    /// <summary>Light, Dark, or System.</summary>
    public string Theme { get; set; } = "System";
    public string SearchEngine { get; set; } = "duckduckgo";
    public bool Blocker { get; set; } = true;
    public int SuspendSeconds { get; set; } = 30;
    public bool RestoreSession { get; set; } = true;

    /// <summary>Set once the first-run setup has been completed or skipped.</summary>
    public bool Onboarded { get; set; }

    /// <summary>Accent color as #RRGGBB.</summary>
    public string AccentColor { get; set; } = "#A3E635";

    /// <summary>Optional name used for the greeting on the new tab page.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>New tab clock: "24", "12", or "auto" (follow Windows).</summary>
    public string Clock { get; set; } = ClockFormat.TwentyFour;

    /// <summary>Which theme is on: "default", or the id of a theme from the shop.</summary>
    public string ThemeId { get; set; } = Themes.DefaultId;

    /// <summary>Report a plain Chrome user agent; some sites block embedded engines.</summary>
    public bool ChromeUserAgent { get; set; }

    /// <summary>Let sites use camera, microphone, location and notifications without asking.</summary>
    public bool AllowSitePermissions { get; set; }

    /// <summary>
    /// Check the release feed for a newer Nibble and install it at the next launch. On by
    /// default, because a browser that never updates is a browser with known bugs in it; it is
    /// the only network request Nibble makes without being asked, so it can be turned off in
    /// the menu.
    /// </summary>
    public bool Updates { get; set; } = true;

    /// <summary>When the feed was last asked, so a launch is not a network call.</summary>
    public DateTimeOffset LastUpdateCheck { get; set; }

    /// <summary>The version that last ran here, so a change can be announced once.</summary>
    public string LastVersion { get; set; } = string.Empty;

    /// <summary>Remembered window rectangle; 0 means "never positioned yet".</summary>
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public double WindowLeft { get; set; }
    public double WindowTop { get; set; }
    public bool WindowMaximized { get; set; }
}

public sealed class HistoryEntry
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset Visited { get; set; } = DateTimeOffset.Now;
}

public sealed class Bookmark
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset Added { get; set; } = DateTimeOffset.Now;
}

public sealed class Session
{
    public List<string> Urls { get; set; } = [];
    public int Active { get; set; }
}

/// <summary>Tiny JSON persistence layer under %AppData%\Nibble.</summary>
public static class Store
{
    /// <summary>Set to a folder path to keep a second, completely separate profile.</summary>
    public static readonly bool CustomProfile = !string.IsNullOrWhiteSpace(ProfileOverride);

    private static string? ProfileOverride
    {
        get
        {
            try
            {
                var value = Environment.GetEnvironmentVariable("NIBBLE_PROFILE");
                return string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value);
            }
            catch
            {
                return null;
            }
        }
    }

    public static readonly string DataDir = ProfileOverride
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nibble");

    /// <summary>One-time move of the old "Zest" folder so history and settings survive the rename.</summary>
    public static void MigrateLegacy()
    {
        try
        {
            if (CustomProfile) return;
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var legacy = Path.Combine(appData, "Zest");
            if (Directory.Exists(DataDir) || !Directory.Exists(legacy)) return;
            Directory.Move(legacy, DataDir);
        }
        catch
        {
            // If the old folder is busy, just start with a clean profile.
        }
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private static readonly object Gate = new();

    public static List<HistoryEntry> History { get; private set; } = [];
    public static List<Bookmark> Bookmarks { get; private set; } = [];
    public static long BlockedTotal { get; set; }
    public static long SavedBytesTotal { get; set; }
    public static int Launches { get; set; }
    public static int PagesVisited { get; set; }
    public static int TabsClosed { get; set; }
    public static long MemoryFreed { get; set; }
    public static long MemoryFreedSession { get; set; }

    public static void LoadAll()
    {
        Directory.CreateDirectory(DataDir);
        History = Read<List<HistoryEntry>>("history.json") ?? [];
        Bookmarks = Read<List<Bookmark>>("bookmarks.json") ?? [];
        var stats = Read<Stats>("stats.json");
        if (stats is not null)
        {
            BlockedTotal = stats.Blocked;
            SavedBytesTotal = stats.SavedBytes;
            Launches = stats.Launches + 1;
            PagesVisited = stats.PagesVisited;
            TabsClosed = stats.TabsClosed;
            MemoryFreed = stats.MemoryFreed;
        }
        else
        {
            Launches = 1;
        }
    }

    public static Settings LoadSettings() => Read<Settings>("settings.json") ?? new Settings();

    public static void SaveSettings(Settings settings) => Write("settings.json", settings);

    /// <summary>Appends a one-line diagnostic to nibble.log.</summary>
    public static void LogError(string message)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.AppendAllText(Path.Combine(DataDir, "nibble.log"), $"[{DateTime.Now:u}] {message}\n");
        }
        catch
        {
            // Diagnostics must never break browsing.
        }
    }

    public static void SaveHistory()
    {
        lock (Gate)
        {
            if (History.Count > 900) History.RemoveRange(900, History.Count - 900);
        }
        Write("history.json", History);
    }

    public static void SaveBookmarks() => Write("bookmarks.json", Bookmarks);

    // =====================================================================
    //  full reset
    // =====================================================================

    private static string ResetMarker => Path.Combine(DataDir, "reset.pending");

    /// <summary>
    /// Flags a complete wipe. It cannot be done while the app is running (the engine holds
    /// its profile open), so the work happens on the next launch, before any engine starts.
    /// </summary>
    public static void BeginFullReset()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(ResetMarker, DateTimeOffset.Now.ToString("u"));
        }
        catch
        {
            // If even the marker cannot be written, the reset simply will not happen.
        }
    }

    /// <summary>
    /// Deletes everything under %AppData%\Nibble when a reset is pending: settings,
    /// history, bookmarks, session, stats, the built-in pages and the whole WebView2
    /// profile (cookies, cache, storage). The marker is removed last, so a launch that
    /// could not finish the wipe retries next time.
    /// </summary>
    public static bool PerformPendingReset()
    {
        try
        {
            if (!File.Exists(ResetMarker)) return false;

            // The previous instance disposed its engine and asked the new one to do the
            // deleting, but the old engine processes can take a moment to actually let go
            // of the profile. Three passes over the whole folder, with a pause between
            // them, covers that hand-off without ever hanging the launch.
            for (var pass = 0; pass < 3; pass++)
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(DataDir))
                {
                    if (string.Equals(entry, ResetMarker, StringComparison.OrdinalIgnoreCase)) continue;
                    DeleteWithRetry(entry);
                }

                if (!Directory.Exists(Path.Combine(DataDir, "WebView2")))
                {
                    File.Delete(ResetMarker);
                    return true;
                }

                Thread.Sleep(400);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static void DeleteWithRetry(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
                else if (File.Exists(path)) File.Delete(path);
                return;
            }
            catch
            {
                // The engine may still be shutting down; give it a moment and try again.
                Thread.Sleep(200);
            }
        }
    }

    public static void SaveStats() => Write("stats.json", new Stats
    {
        Blocked = BlockedTotal,
        SavedBytes = SavedBytesTotal,
        Launches = Launches,
        PagesVisited = PagesVisited,
        TabsClosed = TabsClosed,
        MemoryFreed = MemoryFreed
    });

    public static void SaveSession(IEnumerable<string> urls, int active) =>
        Write("session.json", new Session { Urls = urls.ToList(), Active = active });

    public static Session? LoadSession() => Read<Session>("session.json");

    public static void Record(string url, string title)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            url.StartsWith("nibble:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("zest:", StringComparison.OrdinalIgnoreCase)) return;

        lock (Gate)
        {
            History.RemoveAll(h => string.Equals(h.Url, url, StringComparison.OrdinalIgnoreCase));
            History.Insert(0, new HistoryEntry { Url = url, Title = title, Visited = DateTimeOffset.Now });
        }
    }

    public static bool IsBookmarked(string url) =>
        Bookmarks.Any(b => string.Equals(b.Url, url, StringComparison.OrdinalIgnoreCase));

    public static bool ToggleBookmark(string url, string title)
    {
        var existing = Bookmarks.FirstOrDefault(b => string.Equals(b.Url, url, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Bookmarks.Remove(existing);
            SaveBookmarks();
            return false;
        }

        Bookmarks.Insert(0, new Bookmark { Url = url, Title = title });
        SaveBookmarks();
        return true;
    }

    /// <summary>Fuzzy-ish prefix match over history for the omnibox dropdown.</summary>
    public static List<HistoryEntry> Suggest(string query, int max = 6)
    {
        query = query.Trim();
        if (query.Length == 0) return History.Take(max).ToList();

        var scored = new List<(int Score, HistoryEntry Entry)>();
        foreach (var h in History)
        {
            var score = ScoreMatch(h, query);
            if (score > 0) scored.Add((score, h));
            if (scored.Count > 220) break;
        }

        return scored
            .OrderByDescending(s => s.Score)
            .ThenByDescending(s => s.Entry.Visited)
            .Take(max)
            .Select(s => s.Entry)
            .ToList();
    }

    private static int ScoreMatch(HistoryEntry h, string query)
    {
        var host = string.Empty;
        try { host = new Uri(h.Url).Host; } catch { /* ignore */ }

        if (host.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 100 - host.Length;
        if (h.Title.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 80;
        if (host.Contains(query, StringComparison.OrdinalIgnoreCase)) return 60;
        if (h.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) return 40;
        if (h.Url.Contains(query, StringComparison.OrdinalIgnoreCase)) return 20;
        return 0;
    }

    private static T? Read<T>(string name) where T : class
    {
        try
        {
            var path = Path.Combine(DataDir, name);
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(text) ? null : JsonSerializer.Deserialize<T>(text, Json);
        }
        catch
        {
            return null;
        }
    }

    private static void Write<T>(string name, T value)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(Path.Combine(DataDir, name), JsonSerializer.Serialize(value, Json));
        }
        catch
        {
            // Persistence failures should never break browsing.
        }
    }

    private sealed class Stats
    {
        public long Blocked { get; set; }
        public long SavedBytes { get; set; }
        public int Launches { get; set; }
        public int PagesVisited { get; set; }
        public int TabsClosed { get; set; }
        public long MemoryFreed { get; set; }
    }
}

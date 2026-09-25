using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Nibble.Services;

/// <summary>What the update feed says about the newest release.</summary>
public sealed class UpdateRelease
{
    public string Tag { get; set; } = string.Empty;
    public Version Version { get; set; } = new(0, 0, 0);
    public string Notes { get; set; } = string.Empty;
    public string PageUrl { get; set; } = string.Empty;
    public string InstallerUrl { get; set; } = string.Empty;
    public string InstallerName { get; set; } = string.Empty;
    public long InstallerSize { get; set; }
}

/// <summary>
/// Keeps an installed Nibble current from its own releases.
///
/// The shape of it: check the release feed, download the installer the release carries, and
/// apply it at the *next* launch - not while the browser is running. Applying means running
/// the installer with /SELFUPDATE, which is the only thing that differs from a normal silent
/// install: the installer closes Nibble, replaces it, and starts it again with the new build
/// (see the [Run] entry and IsSelfUpdate() in installer/Nibble.iss). Nothing is written to the
/// profile, and a pending update is applied before any window exists, so nobody watches the
/// old version come up and get replaced a second later.
///
/// Honest limits, in the README as well: the download is trusted because it came from the
/// repository over TLS, and nothing here can prove it is *ours* - that needs a code-signing
/// certificate, which this build does not have yet. The size the feed reports is checked, and
/// a file that does not match is thrown away.
/// </summary>
public static class Updater
{
    /// <summary>The release feed. NIBBLE_UPDATE_FEED overrides it, which the tests use.</summary>
    public const string FeedUrl = "https://api.github.com/repos/DallenLarson/nibble/releases/latest";

    /// <summary>How long a check stays good for, so a browser start is not a network call.</summary>
    public static readonly TimeSpan CheckEvery = TimeSpan.FromHours(6);

    /// <summary>
    /// How long after a failed attempt another one is allowed. Without this, an installer that
    /// cannot run would be started on every launch, forever.
    /// </summary>
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(6);

    private const string AttemptMarker = "apply-attempt.json";

    public static Version Current { get; } = ReadCurrent();

    /// <summary>Where a downloaded installer waits, and where the status file lives.</summary>
    public static string Folder => Path.Combine(Store.DataDir, "updates");

    public static string StatusPath => Path.Combine(Folder, "last-check.json");

    public static string Feed
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("NIBBLE_UPDATE_FEED");
            return string.IsNullOrWhiteSpace(custom) ? FeedUrl : custom.Trim();
        }
    }

    private static Version ReadCurrent()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
    }

    /// <summary>True when the feed's version is newer than the one running.</summary>
    public static bool IsNewer(Version candidate) => candidate > Current;

    /// <summary>"v1.2.3" or "1.2.3" as a version, padded so 1.2 compares against 1.2.0.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var text = tag.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(text, out var parsed)) return null;
        return new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
    }

    /// <summary>
    /// Reads a release - GitHub's JSON shape, or a smaller file of the same shape for tests.
    /// The installer asset is the Nibble-&lt;version&gt;-Setup.exe the workflow attaches; if a
    /// release carries several, the newest wins.
    /// </summary>
    public static UpdateRelease? ParseRelease(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        var release = new UpdateRelease
        {
            Tag = Str(root, "tag_name") ?? Str(root, "tag") ?? string.Empty,
            Notes = Str(root, "body") ?? string.Empty,
            PageUrl = Str(root, "html_url") ?? Str(root, "page") ?? string.Empty
        };
        if (ParseTag(release.Tag) is { } version) release.Version = version;

        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = Str(asset, "name") ?? string.Empty;
                if (!name.StartsWith("Nibble-", StringComparison.OrdinalIgnoreCase) ||
                    !name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase)) continue;

                var candidate = ParseTag(name["Nibble-".Length..^"-Setup.exe".Length]);
                if (candidate is null) continue;
                if (release.InstallerName.Length > 0 && candidate <= release.Version) continue;

                release.Version = candidate;
                release.InstallerName = name;
                release.InstallerUrl = Str(asset, "browser_download_url") ?? Str(asset, "url") ?? string.Empty;
                release.InstallerSize = asset.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes)
                    ? bytes
                    : 0;
            }
        }

        return release.Tag.Length == 0 && release.InstallerName.Length == 0 ? null : release;
    }

    private static string? Str(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Asks the feed for the newest release. Null means "could not tell".</summary>
    public static async Task<UpdateRelease?> FetchAsync(CancellationToken token = default)
    {
        try
        {
            var json = await ReadAllTextAsync(Feed, token).ConfigureAwait(false);
            return json is null ? null : ParseRelease(json);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> ReadAllTextAsync(string url, CancellationToken token)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsFile)
            return File.ReadAllText(uri.LocalPath);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Nibble/{Current}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var response = await client.GetAsync(url, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads the release's installer next to the profile, so the next launch can apply it.
    /// Returns the file path, or null if it did not arrive intact.
    /// </summary>
    public static async Task<string?> DownloadAsync(UpdateRelease release, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(release.InstallerUrl) || string.IsNullOrWhiteSpace(release.InstallerName))
            return null;

        try
        {
            Directory.CreateDirectory(Folder);
            var target = Path.Combine(Folder, Path.GetFileName(release.InstallerName));
            if (File.Exists(target) && release.InstallerSize > 0 && new FileInfo(target).Length == release.InstallerSize)
                return target;

            var part = target + ".part";
            if (Uri.TryCreate(release.InstallerUrl, UriKind.Absolute, out var uri) && uri.IsFile)
            {
                File.Copy(uri.LocalPath, part, true);
            }
            else
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd($"Nibble/{Current}");
                using var response = await client.GetAsync(release.InstallerUrl, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var file = File.Create(part);
                await response.Content.CopyToAsync(file, token).ConfigureAwait(false);
            }

            var length = new FileInfo(part).Length;
            if (release.InstallerSize > 0 && length != release.InstallerSize)
            {
                File.Delete(part);
                return null;
            }
            if (length < 64 * 1024)      // an installer is megabytes; anything smaller is a page
            {
                File.Delete(part);
                return null;
            }

            File.Move(part, target, true);
            return target;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>A downloaded installer for something newer than what is running, if there is one.</summary>
    public static string? PendingInstaller(out Version? version)
    {
        version = null;
        try
        {
            if (!Directory.Exists(Folder)) return null;
            string? best = null;
            foreach (var file in Directory.EnumerateFiles(Folder, "Nibble-*-Setup.exe"))
            {
                // "Nibble-9.9.9-Setup.exe" -> "9.9.9". Cutting the extension first leaves
                // "9.9.9-Setup", which does not parse - and a file that does not parse is a
                // file CleanUp deletes, which is how a downloaded update once vanished before
                // it could be applied.
                var name = Path.GetFileName(file);
                if (!name.StartsWith("Nibble-", StringComparison.OrdinalIgnoreCase) ||
                    !name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase)) continue;
                var parsed = ParseTag(name["Nibble-".Length..^"-Setup.exe".Length]);
                if (parsed is null || !IsNewer(parsed)) continue;
                if (best is null || parsed > version) { best = file; version = parsed; }
            }

            if (best is null) return null;
            if (AttemptedRecently(version)) return null;
            return best;
        }
        catch
        {
            return null;
        }
    }

    private static bool AttemptedRecently(Version? version)
    {
        try
        {
            var marker = Path.Combine(Folder, AttemptMarker);
            if (!File.Exists(marker)) return false;
            using var document = JsonDocument.Parse(File.ReadAllText(marker));
            var root = document.RootElement;
            var when = root.TryGetProperty("at", out var at) && at.TryGetDateTimeOffset(out var parsed)
                ? parsed
                : DateTimeOffset.MinValue;
            var which = ParseTag(Str(root, "version"));
            if (which != version) return false;
            return DateTimeOffset.Now - when < RetryAfterFailure;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Runs the downloaded installer and gets out of its way. The installer closes this
    /// process if it has to, replaces the files, and starts Nibble again. The attempt is
    /// recorded first, so a copy that cannot install cannot put the browser in a loop.
    /// </summary>
    public static bool ApplyAndExit(string installer, Version? version)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, AttemptMarker),
                JsonSerializer.Serialize(new
                {
                    at = DateTimeOffset.Now,
                    version = version?.ToString() ?? string.Empty
                }));

            // Setup replaces Nibble.exe, so it must not run until this process is really gone.
            // Launching it alongside ourselves and exiting raced with it: Setup's Restart
            // Manager asks the app using those files to close, and it can only do that through
            // a window - a copy that is half exited has none, so a silent Setup gave up without
            // saying anything (measured: sometimes it installed, sometimes the uninstall entry
            // never moved). A two-second delay in a detached shell is the whole fix: by the time
            // Setup starts, nothing is holding the files.
            //
            // /LOG leaves Inno's own record in the profile as well - if an update ever fails on
            // somebody's machine, that file says why, and it is the only thing that can.
            var arguments = $"/c ping -n 3 127.0.0.1 > nul & \"{installer}\" " +
                            $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SELFUPDATE " +
                            $"/LOG=\"{Path.Combine(Folder, "install.log")}\"";
            Process.Start(new ProcessStartInfo("cmd.exe", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            Environment.Exit(0);
            return true;
        }
        catch (Exception ex)
        {
            Store.LogError($"could not start the downloaded update: {ex.Message}");
            return false;
        }
    }

    /// <summary>Drops installers that are not newer than what is running, and dead fragments.</summary>
    public static void CleanUp()
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            foreach (var file in Directory.EnumerateFiles(Folder))
            {
                var name = Path.GetFileName(file);
                if (name.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                {
                    if (DateTime.Now - File.GetLastWriteTime(file) > TimeSpan.FromHours(1)) TryDelete(file);
                    continue;
                }
                if (!name.StartsWith("Nibble-", StringComparison.OrdinalIgnoreCase) ||
                    !name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase)) continue;

                var parsed = ParseTag(name["Nibble-".Length..^"-Setup.exe".Length]);
                if (parsed is null || parsed <= Current) TryDelete(file);
            }
        }
        catch
        {
            // Housekeeping is never worth an exception.
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* still running from it, or in use */ }
    }

    /// <summary>What the last check found, for the menu and for --check-updates.</summary>
    public static void WriteStatus(bool enabled, string state, UpdateRelease? release = null,
        string? downloaded = null, string? error = null)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(StatusPath, JsonSerializer.Serialize(new
            {
                at = DateTimeOffset.Now,
                current = Current.ToString(),
                enabled,
                state,
                latest = release?.Version.ToString() ?? string.Empty,
                tag = release?.Tag ?? string.Empty,
                page = release?.PageUrl ?? string.Empty,
                installer = release?.InstallerName ?? string.Empty,
                downloaded = downloaded is not null,
                path = downloaded ?? string.Empty,
                error = error ?? string.Empty
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Diagnostics only.
        }
    }
}

using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Nibble.Services;

/// <summary>
/// Ships the built-in pages (new tab, error) out of embedded resources to a tiny
/// folder and serves them from a virtual host so they get a real origin.
/// </summary>
public static class Pages
{
    public const string Host = "nibble.pages";
    public const string NewTabUrl = "https://nibble.pages/newtab.html";

    /// <summary>
    /// Built-in pages are served straight off disk. (The runtime's virtual-host mapping
    /// proved unreliable here — navigations to it were aborted — and file URLs give the
    /// pages a real origin with no extra moving parts.)
    /// </summary>
    public static string NewTabPage => FileUrl("newtab.html");

    public static string ErrorUrl(string url, string message, string code) =>
        $"{FileUrl("error.html")}?u={Uri.EscapeDataString(url)}" +
        $"&m={Uri.EscapeDataString(message)}&c={Uri.EscapeDataString(code)}";

    public static bool IsNewTabPage(string uri) =>
        uri.Contains($"{Host}/newtab.html", StringComparison.OrdinalIgnoreCase) ||
        uri.Contains("newtab.html", StringComparison.OrdinalIgnoreCase) &&
        uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase);

    public static bool IsErrorPage(string uri) =>
        uri.Contains($"{Host}/error.html", StringComparison.OrdinalIgnoreCase) ||
        uri.Contains("error.html", StringComparison.OrdinalIgnoreCase) &&
        uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase);

    private static string FileUrl(string file) => new Uri(Path.Combine(Folder, file)).AbsoluteUri;

    public static string Folder => Path.Combine(Store.DataDir, "pages");

    private static string? _newTabHtml;
    private static string? _errorHtml;

    /// <summary>Built-in new tab page, ready to render inline if the virtual host fails.</summary>
    public static string NewTabHtml => _newTabHtml ??= Build("newtab.html");

    /// <summary>Error page with its details baked in, for the inline fallback path.</summary>
    public static string ErrorHtml(string url, string message, string code)
    {
        var html = _errorHtml ??= Build("error.html");
        var payload = JsonSerializer.Serialize(new { url, message, code });
        return html.Replace(
            "</body>",
            $"<script>window.__nibbleError = {payload};</script></body>",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Runs inside every page; forwards shortcuts the shell cares about.</summary>
    public const string BridgeScript = """
        (() => {
          const w = window.chrome && window.chrome.webview;
          if (!w) return;
          const post = (k, e) => w.postMessage({
            type: 'key', key: k,
            ctrl: e.ctrlKey, shift: e.shiftKey, alt: e.altKey
          });
          addEventListener('keydown', (e) => {
            const k = e.key;
            if (e.ctrlKey || e.metaKey || e.altKey || k === 'F5' || k === 'F6' || k === 'F11' || k === 'Escape') {
              post(k, e);
            }
          }, true);
          // Clicks land in the page's own window, so tell the shell about them: menus
          // opened over the content should close, exactly like in any other browser.
          //
          // A click on a link says more than that. Ctrl-click and the middle button mean
          // "open this, but leave me where I am", and the right button is the link menu,
          // which offers a window. The engine reports that something was opened, never how,
          // so the gesture is named here and the shell asks for it by name.
          const onLink = (e) => {
            const path = e.composedPath ? e.composedPath() : [e.target];
            for (const node of path) {
              if (node && node.tagName === 'A' && (node.href || node.getAttribute('href'))) return true;
            }
            return false;
          };
          addEventListener('mousedown', (e) => {
            w.postMessage({ type: 'click' });
            w.postMessage({
              type: 'gesture',
              gesture: onLink(e) && e.button === 2
                ? 'menu'
                : (e.button === 1 || e.ctrlKey || e.metaKey) ? 'background' : 'plain'
            });
          }, true);
          addEventListener('wheel', () => w.postMessage({ type: 'click' }), { capture: true, passive: true });
        })();
        """;

    public static void Ensure()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var newTab = Build("newtab.html");
            var error = Build("error.html");
            var mark = ReadEmbeddedBytes("nibble-mark.png");
            var wordmark = ReadEmbeddedBytes("nibble-wordmark.png");
            var themes = ThemeAssets();
            _newTabHtml = newTab;
            _errorHtml = error;

            // Stamp on the content itself so edits always reach the extracted pages.
            var version = typeof(Pages).Assembly.GetName().Version?.ToString() ?? "0";
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            [
                .. Encoding.UTF8.GetBytes(version + "\u0000" + newTab + "\u0000" + error),
                .. mark, .. wordmark,
                .. themes.SelectMany(t => Encoding.UTF8.GetBytes(t.Relative)).ToArray(),
                .. themes.SelectMany(t => t.Bytes).ToArray()
            ]));

            var stampPath = Path.Combine(Folder, "build.txt");
            if (File.Exists(stampPath) && File.ReadAllText(stampPath) == hash) return;

            File.WriteAllText(Path.Combine(Folder, "newtab.html"), newTab);
            File.WriteAllText(Path.Combine(Folder, "error.html"), error);
            // The page's own copy of the badge: a relative <img> beats a data URI in the
            // markup, and the browser serves it straight from this folder.
            if (mark.Length > 0) File.WriteAllBytes(Path.Combine(Folder, "mark.png"), mark);
            if (wordmark.Length > 0) File.WriteAllBytes(Path.Combine(Folder, "wordmark.png"), wordmark);

            // Themes: theme.json, the page's css/js, block textures and the font.
            foreach (var theme in themes)
            {
                var target = Path.Combine(Folder, "themes", theme.Relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, theme.Bytes);
            }

            File.WriteAllText(stampPath, hash);
        }
        catch
        {
            // If this fails the browser still works; new tabs just come back empty.
        }
    }

    private static string Build(string name)
    {
        var html = ReadEmbedded(name);
        return html
            .Replace("__FONT_REGULAR__", FontBase64("Silkscreen-Regular.ttf"), StringComparison.Ordinal)
            .Replace("__FONT_BOLD__", FontBase64("Silkscreen-Bold.ttf"), StringComparison.Ordinal);
    }

    private static string ReadEmbedded(string name)
    {
        var assembly = typeof(Pages).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("." + name, StringComparison.OrdinalIgnoreCase));
        if (resource is null) return string.Empty;
        using var stream = assembly.GetManifestResourceStream(resource);
        if (stream is null) return string.Empty;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static byte[] ReadEmbeddedBytes(string name)
    {
        try
        {
            // The brand images are WPF resources (XAML draws them), and this pulls the same
            // bytes out to sit next to the extracted pages. They used to be embedded a second
            // time as manifest resources for this call alone: 53,808 bytes of the bundle were
            // the same two PNGs twice, which is what kept the exe above 2.2 MB.
            var resource = System.Windows.Application.GetResourceStream(
                new Uri($"pack://application:,,,/Nibble;component/Assets/{name}", UriKind.Absolute));
            if (resource is null) return [];
            using var stream = resource.Stream;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Every file of every bundled theme, keyed by its path under themes/.</summary>
    private static List<(string Relative, byte[] Bytes)> ThemeAssets()
    {
        const string prefix = "nibble-theme/";
        var assets = new List<(string, byte[])>();
        try
        {
            var assembly = typeof(Pages).Assembly;
            foreach (var resource in assembly.GetManifestResourceNames())
            {
                if (!resource.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var relative = resource[prefix.Length..].Replace('\\', '/');
                using var stream = assembly.GetManifestResourceStream(resource);
                if (stream is null) continue;
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                assets.Add((relative, buffer.ToArray()));
            }
        }
        catch
        {
            // Without themes the browser still starts; the page simply has no overlay.
        }
        return assets;
    }

    private static string FontBase64(string file)
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/Assets/Fonts/{file}", UriKind.Absolute);
            using var stream = Application.GetResourceStream(uri)?.Stream;
            if (stream is null) return string.Empty;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return Convert.ToBase64String(buffer.ToArray());
        }
        catch
        {
            return string.Empty;
        }
    }

}

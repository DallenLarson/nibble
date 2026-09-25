using System.Text.RegularExpressions;

namespace Nibble.Services;

public static partial class Urls
{
    public const string Home = "nibble://newtab";

    public sealed record Engine(string Id, string Name, string Query, string Host, string Blurb);

    public static readonly Engine[] Engines =
    [
        new("google", "Google", "https://www.google.com/search?q={0}", "google.com",
            "The classic, with the fastest results"),
        new("duckduckgo", "DuckDuckGo", "https://duckduckgo.com/?q={0}", "duckduckgo.com",
            "Private by default, nothing tracked"),
        new("brave", "Brave", "https://search.brave.com/search?q={0}", "search.brave.com",
            "Independent index, privacy first"),
        new("bing", "Bing", "https://www.bing.com/search?q={0}", "bing.com",
            "Microsoft's engine, strong on images")
    ];

    public static Engine EngineFor(string id) =>
        Engines.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? Engines[0];

    /// <summary>True when the text should be treated as a web address rather than a search.</summary>
    public static bool LooksLikeAddress(string input)
    {
        input = input.Trim();
        if (input.Length == 0 || input.Contains(' ')) return false;
        if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return true;
        if (input.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return true;
        if (input.StartsWith("nibble://", StringComparison.OrdinalIgnoreCase)) return true;
        if (input.StartsWith("zest://", StringComparison.OrdinalIgnoreCase)) return true;
        if (input.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (input.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return true;
        if (input.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return true;
        return DomainLike().IsMatch(input);
    }

    /// <summary>Turns omnibox text into a navigable target.</summary>
    public static string Resolve(string input, string engineId)
    {
        input = input.Trim();
        if (input.Length == 0) return Home;
        if (input.StartsWith("nibble://", StringComparison.OrdinalIgnoreCase)) return input;
        if (input.StartsWith("zest://", StringComparison.OrdinalIgnoreCase)) return Home; // legacy bookmark/session
        if (input.Contains("://", StringComparison.Ordinal)) return input;

        if (LooksLikeAddress(input))
        {
            if (input.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase))
                return "http://" + input;
            return "https://" + input.TrimStart('/');
        }

        var engine = EngineFor(engineId);
        return string.Format(engine.Query, Uri.EscapeDataString(input));
    }

    /// <summary>Short, human-friendly label for a URL (used on tabs and tiles).</summary>
    public static string PrettyHost(string url)
    {
        if (url.StartsWith("nibble://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("zest://", StringComparison.OrdinalIgnoreCase)) return "New tab";
        try
        {
            var uri = new Uri(url);
            return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        }
        catch
        {
            return url;
        }
    }

    public static string FormatBytes(double bytes)
    {
        if (bytes >= 1024 * 1024 * 1024) return $"{bytes / (1024 * 1024 * 1024):0.#} GB";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024 * 1024):0.#} MB";
        if (bytes >= 1024) return $"{bytes / 1024:0.#} KB";
        return $"{bytes:0} B";
    }

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9\-\.]*\.[a-zA-Z]{2,}(:\d+)?(/.*)?$")]
    private static partial Regex DomainLike();
}

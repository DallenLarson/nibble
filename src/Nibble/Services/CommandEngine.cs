using Nibble.Controls;

namespace Nibble.Services;

public enum ResultKind
{
    Command,
    Tab,
    History,
    Bookmark,
    Closed,
    Web,
    Calculator,
    Conversion
}

/// <summary>One row in the command bar.</summary>
public sealed record CommandResult(
    ResultKind Kind,
    string Title,
    string Subtitle,
    string Note,
    string Icon,
    string Payload,
    object? Tag = null);

/// <summary>A thing the browser can do, shown by name in the command bar.</summary>
public sealed record BrowserCommand(string Name, string Hint, string Icon, Action Run, params string[] Keywords);

public sealed class CommandInputs
{
    public required IReadOnlyList<(string Title, string Url, object Tag)> Tabs { get; init; }
    public required IReadOnlyList<HistoryEntry> History { get; init; }
    public required IReadOnlyList<Bookmark> Bookmarks { get; init; }
    public required IReadOnlyList<(string Title, string Url)> RecentlyClosed { get; init; }
    public required IReadOnlyList<BrowserCommand> Commands { get; init; }
    public required string SearchEngineId { get; init; }
}

/// <summary>
/// The whole command bar in one place: scoped searches (<c>@tabs</c>, <c>@history</c>,
/// <c>@bookmarks</c>, <c>@closed</c>), the command list (<c>&gt;</c>), arithmetic,
/// unit conversion, and a web search as the fallback.
///
/// Pure logic - no UI - so it can be unit tested from work/tools/CommandTests.
/// </summary>
public static class CommandEngine
{
    public const int DefaultMax = 9;

    public static List<CommandResult> Query(string raw, CommandInputs input, int max = DefaultMax)
    {
        var text = (raw ?? string.Empty).Trim();
        var results = new List<CommandResult>();

        // ---- explicit scopes -------------------------------------------------------
        if (text.StartsWith('@'))
        {
            var body = text[1..];
            var split = body.IndexOf(' ');
            var scope = (split < 0 ? body : body[..split]).ToLowerInvariant();
            var rest = split < 0 ? string.Empty : body[(split + 1)..].Trim();

            switch (scope)
            {
                case "tab" or "tabs":
                    return SearchTabs(rest, input, max);
                case "history" or "hist":
                    return SearchHistory(rest, input, max);
                case "bookmark" or "bookmarks" or "bm":
                    return SearchBookmarks(rest, input, max);
                case "closed":
                    return SearchClosed(rest, input, max);
            }
        }

        if (text.StartsWith('>'))
            return SearchCommands(text[1..].Trim(), input, max);

        // ---- calculator and unit conversion ---------------------------------------
        if (text.Length > 0 && Calculator.TryConvert(text, out var converted))
        {
            results.Add(new CommandResult(ResultKind.Conversion, text, "unit conversion",
                converted, Icons.Ruler, converted));
        }
        else if (text.Length > 0 && Calculator.TryEvaluate(text, out var value))
        {
            var formatted = Calculator.Format(value);
            results.Add(new CommandResult(ResultKind.Calculator, text, "calculator",
                formatted, Icons.Calculator, formatted));
        }

        // ---- everything else, blended ---------------------------------------------
        if (text.Length == 0)
        {
            results.AddRange(SearchCommands(string.Empty, input, max));
            return results;
        }

        results.AddRange(SearchCommands(text, input, 4));
        results.AddRange(SearchTabs(text, input, 3));
        results.AddRange(SearchHistory(text, input, 3));
        results.AddRange(SearchBookmarks(text, input, 2));
        results.AddRange(SearchClosed(text, input, 2));

        results.Add(new CommandResult(ResultKind.Web,
            $"Search {Urls.EngineFor(input.SearchEngineId).Name} for \u201c{text}\u201d", text + "  \u00b7  web search",
            "web", Icons.Search, Urls.Resolve(text, input.SearchEngineId)));

        return results.Take(max).ToList();
    }

    // =====================================================================
    //  scopes
    // =====================================================================

    private static List<CommandResult> SearchTabs(string text, CommandInputs input, int max) =>
        input.Tabs
            .Select(tab => (tab, score: Score(text, tab.Title, tab.Url)))
            .Where(pair => pair.score > 0)
            .OrderByDescending(pair => pair.score)
            .Take(max)
            .Select(pair => new CommandResult(
                ResultKind.Tab,
                pair.tab.Title,
                Urls.PrettyHost(pair.tab.Url),
                "switch",
                Icons.Globe,
                pair.tab.Url,
                pair.tab.Tag))
            .ToList();

    private static List<CommandResult> SearchHistory(string text, CommandInputs input, int max) =>
        input.History
            .Select(entry => (entry, score: Score(text, entry.Title, entry.Url)))
            .Where(pair => pair.score > 0)
            .OrderByDescending(pair => pair.score)
            .Take(max)
            .Select(pair => new CommandResult(
                ResultKind.History,
                string.IsNullOrWhiteSpace(pair.entry.Title) ? Urls.PrettyHost(pair.entry.Url) : pair.entry.Title,
                Urls.PrettyHost(pair.entry.Url),
                "history",
                Icons.Clock,
                pair.entry.Url))
            .ToList();

    private static List<CommandResult> SearchBookmarks(string text, CommandInputs input, int max) =>
        input.Bookmarks
            .Select(entry => (entry, score: Score(text, entry.Title, entry.Url)))
            .Where(pair => pair.score > 0)
            .OrderByDescending(pair => pair.score)
            .Take(max)
            .Select(pair => new CommandResult(
                ResultKind.Bookmark,
                string.IsNullOrWhiteSpace(pair.entry.Title) ? Urls.PrettyHost(pair.entry.Url) : pair.entry.Title,
                Urls.PrettyHost(pair.entry.Url),
                "bookmark",
                Icons.Star,
                pair.entry.Url))
            .ToList();

    private static List<CommandResult> SearchClosed(string text, CommandInputs input, int max) =>
        input.RecentlyClosed
            .Select(entry => (entry, score: Score(text, entry.Title, entry.Url)))
            .Where(pair => pair.score > 0)
            .OrderByDescending(pair => pair.score)
            .Take(max)
            .Select(pair => new CommandResult(
                ResultKind.Closed,
                string.IsNullOrWhiteSpace(pair.entry.Title) ? Urls.PrettyHost(pair.entry.Url) : pair.entry.Title,
                Urls.PrettyHost(pair.entry.Url),
                "reopen",
                Icons.Retry,
                pair.entry.Url))
            .ToList();

    private static List<CommandResult> SearchCommands(string text, CommandInputs input, int max) =>
        input.Commands
            .Select(command => (command, score: Score(text, command.Name, string.Join(' ', command.Keywords))))
            .Where(pair => pair.score > 0)
            .OrderByDescending(pair => pair.score)
            .Take(max)
            .Select(pair => new CommandResult(
                ResultKind.Command,
                pair.command.Name,
                string.Empty,
                pair.command.Hint,
                pair.command.Icon,
                pair.command.Name,
                pair.command))
            .ToList();

    // =====================================================================
    //  matching: prefix beats word-start beats contains beats subsequence
    // =====================================================================

    public static int Score(string query, params string[] candidates)
    {
        if (string.IsNullOrWhiteSpace(query)) return 1;
        var best = 0;

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            var text = candidate.ToLowerInvariant();
            var needle = query.ToLowerInvariant();

            if (text.Equals(needle, StringComparison.Ordinal)) best = Math.Max(best, 1000);
            else if (text.StartsWith(needle, StringComparison.Ordinal)) best = Math.Max(best, 900 - text.Length);
            else if (text.Contains(" " + needle, StringComparison.Ordinal)) best = Math.Max(best, 800 - text.Length);
            else if (text.Contains(needle, StringComparison.Ordinal)) best = Math.Max(best, 600 - text.Length);
            else if (IsSubsequence(needle, text)) best = Math.Max(best, 300 - text.Length);
        }

        return best;
    }

    private static bool IsSubsequence(string needle, string haystack)
    {
        var i = 0;
        foreach (var c in haystack)
        {
            if (i < needle.Length && needle[i] == c) i++;
        }
        return i == needle.Length;
    }
}

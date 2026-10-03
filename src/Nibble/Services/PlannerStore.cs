using System.IO;
using System.Globalization;
using System.Text.Json;

namespace Nibble.Services;

public sealed record PlannerNote(string Id, string Title, string Body, DateTimeOffset Updated);
public sealed record PlannerEvent(string Id, string Title, string Start, string End, string Details);
public sealed record PlannerData(List<PlannerNote> Notes, List<PlannerEvent> Events);

/// <summary>Item-level updates avoid overwriting unrelated edits from another tab.</summary>
public sealed class PlannerStore(string? path)
{
    private PlannerData? _data;
    public static PlannerStore Shared { get; } = new(Path.Combine(Store.DataDir, "planner.json"));

    public PlannerData Read()
    {
        if (_data is null)
        {
            // Do not overwrite an unreadable file with an empty notebook.
            _data = path is not null && File.Exists(path)
                ? JsonSerializer.Deserialize<PlannerData>(File.ReadAllText(path))
                    ?? throw new InvalidDataException("The notes and calendar file could not be read.")
                : new([], []);
            if (_data.Notes is null || _data.Events is null)
            {
                _data = null;
                throw new InvalidDataException("The notes and calendar file is invalid.");
            }
        }
        return new([.. _data.Notes], [.. _data.Events]);
    }

    public PlannerData Apply(string action, JsonElement item)
    {
        var next = Read();
        string Text(string key, int limit) => item.TryGetProperty(key, out var v) &&
            v.ValueKind == JsonValueKind.String && v.GetString() is { } text && text.Length <= limit
                ? text : throw new ArgumentException($"Invalid {key}.");
        var id = Text("id", 64);
        if (!Guid.TryParse(id, out _)) throw new ArgumentException("Invalid item ID.");
        switch (action)
        {
            case "save-note":
                var note = new PlannerNote(id, Text("title", 160), Text("body", 100_000), DateTimeOffset.Now);
                next.Notes.RemoveAll(n => n.Id == id);
                next.Notes.Add(note);
                break;
            case "delete-note": next.Notes.RemoveAll(n => n.Id == id); break;
            case "save-event":
                var title = Text("title", 160).Trim();
                var start = Text("start", 16);
                var end = Text("end", 16);
                if (title.Length == 0 || !ParseTime(start, out var from) || !ParseTime(end, out var to) || to <= from)
                    throw new ArgumentException("Choose a title and an end time after the start time.");
                var entry = new PlannerEvent(id, title, start, end, Text("details", 10_000));
                next.Events.RemoveAll(e => e.Id == id);
                next.Events.Add(entry);
                break;
            case "delete-event": next.Events.RemoveAll(e => e.Id == id); break;
            default: throw new ArgumentException("Unknown planner action.");
        }
        if (next.Notes.Count > 1000 || next.Events.Count > 5000)
            throw new ArgumentException("This planner is full. Remove an unused item first.");
        if (path is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(next));
            File.Move(temporary, path, overwrite: true);
        }
        _data = next;
        return Read();
    }

    private static bool ParseTime(string value, out DateTime time) => DateTime.TryParseExact(
        value, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}

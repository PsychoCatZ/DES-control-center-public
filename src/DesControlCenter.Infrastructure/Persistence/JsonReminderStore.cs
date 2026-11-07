using System.Text.Json;
using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Models;

namespace DesControlCenter.Infrastructure.Persistence;

/// <summary>
/// <c>%AppData%\DesControlCenter\reminders.json</c>: a JSON array of
/// <c>{ "Id", "At": "2026-10-16T10:00:00+07:00", "Text", "SentAt" }</c>. Meant to be edited by hand
/// (or by an assistant) — so a missing file means "no reminders" but an unreadable one means
/// "hands off" (Load returns null), never overwritten.
/// </summary>
public sealed class JsonReminderStore : IReminderStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _path;

    public JsonReminderStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DesControlCenter", "reminders.json");
    }

    public IReadOnlyList<Reminder>? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return Array.Empty<Reminder>();
            }

            return JsonSerializer.Deserialize<List<Reminder>>(File.ReadAllText(_path), Options) ?? new List<Reminder>();
        }
        catch
        {
            return null;
        }
    }

    public void Save(IReadOnlyList<Reminder> reminders)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(reminders, Options), new System.Text.UTF8Encoding(false));
        File.Move(temp, _path, overwrite: true);
    }
}

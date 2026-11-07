using System.Text.Json;
using DesControlCenter.Core.Abstractions;

namespace DesControlCenter.Infrastructure.Persistence;

/// <summary>When the weekly report last went out — <c>%AppData%\DesControlCenter\weekly-report-state.json</c>.</summary>
public sealed class JsonReportStateStore : IReportStateStore
{
    private readonly string _path;

    public JsonReportStateStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DesControlCenter", "weekly-report-state.json");
    }

    public DateTimeOffset? LoadLastSent()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(_path));
            return doc.RootElement.TryGetProperty("LastSent", out var v) && v.TryGetDateTimeOffset(out var at) ? at : null;
        }
        catch
        {
            return null;
        }
    }

    public void SaveLastSent(DateTimeOffset sentAt)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(new { LastSent = sentAt }));
    }
}

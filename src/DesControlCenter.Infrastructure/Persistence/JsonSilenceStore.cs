using System.Text.Json;
using System.Text.Json.Serialization;
using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Models;

namespace DesControlCenter.Infrastructure.Persistence;

/// <summary>Silences in %AppData%\DesControlCenter\silences.json. A missing or unreadable file
/// simply means "nothing silenced" — never a reason to stop the app from starting.</summary>
public sealed class JsonSilenceStore : ISilenceStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;

    public JsonSilenceStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DesControlCenter", "silences.json");
    }

    public IReadOnlyList<Silence> Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return Array.Empty<Silence>();
            }

            return JsonSerializer.Deserialize<List<Silence>>(File.ReadAllText(_path), Options) ?? new List<Silence>();
        }
        catch
        {
            return Array.Empty<Silence>();
        }
    }

    public void Save(IReadOnlyCollection<Silence> silences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(silences, Options));
        File.Move(temp, _path, overwrite: true);
    }
}

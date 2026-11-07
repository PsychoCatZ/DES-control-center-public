using DesControlCenter.Core.Models;

namespace DesControlCenter.Core.Abstractions;

/// <summary>Where silences survive an app restart. Deliberately separate from the config file:
/// the settings window saves the whole config, and must not overwrite silences set meanwhile.</summary>
public interface ISilenceStore
{
    IReadOnlyList<Silence> Load();
    void Save(IReadOnlyCollection<Silence> silences);
}

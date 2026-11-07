using DesControlCenter.Infrastructure.Persistence;
using Xunit;

namespace DesControlCenter.Core.Tests;

public class UiSettingsTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), "des-ui-" + Guid.NewGuid().ToString("N"), "ui-settings.json");

    [Fact]
    public void NothingSaved_MeansTheOriginalDarkLook()
    {
        Assert.Equal(ThemeMode.Dark, new JsonUiSettingsStore(TempFile()).LoadTheme());
    }

    [Theory]
    [InlineData(ThemeMode.Dark)]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.System)]
    public void TheChoice_SurvivesARestart(ThemeMode mode)
    {
        var path = TempFile();
        try
        {
            new JsonUiSettingsStore(path).SaveTheme(mode);

            Assert.Equal(mode, new JsonUiSettingsStore(path).LoadTheme());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void AGarbledFile_FallsBackToDark_InsteadOfFailing()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(path, "{ this is not json");
            Assert.Equal(ThemeMode.Dark, new JsonUiSettingsStore(path).LoadTheme());

            File.WriteAllText(path, "{ \"Theme\": \"Purple\" }");
            Assert.Equal(ThemeMode.Dark, new JsonUiSettingsStore(path).LoadTheme());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}

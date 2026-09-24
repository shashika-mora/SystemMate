using SystemMate.Services;
using Xunit;

namespace SystemMate.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"sm_settings_{Guid.NewGuid():N}.ini");

    [Fact]
    public void InvalidValues_UseDefaults()
    {
        File.WriteAllText(_path, "ConfirmBeforeDelete=not-a-bool\nShowFileDetails=1\nTheme=invalid\n");
        var settings = new SettingsService(_path);

        Assert.True(settings.GetConfirmBeforeDelete());
        Assert.True(settings.GetShowFileDetails());
        Assert.Equal("Default (System)", settings.GetTheme());
    }

    [Fact]
    public void ThemeChanges_RaiseEventAndPersistAtomically()
    {
        var settings = new SettingsService(_path);
        var changed = 0;
        settings.ThemeChanged += (_, _) => changed++;

        settings.SetTheme("Dark");

        Assert.Equal("Dark", settings.GetTheme());
        Assert.Equal(1, changed);
        Assert.Contains("Theme=Dark", File.ReadAllText(_path));
    }

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }
}

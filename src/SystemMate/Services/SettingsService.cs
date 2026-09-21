using Microsoft.UI.Xaml;

namespace SystemMate.Services;

/// <summary>Persists user preferences to %LOCALAPPDATA%\SystemMate\settings.ini</summary>
public sealed class SettingsService
{
    private readonly string _settingsPath;
    private readonly Dictionary<string, string> _values = new();

    public SettingsService()
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SystemMate");
        Directory.CreateDirectory(dataDir);
        _settingsPath = Path.Combine(dataDir, "settings.ini");
        Load();
    }

    private void Load()
    {
        if (!File.Exists(_settingsPath)) return;
        foreach (var line in File.ReadAllLines(_settingsPath))
        {
            var idx = line.IndexOf('=');
            if (idx <= 0) continue;
            _values[line[..idx].Trim()] = line[(idx + 1)..].Trim();
        }
    }

    private void Save()
    {
        File.WriteAllLines(_settingsPath,
            _values.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    public string GetTheme() => _values.GetValueOrDefault("Theme", "Default");
    public void SetTheme(string theme) { _values["Theme"] = theme; Save(); }

    public bool GetConfirmBeforeDelete() =>
        bool.Parse(_values.GetValueOrDefault("ConfirmBeforeDelete", "true"));
    public void SetConfirmBeforeDelete(bool value) { _values["ConfirmBeforeDelete"] = value.ToString(); Save(); }

    public bool GetShowFileDetails() =>
        bool.Parse(_values.GetValueOrDefault("ShowFileDetails", "true"));
    public void SetShowFileDetails(bool value) { _values["ShowFileDetails"] = value.ToString(); Save(); }
}

// No WinUI dependency — settings persistence is pure .NET

namespace SystemMate.Services;

/// <summary>Persists user preferences to %LOCALAPPDATA%\SystemMate\settings.ini</summary>
public sealed class SettingsService
{
    private readonly string _settingsPath;
    private readonly Dictionary<string, string> _values = new();

    public SettingsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SystemMate",
            "settings.ini"))
    {
    }

    public SettingsService(string settingsPath)
    {
        _settingsPath = settingsPath;
        var dataDir = Path.GetDirectoryName(_settingsPath);
        if (string.IsNullOrWhiteSpace(dataDir))
            throw new ArgumentException("Settings path must include a directory.", nameof(settingsPath));

        Directory.CreateDirectory(dataDir);
        Load();
    }

    private void Load()
    {
        if (!File.Exists(_settingsPath)) return;
        foreach (var line in File.ReadLines(_settingsPath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
            var idx = line.IndexOf('=');
            if (idx <= 0) continue;
            _values[line[..idx].Trim()] = line[(idx + 1)..].Trim();
        }
    }

    private void Save()
    {
        var temporaryPath = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllLines(temporaryPath, _values.Select(kv => $"{kv.Key}={kv.Value}"));
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public event EventHandler? ThemeChanged;

    public string GetTheme() => NormalizeTheme(_values.GetValueOrDefault("Theme", "Default (System)"));
    public void SetTheme(string theme)
    {
        var normalized = NormalizeTheme(theme);
        _values["Theme"] = normalized;
        Save();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool GetConfirmBeforeDelete() => GetBool("ConfirmBeforeDelete", true);
    public void SetConfirmBeforeDelete(bool value) { _values["ConfirmBeforeDelete"] = value.ToString(); Save(); }

    public bool GetShowFileDetails() => GetBool("ShowFileDetails", true);
    public void SetShowFileDetails(bool value) { _values["ShowFileDetails"] = value.ToString(); Save(); }

    private bool GetBool(string key, bool defaultValue)
        => bool.TryParse(_values.GetValueOrDefault(key), out var value) ? value : defaultValue;

    private static string NormalizeTheme(string theme)
        => theme switch
        {
            "Light" => "Light",
            "Dark" => "Dark",
            _ => "Default (System)"
        };
}

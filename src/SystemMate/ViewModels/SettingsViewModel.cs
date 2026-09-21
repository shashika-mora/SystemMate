using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemMate.Services;

namespace SystemMate.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;

    [ObservableProperty] private string _selectedTheme;
    [ObservableProperty] private bool _confirmBeforeDelete;
    [ObservableProperty] private bool _showFileDetails;

    public string[] Themes { get; } = ["Default (System)", "Light", "Dark"];

    public SettingsViewModel(SettingsService settings)
    {
        _settings = settings;
        _selectedTheme      = _settings.GetTheme();
        _confirmBeforeDelete = _settings.GetConfirmBeforeDelete();
        _showFileDetails     = _settings.GetShowFileDetails();
    }

    partial void OnSelectedThemeChanged(string value) => _settings.SetTheme(value);
    partial void OnConfirmBeforeDeleteChanged(bool value) => _settings.SetConfirmBeforeDelete(value);
    partial void OnShowFileDetailsChanged(bool value) => _settings.SetShowFileDetails(value);

    [RelayCommand]
    private static void OpenDataFolder()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SystemMate");
        System.Diagnostics.Process.Start("explorer.exe", path);
    }

    public string AppVersion => "0.1.0";
    public string DotNetVersion => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
}

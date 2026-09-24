using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using SystemMate.Models;
using SystemMate.Services;

namespace SystemMate.ViewModels;

public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly HistoryService _history;
    private readonly SettingsService _settings;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private bool _showFileDetails;

    public ObservableCollection<CleanupSession> Sessions { get; } = new();
    public Func<Task<bool>>? ConfirmClearAsync { get; set; }

    public HistoryViewModel(HistoryService history, SettingsService settings)
    {
        _history = history;
        _settings = settings;
        _showFileDetails = settings.GetShowFileDetails();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        Sessions.Clear();
        StatusMessage = "Loading history…";

        try
        {
            var sessions = await _history.GetSessionsAsync();
            foreach (var s in sessions)
            {
                s.ShowFileDetails = ShowFileDetails;
                Sessions.Add(s);
            }

            IsEmpty = Sessions.Count == 0;
            StatusMessage = Sessions.Count == 0
                ? "No cleanup history yet. Run a scan to get started."
                : $"{Sessions.Count} session{(Sessions.Count == 1 ? "" : "s")} found.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load history: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        try
        {
            var downloadsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var fileName = $"SystemMate_History_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            var fullPath = Path.Combine(downloadsPath, fileName);
            await _history.ExportToCsvAsync(fullPath);
            StatusMessage = $"Exported to {fullPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        if (_settings.GetConfirmBeforeDelete() &&
            ConfirmClearAsync is not null &&
            !await ConfirmClearAsync())
            return;

        try
        {
            await _history.ClearHistoryAsync();
            Sessions.Clear();
            IsEmpty = true;
            StatusMessage = "History cleared.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to clear history: {ex.Message}";
        }
    }
}

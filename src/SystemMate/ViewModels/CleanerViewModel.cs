using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using SystemMate.Models;
using SystemMate.Services;

namespace SystemMate.ViewModels;

public sealed partial class CleanerViewModel : ObservableObject
{
    private readonly CleanerService _cleaner;
    private ScanResult? _lastScan;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCleanNow))]
    private bool _isScanning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCleanNow))]
    private bool _isCleaning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCleanNow))]
    private bool _hasScanResult;

    [ObservableProperty] private string _statusMessage = "Ready to scan. SystemMate never removes anything silently.";
    [ObservableProperty] private string _totalSizeDisplay = "0 MB";
    [ObservableProperty] private int _totalFileCount;
    [ObservableProperty] private string _progressFile = string.Empty;
    [ObservableProperty] private int _progressDone;
    [ObservableProperty] private int _progressTotal;

    public bool CanCleanNow => CanClean();

    public ObservableCollection<CleanupCategory> Categories { get; } = new();

    public CleanerViewModel(CleanerService cleaner)
    {
        _cleaner = cleaner;
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        IsScanning = true;
        HasScanResult = false;
        Categories.Clear();
        StatusMessage = "Scanning…";

        try
        {
            var progress = new Progress<string>(msg => StatusMessage = msg);
            _lastScan = await _cleaner.ScanAsync(progress);

            foreach (var cat in _lastScan.Categories)
                Categories.Add(cat);

            UpdateTotals();
            HasScanResult = true;
            StatusMessage = $"Scan complete — {_lastScan.TotalSizeDisplay} found across {_lastScan.TotalFileCount:N0} files.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private bool CanScan() => !IsScanning && !IsCleaning;

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        if (_lastScan is null) return;

        IsCleaning = true;
        ProgressDone = 0;
        ProgressTotal = _lastScan.TotalFileCount;
        StatusMessage = "Cleaning…";

        try
        {
            var progress = new Progress<(string file, int done, int total)>(p =>
            {
                ProgressFile  = p.file;
                ProgressDone  = p.done;
                ProgressTotal = p.total;
            });

            var session = await _cleaner.CleanAsync(_lastScan, progress);

            StatusMessage = $"Done! Freed {session.TotalSizeDisplay}. " +
                            $"{session.FilesDeleted} deleted, " +
                            $"{session.FilesSkipped} skipped, " +
                            $"{session.FilesErrored} errors.";
            HasScanResult = false;
            Categories.Clear();
            _lastScan = null;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Clean failed: {ex.Message}";
        }
        finally
        {
            IsCleaning = false;
        }
    }

    private bool CanClean() => HasScanResult && !IsScanning && !IsCleaning
                               && Categories.Any(c => c.IsSelected && c.FileCount > 0);

    public void UpdateTotals()
    {
        if (_lastScan is null) return;
        TotalSizeDisplay = _lastScan.TotalSizeDisplay;
        TotalFileCount   = _lastScan.TotalFileCount;
    }
}

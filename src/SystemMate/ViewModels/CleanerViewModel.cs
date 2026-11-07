using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using SystemMate.Models;
using SystemMate.Services;

namespace SystemMate.ViewModels;

public sealed partial class CleanerViewModel : ObservableObject
{
    private readonly CleanerService _cleaner;
    private ScanResult? _lastScan;
    private CancellationTokenSource? _operationCancellation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCleanNow))]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(CleanCommand), nameof(CancelOperationCommand))]
    private bool _isScanning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCleanNow))]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(CleanCommand), nameof(CancelOperationCommand))]
    private bool _isCleaning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCleanNow))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
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
        ClearCategories();
        _lastScan = null;
        _operationCancellation = new CancellationTokenSource();
        StatusMessage = "Scanning…";

        try
        {
            var progress = new Progress<string>(msg => { if (IsScanning) StatusMessage = msg; });
            _lastScan = await _cleaner.ScanAsync(progress, _operationCancellation.Token);

            foreach (var cat in _lastScan.Categories)
            {
                cat.PropertyChanged += CategorySelectionChanged;
                Categories.Add(cat);
            }

            UpdateTotals();
            HasScanResult = true;
            StatusMessage = $"Scan complete — {_lastScan.TotalSizeDisplay} found across {_lastScan.TotalFileCount:N0} files. Review the categories before cleaning.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Scan stopped. No files were changed.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            _operationCancellation?.Dispose();
            _operationCancellation = null;
        }
    }

    private bool CanScan() => !IsScanning && !IsCleaning;

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        if (_lastScan is null) return;

        IsCleaning = true;
        _operationCancellation = new CancellationTokenSource();
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

            var session = await _cleaner.CleanAsync(_lastScan, progress, _operationCancellation.Token);

            StatusMessage = $"{(session.WasCanceled ? "Cleanup stopped" : "Cleanup complete")}. Freed {session.TotalSizeDisplay}. " +
                            $"{session.FilesDeleted} deleted, " +
                            $"{session.FilesSkipped} skipped, " +
                            $"{session.FilesErrored} errors.";
            HasScanResult = false;
            ClearCategories();
            _lastScan = null;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Cleanup stopped: {ex.Message} Check History for recorded attempts, then scan again.";
            HasScanResult = false;
            ClearCategories();
            _lastScan = null;
        }
        finally
        {
            IsCleaning = false;
            _operationCancellation?.Dispose();
            _operationCancellation = null;
        }
    }

    private bool CanClean() => HasScanResult && !IsScanning && !IsCleaning
                               && Categories.Any(c => c.IsSelected && c.FileCount > 0);

    public void UpdateTotals()
    {
        if (_lastScan is null) return;
        TotalSizeDisplay = _lastScan.TotalSizeDisplay;
        TotalFileCount   = _lastScan.TotalFileCount;
        OnPropertyChanged(nameof(CanCleanNow));
        CleanCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelOperation() => _operationCancellation?.Cancel();

    private bool CanCancel() => IsScanning || IsCleaning;

    private void CategorySelectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(CleanupCategory.IsSelected)) UpdateTotals();
    }

    private void ClearCategories()
    {
        foreach (var category in Categories) category.PropertyChanged -= CategorySelectionChanged;
        Categories.Clear();
    }
}

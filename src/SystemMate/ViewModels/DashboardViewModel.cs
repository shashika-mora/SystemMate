using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SystemMate.Models;
using SystemMate.Services;

namespace SystemMate.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly SystemInfoService _sysInfo;
    private DispatcherQueueTimer? _refreshTimer;
    private bool _disposed;

    [ObservableProperty] private double _cpuUsage;
    [ObservableProperty] private string _ramDisplay = "—";
    [ObservableProperty] private string _storageDisplay = "—";
    [ObservableProperty] private string _bootTimeDisplay = "—";
    [ObservableProperty] private string _osVersion = "—";
    [ObservableProperty] private string _machineName = "—";
    [ObservableProperty] private string _cpuName = "—";
    [ObservableProperty] private string _diskHealth = "—";
    [ObservableProperty] private double _ramPercent;
    [ObservableProperty] private double _diskPercent;

    [ObservableProperty] private bool _isLoading = true;

    public DashboardViewModel(SystemInfoService sysInfo)
    {
        _sysInfo = sysInfo;
    }

    public void StartMonitoring(DispatcherQueue dispatcher)
    {
        _refreshTimer = dispatcher.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(2);
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();

        // Immediate first load
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var snap = await _sysInfo.CollectAsync();
            UpdateFromSnapshot(snap);
            IsLoading = false;
        }
        catch { /* Don't crash on transient WMI errors */ }
    }

    private void UpdateFromSnapshot(SystemSnapshot snap)
    {
        CpuUsage       = snap.CpuUsagePercent;
        RamPercent     = snap.RamUsagePercent;
        DiskPercent    = snap.DiskUsagePercent;
        BootTimeDisplay = snap.BootTimeDisplay;
        OsVersion      = snap.OsVersion;
        MachineName    = snap.MachineName;
        CpuName        = snap.CpuName.Length > 45
            ? snap.CpuName[..42] + "…"
            : snap.CpuName;
        DiskHealth     = snap.DiskHealthStatus ?? "Unavailable";

        // RAM: "7.1 / 16 GB"
        var usedGb  = snap.RamUsedBytes  / 1_073_741_824.0;
        var totalGb = snap.RamTotalBytes / 1_073_741_824.0;
        RamDisplay = $"{usedGb:F1} / {totalGb:F0} GB";

        // Disk: "312 / 476 GB"
        var usedDiskGb  = snap.DiskUsedBytes  / 1_073_741_824.0;
        var totalDiskGb = snap.DiskTotalBytes / 1_073_741_824.0;
        StorageDisplay = $"{usedDiskGb:F0} / {totalDiskGb:F0} GB";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _refreshTimer?.Stop();
    }
}

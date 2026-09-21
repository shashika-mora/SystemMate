using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using SystemMate.Models;

namespace SystemMate.Services;

/// <summary>
/// Reads live system metrics: CPU, RAM, Disk, boot time, OS info.
/// Uses PerformanceCounter + P/Invoke + WMI. Thread-safe; call CollectAsync from any thread.
/// </summary>
public sealed class SystemInfoService : IDisposable
{
    private PerformanceCounter? _cpuCounter;
    private bool _disposed;

    public SystemInfoService()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue(); // first call always returns 0 — prime it
        }
        catch
        {
            _cpuCounter = null; // fallback: report 0
        }
    }

    public async Task<SystemSnapshot> CollectAsync()
    {
        // Run WMI calls off the UI thread
        return await Task.Run(() =>
        {
            var cpu = GetCpuUsage();
            GetMemoryStatus(out var ramUsed, out var ramTotal);
            GetDiskInfo(out var diskUsed, out var diskTotal);
            var bootTime = GetLastBootTime();
            var (osVersion, machineName, cpuName) = GetStaticInfo();

            return new SystemSnapshot
            {
                CpuUsagePercent   = cpu,
                RamUsedBytes      = ramUsed,
                RamTotalBytes     = ramTotal,
                DiskUsedBytes     = diskUsed,
                DiskTotalBytes    = diskTotal,
                LastBootTime      = bootTime,
                OsVersion         = osVersion,
                MachineName       = machineName,
                CpuName           = cpuName,
                DiskHealthStatus  = "Good" // placeholder; v0.2 will use SMART via WMI
            };
        });
    }

    // ── CPU ─────────────────────────────────────────────────────────────────

    private double GetCpuUsage()
    {
        try { return Math.Round(_cpuCounter?.NextValue() ?? 0, 1); }
        catch { return 0; }
    }

    // ── RAM ─────────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private static void GetMemoryStatus(out ulong used, out ulong total)
    {
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref mem))
        {
            total = mem.ullTotalPhys;
            used  = mem.ullTotalPhys - mem.ullAvailPhys;
        }
        else
        {
            total = 0;
            used  = 0;
        }
    }

    // ── Disk ─────────────────────────────────────────────────────────────────

    private static void GetDiskInfo(out long used, out long total)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            total = drive.TotalSize;
            used  = drive.TotalSize - drive.AvailableFreeSpace;
        }
        catch
        {
            total = 0;
            used  = 0;
        }
    }

    // ── Boot time ────────────────────────────────────────────────────────────

    private static DateTime GetLastBootTime()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT LastBootUpTime FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                var raw = obj["LastBootUpTime"]?.ToString();
                if (raw is not null)
                    return ManagementDateTimeConverter.ToDateTime(raw);
            }
        }
        catch { /* fall through */ }
        return DateTime.Now;
    }

    // ── Static info ──────────────────────────────────────────────────────────

    private static (string os, string machine, string cpu) GetStaticInfo()
    {
        string os = string.Empty, machine = Environment.MachineName, cpu = string.Empty;
        try
        {
            using var osSearcher = new ManagementObjectSearcher(
                "SELECT Caption FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in osSearcher.Get())
                os = obj["Caption"]?.ToString() ?? string.Empty;

            using var cpuSearcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_Processor");
            foreach (ManagementObject obj in cpuSearcher.Get())
                cpu = obj["Name"]?.ToString() ?? string.Empty;
        }
        catch { /* best-effort */ }

        return (os, machine, cpu);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cpuCounter?.Dispose();
    }
}

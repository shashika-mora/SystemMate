using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using SystemMate.Models;

namespace SystemMate.Services;

public sealed class PerformanceService : IDisposable
{
    private Dictionary<int, (TimeSpan Cpu, long Started)> _previous = new();
    private long _lastSample;
    private string? _processorName;
    private PerformanceCounter? _totalCpu;

    public PerformanceService()
    {
        try
        {
            _totalCpu = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _totalCpu.NextValue();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        { _totalCpu?.Dispose(); _totalCpu = null; }
    }

    public Task<PerformanceSample> SampleAsync(CancellationToken token = default) => Task.Run(() =>
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = _lastSample == 0 ? 0 : Stopwatch.GetElapsedTime(_lastSample, now).TotalSeconds;
        var current = new Dictionary<int, (TimeSpan Cpu, long Started)>();
        var rows = new List<ProcessUsage>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var cpu = process.TotalProcessorTime;
                    var started = process.StartTime.ToUniversalTime().Ticks;
                    current[process.Id] = (cpu, started);
                    var usage = elapsed > 0 && _previous.TryGetValue(process.Id, out var old) && old.Started == started
                        ? Math.Clamp((cpu - old.Cpu).TotalSeconds / elapsed / Environment.ProcessorCount * 100, 0, 100)
                        : 0;
                    rows.Add(new(process.Id, process.ProcessName, usage, process.WorkingSet64, process.Threads.Count));
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                { /* Protected or already-exited processes are not accessible to this user. */ }
            }
        }
        _previous = current;
        _lastSample = now;
        double? frequency = null;
        try
        {
            using var query = new ManagementObjectSearcher("SELECT Name, CurrentClockSpeed FROM Win32_Processor")
            {
                Options = new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(8), ReturnImmediately = false }
            };
            using var values = query.Get();
            token.ThrowIfCancellationRequested();
            foreach (ManagementObject value in values)
            {
                using (value)
                {
                    _processorName ??= value["Name"]?.ToString();
                    frequency = Convert.ToDouble(value["CurrentClockSpeed"]);
                    break;
                }
            }
        }
        catch (ManagementException) { }
        catch (COMException) { }
        token.ThrowIfCancellationRequested();
        double? totalCpu = null;
        try { if (_totalCpu is not null) totalCpu = Math.Clamp(_totalCpu.NextValue(), 0, 100); }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
        return new PerformanceSample(rows.OrderByDescending(r => r.CpuPercent).ThenByDescending(r => r.WorkingSetBytes).Take(40).ToArray(),
            _processorName ?? "Processor information unavailable", Environment.ProcessorCount, frequency, totalCpu);
    }, token);

    public IReadOnlyList<PowerPlan> GetPowerPlans()
    {
        var active = GetActivePlanId();
        var plans = new List<PowerPlan>();
        for (uint index = 0; ; index++)
        {
            uint size = 16;
            var buffer = new byte[16];
            var result = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, index, buffer, ref size);
            if (result == 259) break;
            if (result != 0) throw new Win32Exception((int)result);
            var id = new Guid(buffer);
            uint nameSize = 0;
            result = PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, null, ref nameSize);
            if (result != 0 && result != 234) throw new Win32Exception((int)result);
            var name = new byte[nameSize];
            result = PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, name, ref nameSize);
            if (result != 0) throw new Win32Exception((int)result);
            plans.Add(new(id, Encoding.Unicode.GetString(name).TrimEnd('\0'), id == active));
        }
        return plans;
    }

    public Guid GetActivePlanId()
    {
        var result = PowerGetActiveScheme(IntPtr.Zero, out var pointer);
        if (result != 0) throw new Win32Exception((int)result);
        try { return Marshal.PtrToStructure<Guid>(pointer); }
        finally { LocalFree(pointer); }
    }

    public Guid SetActivePlan(Guid id)
    {
        // Accept an existing plan only; keep the previous ID available to restore.
        if (!GetPowerPlans().Any(p => p.Id == id)) throw new ArgumentException("This power plan is no longer available.");
        var previous = GetActivePlanId();
        var result = PowerSetActiveScheme(IntPtr.Zero, ref id);
        if (result != 0) throw new Win32Exception((int)result);
        if (GetActivePlanId() != id) throw new InvalidOperationException("Windows did not activate the selected power plan.");
        return previous;
    }

    public void Dispose() => _totalCpu?.Dispose();

    [DllImport("powrprof.dll")]
    private static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr subgroup, uint access,
        uint index, byte[] buffer, ref uint size);
    [DllImport("powrprof.dll")]
    private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr subgroup,
        IntPtr setting, byte[]? buffer, ref uint size);
    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}

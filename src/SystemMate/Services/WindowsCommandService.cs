using System.Diagnostics;
using System.Text;
using SystemMate.Models;

namespace SystemMate.Services;

/// <summary>Runs only the reviewed commands in the built-in catalog.</summary>
public sealed class WindowsCommandService
{
    public static IReadOnlyList<SystemTool> Tools { get; } = Array.AsReadOnly<SystemTool>(
    [
        new("network-info", "Network configuration", "Network", "Show adapters, addresses, DNS servers, and DHCP details.", "ipconfig.exe", ["/all"]),
        new("dns-cache", "Inspect DNS cache", "Network", "View recently cached DNS answers on this computer.", "ipconfig.exe", ["/displaydns"]),
        new("dns-flush", "Clear DNS cache", "Network", "Discard cached DNS answers. Windows will resolve names again as needed.", "ipconfig.exe", ["/flushdns"], ChangesSystem: true),
        new("sleep-states", "Available sleep states", "Power", "Explain which sleep and standby modes this device supports.", "powercfg.exe", ["/a"]),
        new("power-requests", "What is keeping this PC awake?", "Power", "List current application and driver power requests.", "powercfg.exe", ["/requests"], true),
        new("component-check", "Quick Windows image check", "Windows health", "Read the recorded component-store health status without repairing Windows.", "Dism.exe", ["/Online", "/Cleanup-Image", "/CheckHealth"], true),
        new("component-scan", "Scan Windows image", "Windows health", "Perform a deeper component-store corruption scan. This may take several minutes.", "Dism.exe", ["/Online", "/Cleanup-Image", "/ScanHealth"], true),
        new("component-repair", "Repair Windows image", "Windows health", "Repair the Windows component store. Windows Update may download replacement files; save your work first.", "Dism.exe", ["/Online", "/Cleanup-Image", "/RestoreHealth"], true, true),
        new("system-verify", "Verify system files", "Windows health", "Check protected system files without repairing them. This may take several minutes.", "sfc.exe", ["/verifyonly"], true),
        new("system-repair", "Repair system files", "Windows health", "Scan and repair protected system files. Repair the Windows image first when component-store corruption is suspected.", "sfc.exe", ["/scannow"], true, true),
        new("app-updates", "Check app updates", "Updates", "Ask Windows Package Manager for available updates. Availability depends on installed sources and package matching; no updates are installed.", "winget.exe", ["upgrade", "--disable-interactivity"]),
    ]);

    public async Task<CommandResult> RunAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tool = Tools.SingleOrDefault(t => t.Id == id)
            ?? throw new ArgumentException("Unknown tool.", nameof(id));
        var executable = ResolveExecutable(tool.Executable);
        if (!File.Exists(executable))
            throw new FileNotFoundException(tool.Executable == "winget.exe"
                ? "Windows Package Manager is unavailable. Install or update App Installer from Microsoft Store."
                : "This Windows tool is unavailable on this device.");

        if (tool.RequiresAdministrator)
        {
            // A visible console keeps the complete repair output available to the user.
            // Every argument comes from the fixed catalog; no user-supplied shell text is accepted.
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = $"/d /k \"\"{executable}\" {string.Join(' ', tool.Arguments)}\"",
                WorkingDirectory = Environment.SystemDirectory
            };
            using var launched = Process.Start(start)
                ?? throw new InvalidOperationException("Windows could not open the tool.");
            return new(null, "Opened in an administrator command window. Review its output there; opening the window does not establish that the operation succeeded.", true);
        }

        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Environment.SystemDirectory
        };
        foreach (var argument in tool.Arguments) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        process.Start();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var stdout = ReadBoundedAsync(process.StandardOutput, timeout.Token);
        var stderr = ReadBoundedAsync(process.StandardError, timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var output = (await stdout) + (await stderr);
            return new(process.ExitCode, output.Length == 0 ? "The tool returned no output." : output);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            try { await Task.WhenAll(stdout, stderr); } catch (OperationCanceledException) { }
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException("The tool did not finish within two minutes.");
        }
    }

    private static string ResolveExecutable(string name) => name == "winget.exe"
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe")
        : Path.Combine(Environment.SystemDirectory, name);

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        const int limit = 120_000;
        var output = new StringBuilder();
        var buffer = new char[4096];
        int count;
        var truncated = false;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            var retain = Math.Min(count, Math.Max(0, limit - output.Length));
            output.Append(buffer, 0, retain);
            truncated |= retain < count;
        }
        if (truncated) output.AppendLine("\n[Output truncated for display.]");
        return output.ToString();
    }
}

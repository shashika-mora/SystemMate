using SystemMate.Models;
using SystemMate.Services;
using System.Runtime.Versioning;
using Xunit;

namespace SystemMate.Tests;

[SupportedOSPlatform("windows")]
public sealed class StartupServiceSafetyTests
{
    [Fact]
    public async Task DisableAsync_RejectsUnsupportedEntryBeforeRegistryAccess()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => new StartupService().DisableAsync(new StartupItem
        {
            Name = "System service", Command = "example.exe", CanDisable = false
        }));
    }

    [Fact]
    public async Task DisableAsync_RejectsUnsupportedRegistryViewBeforeRegistryAccess()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => new StartupService().DisableAsync(new StartupItem
        {
            Name = "Unreviewed item", Command = "example.exe", CanDisable = true,
            RegistryView = int.MaxValue, RegistryValueKind = 1
        }));
    }

    [Fact]
    public async Task RestoreAsync_RejectsBackupOutsideItsPrivateDirectory()
    {
        var outsidePath = Path.Combine(Path.GetTempPath(), "systemmate-untrusted-startup.json");
        await Assert.ThrowsAsync<InvalidOperationException>(() => new StartupService().RestoreAsync(new StartupItem
        {
            Name = "Untrusted backup", Command = "example.exe", BackupPath = outsidePath
        }));
    }
}

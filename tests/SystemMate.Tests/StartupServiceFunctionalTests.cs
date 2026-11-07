using Microsoft.Win32;
using System.Runtime.Versioning;
using System.Text.Json;
using SystemMate.Models;
using SystemMate.Services;
using Xunit;

namespace SystemMate.Tests;

[SupportedOSPlatform("windows")]
public sealed class StartupServiceFunctionalTests : IDisposable
{
    private const string TestRegistryParent = @"Software\SystemMateTests\";
    private readonly string _id = Guid.NewGuid().ToString("N");
    private readonly string _testKeyRoot;
    private readonly string _runKeyPath;
    private readonly string _backupDirectory;
    private readonly RegistryView _view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
    private readonly StartupService _service;
    private const string ValueName = "Example startup";
    private const string OriginalCommand = "\"%LOCALAPPDATA%\\SystemMate test\\example.exe\" --mode \"sample\"";

    public StartupServiceFunctionalTests()
    {
        _testKeyRoot = TestRegistryParent + _id;
        _runKeyPath = _testKeyRoot + @"\Run";
        _backupDirectory = Path.Combine(Path.GetTempPath(), "SystemMate_StartupTests_" + _id);
        _service = new StartupService(_backupDirectory, _runKeyPath);
        using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, _view);
        using var key = root.CreateSubKey(_runKeyPath, writable: true);
    }

    [Theory]
    [InlineData(RegistryValueKind.String)]
    [InlineData(RegistryValueKind.ExpandString)]
    public async Task DisableThenRestore_PreservesExactCommandAndValueKindWithVerifiedBackup(RegistryValueKind kind)
    {
        var item = CreateEntry(kind);

        await _service.DisableAsync(item);

        using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, _view);
        using var key = root.OpenSubKey(_runKeyPath, writable: true)!;
        Assert.DoesNotContain(ValueName, key.GetValueNames());
        var backupPath = Assert.Single(Directory.GetFiles(_backupDirectory, "*.json"));
        using (var backup = JsonDocument.Parse(await File.ReadAllTextAsync(backupPath)))
        {
            Assert.Equal(ValueName, backup.RootElement.GetProperty("ValueName").GetString());
            Assert.Equal(OriginalCommand, backup.RootElement.GetProperty("Command").GetString());
            Assert.Equal((int)kind, backup.RootElement.GetProperty("ValueKind").GetInt32());
            Assert.Equal((int)_view, backup.RootElement.GetProperty("View").GetInt32());
        }

        await _service.RestoreAsync(item with { CanDisable = false, BackupPath = backupPath });

        Assert.Equal(OriginalCommand, key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
        Assert.Equal(kind, key.GetValueKind(ValueName));
        Assert.True(File.Exists(backupPath));
        Assert.True(File.Exists(backupPath + ".restored"));
    }

    [Fact]
    public async Task RestoreAsync_DoesNotOverwriteAnExistingValue()
    {
        var item = CreateEntry(RegistryValueKind.ExpandString);
        await _service.DisableAsync(item);
        var backupPath = Assert.Single(Directory.GetFiles(_backupDirectory, "*.json"));
        using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, _view);
        using var key = root.OpenSubKey(_runKeyPath, writable: true)!;
        key.SetValue(ValueName, "replacement value", RegistryValueKind.String);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RestoreAsync(item with { CanDisable = false, BackupPath = backupPath }));

        Assert.Equal("replacement value", key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
        Assert.Equal(RegistryValueKind.String, key.GetValueKind(ValueName));
        Assert.False(File.Exists(backupPath + ".restored"));
    }

    [Fact]
    public async Task RestoreAsync_RejectsCorruptedBackupBeforeChangingAnyValue()
    {
        var item = CreateEntry(RegistryValueKind.String);
        await _service.DisableAsync(item);
        var backupPath = Assert.Single(Directory.GetFiles(_backupDirectory, "*.json"));
        await File.WriteAllTextAsync(backupPath, "not valid JSON");
        using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, _view);
        using var key = root.OpenSubKey(_runKeyPath, writable: true)!;
        key.SetValue("Unrelated", "keep me", RegistryValueKind.String);

        await Assert.ThrowsAsync<JsonException>(() => _service.RestoreAsync(item with { CanDisable = false, BackupPath = backupPath }));

        Assert.DoesNotContain(ValueName, key.GetValueNames());
        Assert.Equal("keep me", key.GetValue("Unrelated"));
        Assert.Single(key.GetValueNames());
        Assert.False(File.Exists(backupPath + ".restored"));
    }

    private StartupItem CreateEntry(RegistryValueKind kind)
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, _view);
        using var key = root.OpenSubKey(_runKeyPath, writable: true)!;
        key.SetValue(ValueName, OriginalCommand, kind);
        return new StartupItem
        {
            Name = ValueName, RegistryValueName = ValueName, Command = OriginalCommand,
            RegistryView = (int)_view, RegistryValueKind = (int)kind, CanDisable = true
        };
    }

    public void Dispose()
    {
        // Cleanup is restricted to this fixture's GUID key and temporary directory.
        if (_testKeyRoot != TestRegistryParent + _id || !Guid.TryParseExact(_id, "N", out _))
            throw new InvalidOperationException("Unexpected test registry cleanup target.");
        using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, _view);
        root.DeleteSubKeyTree(_testKeyRoot, throwOnMissingSubKey: false);
        var expectedBackup = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SystemMate_StartupTests_" + _id));
        if (!string.Equals(Path.GetFullPath(_backupDirectory), expectedBackup, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected test directory cleanup target.");
        if (Directory.Exists(_backupDirectory)) Directory.Delete(_backupDirectory, recursive: true);
    }
}

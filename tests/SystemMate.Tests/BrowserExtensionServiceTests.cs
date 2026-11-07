using SystemMate.Models;
using SystemMate.Services;
using Xunit;

namespace SystemMate.Tests;

public sealed class BrowserExtensionServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SystemMate_extensions_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ScanAsync_ReadsChromiumLocalizationAndSeparatesOptionalPermissions()
    {
        var root = Path.Combine(_directory, "Chrome");
        var extension = Path.Combine(root, "Default", "Extensions", "test-extension", "1.0_0");
        Directory.CreateDirectory(Path.Combine(extension, "_locales", "en"));
        await File.WriteAllTextAsync(Path.Combine(extension, "manifest.json"), """
            {"name":"__MSG_name__","description":"Description","version":"1.0","default_locale":"en","permissions":["tabs"],"host_permissions":["https://example.test/*"],"optional_permissions":["bookmarks"]}
            """);
        await File.WriteAllTextAsync(Path.Combine(extension, "_locales", "en", "messages.json"), """{"name":{"message":"Localized extension"}}""");
        var result = await new BrowserExtensionService(new[] { new BrowserExtensionRoot("Google Chrome", root) }).ScanAsync();
        var item = Assert.Single(result.Items);
        Assert.Equal("Localized extension", item.Name);
        Assert.Equal("Default", item.Profile);
        Assert.Contains("tabs", item.Permissions);
        Assert.Contains("https://example.test/*", item.Permissions);
        Assert.Contains("bookmarks", item.OptionalPermissions);
        Assert.Contains("enabled state not checked", item.Status);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task ScanAsync_ReadsFirefoxSavedStateAndIgnoresThemes()
    {
        var root = Path.Combine(_directory, "Firefox");
        Directory.CreateDirectory(Path.Combine(root, "test.default"));
        await File.WriteAllTextAsync(Path.Combine(root, "test.default", "extensions.json"), """
            {"addons":[{"id":"test@example.test","type":"extension","version":"2.1","defaultLocale":{"name":"Test addon"},"active":false,"userPermissions":{"permissions":["tabs"],"origins":["https://example.test/*"]}},{"id":"theme","type":"theme"}]}
            """);
        var result = await new BrowserExtensionService(new[] { new BrowserExtensionRoot("Firefox", root, IsFirefox: true) }).ScanAsync();
        var item = Assert.Single(result.Items);
        Assert.Equal("Test addon", item.Name);
        Assert.Equal("Saved metadata: inactive", item.Status);
        Assert.Equal("about:addons", item.ManagerAddress);
        Assert.Equal(2, item.Permissions.Count);
    }

    [Fact]
    public async Task ScanAsync_SkipsOversizedAndMalformedManifestsWithoutReadingBrowserHistory()
    {
        var root = Path.Combine(_directory, "Chrome");
        var profile = Path.Combine(root, "Default");
        foreach (var id in new[] { "large", "malformed", "valid" }) Directory.CreateDirectory(Path.Combine(profile, "Extensions", id, "1"));
        await File.WriteAllTextAsync(Path.Combine(profile, "Extensions", "large", "1", "manifest.json"), new string(' ', 1_048_577));
        await File.WriteAllTextAsync(Path.Combine(profile, "Extensions", "malformed", "1", "manifest.json"), "not JSON");
        await File.WriteAllTextAsync(Path.Combine(profile, "Extensions", "valid", "1", "manifest.json"), """{"name":"Valid","version":"1"}""");
        var history = Path.Combine(profile, "History");
        using var lockedHistory = new FileStream(history, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        var result = await new BrowserExtensionService(new[] { new BrowserExtensionRoot("Google Chrome", root) }).ScanAsync();
        Assert.Equal("Valid", Assert.Single(result.Items).Name);
        Assert.Single(result.Warnings);
        Assert.Equal(0, lockedHistory.Length);
    }

    [Fact]
    public async Task ScanAsync_RejectsCancellationBeforeReadingProfiles()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new BrowserExtensionService(Array.Empty<BrowserExtensionRoot>()).ScanAsync(cancellation.Token));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}

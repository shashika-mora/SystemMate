using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;
using SystemMate.Models;

namespace SystemMate.Services;

public sealed class BrowserExtensionService
{
    private const long ManifestLimit = 1_048_576;
    private const long FirefoxDatabaseLimit = 8_388_608;
    private const int ItemLimit = 5000;
    private readonly IReadOnlyList<BrowserExtensionRoot> _roots;

    public BrowserExtensionService(IEnumerable<BrowserExtensionRoot>? roots = null) => _roots = (roots ?? DefaultRoots()).ToArray();

    public Task<BrowserExtensionInventoryResult> ScanAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var items = new List<BrowserExtensionItem>();
        var warnings = new HashSet<string>();
        foreach (var root in _roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!Directory.Exists(root.RootPath)) continue;
                if (HasReparseAncestor(root.RootPath)) { warnings.Add($"Skipped a linked {root.Browser} profile location."); continue; }
                foreach (var profile in SafeDirectories(root.RootPath, root.Browser, warnings))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (root.IsFirefox) ReadFirefox(root, profile, items, warnings, cancellationToken);
                    else ReadChromium(root, profile, items, warnings, cancellationToken);
                    if (items.Count >= ItemLimit) break;
                }
            }
            catch (Exception ex) when (IsReadError(ex)) { warnings.Add($"Could not read some {root.Browser} extension metadata."); }
            if (items.Count >= ItemLimit) { warnings.Add($"Stopped after {ItemLimit} extension records. Some entries were not scanned."); break; }
        }
        return new BrowserExtensionInventoryResult(items.OrderBy(x => x.Browser).ThenBy(x => x.Profile).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(), warnings.ToArray());
    }, cancellationToken);

    private static void ReadChromium(BrowserExtensionRoot root, string profile, List<BrowserExtensionItem> items,
        HashSet<string> warnings, CancellationToken cancellationToken)
    {
        var extensions = Path.Combine(profile, "Extensions");
        if (!Directory.Exists(extensions)) return;
        foreach (var extension in SafeDirectories(extensions, root.Browser, warnings))
        foreach (var version in SafeDirectories(extension, root.Browser, warnings))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (items.Count >= ItemLimit) return;
            var manifestPath = Path.Combine(version, "manifest.json");
            if (!File.Exists(manifestPath)) continue;
            try
            {
                using var document = ReadJson(manifestPath, ManifestLimit);
                var manifest = document.RootElement;
                if (manifest.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid extension manifest.");
                var id = Path.GetFileName(extension);
                var name = Localize(GetString(manifest, "name"), manifest, version);
                var description = Localize(GetString(manifest, "description"), manifest, version);
                var permissions = ReadArray(manifest, "permissions").Concat(ReadArray(manifest, "host_permissions")).ToList();
                if (manifest.TryGetProperty("content_scripts", out var scripts) && scripts.ValueKind == JsonValueKind.Array)
                    foreach (var script in scripts.EnumerateArray().Take(100))
                        permissions.AddRange(ReadArray(script, "matches").Select(x => $"content script: {x}"));
                items.Add(new BrowserExtensionItem
                {
                    Browser = root.Browser, Profile = Path.GetFileName(profile), Id = id,
                    Name = string.IsNullOrWhiteSpace(name) ? id : name, Description = description,
                    Version = GetString(manifest, "version"), Status = "Installed files · enabled state not checked",
                    MetadataPath = manifestPath, ManagerAddress = ManagerAddress(root),
                    Permissions = permissions.Distinct(StringComparer.Ordinal).Take(500).ToArray(),
                    OptionalPermissions = ReadArray(manifest, "optional_permissions").Concat(ReadArray(manifest, "optional_host_permissions")).Distinct(StringComparer.Ordinal).Take(500).ToArray()
                });
            }
            catch (Exception ex) when (IsReadError(ex)) { warnings.Add($"Some {root.Browser} manifests were unreadable, invalid, linked, or larger than 1 MB."); }
        }
    }

    private static void ReadFirefox(BrowserExtensionRoot root, string profile, List<BrowserExtensionItem> items,
        HashSet<string> warnings, CancellationToken cancellationToken)
    {
        var metadataPath = Path.Combine(profile, "extensions.json");
        if (!File.Exists(metadataPath)) return;
        try
        {
            using var document = ReadJson(metadataPath, FirefoxDatabaseLimit);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("addons", out var addons) || addons.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Invalid Firefox extension metadata.");
            foreach (var addon in addons.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (items.Count >= ItemLimit) return;
                if (GetString(addon, "type") != "extension") continue;
                var id = GetString(addon, "id");
                var name = "";
                var description = "";
                if (addon.TryGetProperty("defaultLocale", out var locale) && locale.ValueKind == JsonValueKind.Object)
                {
                    name = GetString(locale, "name");
                    description = GetString(locale, "description");
                }
                var permissions = ReadPermissionObject(addon, "userPermissions");
                var optional = ReadPermissionObject(addon, "optionalPermissions");
                var state = "Saved metadata · enabled state unknown";
                if (addon.TryGetProperty("active", out var active) && active.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    state = active.GetBoolean() ? "Saved metadata: active" : "Saved metadata: inactive";
                items.Add(new BrowserExtensionItem
                {
                    Browser = root.Browser, Profile = Path.GetFileName(profile), Id = id,
                    Name = string.IsNullOrWhiteSpace(name) ? id : name, Description = description,
                    Version = GetString(addon, "version"), Status = state, MetadataPath = metadataPath,
                    ManagerAddress = "about:addons", Permissions = permissions, OptionalPermissions = optional
                });
            }
        }
        catch (Exception ex) when (IsReadError(ex)) { warnings.Add("Some Firefox extension metadata was unreadable, invalid, linked, or larger than 8 MB."); }
    }

    private static string[] ReadPermissionObject(JsonElement addon, string property)
    {
        if (!addon.TryGetProperty(property, out var permissions) || permissions.ValueKind != JsonValueKind.Object) return Array.Empty<string>();
        return ReadArray(permissions, "permissions").Concat(ReadArray(permissions, "origins")).Distinct(StringComparer.Ordinal).Take(500).ToArray();
    }

    private static string Localize(string value, JsonElement manifest, string versionDirectory)
    {
        if (!value.StartsWith("__MSG_", StringComparison.Ordinal) || !value.EndsWith("__", StringComparison.Ordinal)) return value;
        var locale = GetString(manifest, "default_locale");
        if (!Regex.IsMatch(locale, "^[A-Za-z0-9_-]{1,30}$")) return value;
        var path = Path.Combine(versionDirectory, "_locales", locale, "messages.json");
        if (!File.Exists(path)) return value;
        try
        {
            using var messages = ReadJson(path, ManifestLimit);
            var key = value[6..^2];
            if (messages.RootElement.ValueKind == JsonValueKind.Object && messages.RootElement.TryGetProperty(key, out var entry))
                return GetString(entry, "message") is { Length: > 0 } message ? message : value;
        }
        catch (Exception ex) when (IsReadError(ex)) { }
        return value;
    }

    private static JsonDocument ReadJson(string path, long limit)
    {
        if (HasReparseAncestor(path)) throw new IOException("Linked metadata is skipped.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > limit) throw new InvalidDataException("Metadata exceeds read limit.");
        using var limited = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            if (limited.Length + read > limit) throw new InvalidDataException("Metadata grew beyond read limit.");
            limited.Write(buffer, 0, read);
        }
        return JsonDocument.Parse(limited.ToArray(), new JsonDocumentOptions { MaxDepth = 64, AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
    }

    private static IEnumerable<string> SafeDirectories(string path, string browser, HashSet<string> warnings)
    {
        try
        {
            if (HasReparseAncestor(path)) { warnings.Add($"Skipped linked {browser} extension directories."); return Array.Empty<string>(); }
            return Directory.GetDirectories(path).Where(x => !HasReparseAncestor(x)).Take(ItemLimit).ToArray();
        }
        catch (Exception ex) when (IsReadError(ex)) { warnings.Add($"Some {browser} extension directories could not be read."); return Array.Empty<string>(); }
    }

    private static bool HasReparseAncestor(string path)
    {
        FileSystemInfo? current = File.Exists(path) ? new FileInfo(path) : new DirectoryInfo(path);
        while (current is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) return true;
            current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent;
        }
        return false;
    }

    private static string GetString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return "";
        var text = value.GetString() ?? "";
        return text.Length > 2000 ? text[..2000] : text;
    }

    private static IEnumerable<string> ReadArray(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
        return array.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Take(500)
            .Select(x => x.GetString() ?? "").Select(x => x.Length > 500 ? x[..500] : x).ToArray();
    }

    private static string ManagerAddress(BrowserExtensionRoot root) => root.Browser switch
    {
        "Microsoft Edge" => "edge://extensions", "Brave" => "brave://extensions", "Vivaldi" => "vivaldi://extensions", _ => "chrome://extensions"
    };

    private static IEnumerable<BrowserExtensionRoot> DefaultRoots()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new[]
        {
            new BrowserExtensionRoot("Google Chrome", Path.Combine(local, "Google", "Chrome", "User Data")),
            new BrowserExtensionRoot("Microsoft Edge", Path.Combine(local, "Microsoft", "Edge", "User Data")),
            new BrowserExtensionRoot("Brave", Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data")),
            new BrowserExtensionRoot("Vivaldi", Path.Combine(local, "Vivaldi", "User Data")),
            new BrowserExtensionRoot("Firefox", Path.Combine(roaming, "Mozilla", "Firefox", "Profiles"), IsFirefox: true)
        };
    }

    private static bool IsReadError(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException
        or SecurityException or JsonException or ArgumentException or InvalidOperationException;
}

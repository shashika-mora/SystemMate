using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using SystemMate.Helpers;
using SystemMate.Models;
using SystemMate.Services;
using Windows.ApplicationModel.DataTransfer;

namespace SystemMate.Views;

public sealed partial class BrowserExtensionsPage : Page
{
    private readonly BrowserExtensionService _service = new();
    private IReadOnlyList<BrowserExtensionItem> _allItems = Array.Empty<BrowserExtensionItem>();
    private CancellationTokenSource? _scanCancellation;
    private bool _active;
    public ObservableCollection<BrowserExtensionItem> Items { get; } = new();

    public BrowserExtensionsPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _active = true;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _active = false;
        _scanCancellation?.Cancel();
        base.OnNavigatedFrom(e);
    }

    private async void RefreshClick(object sender, RoutedEventArgs e)
    {
        if (_scanCancellation is not null) return;
        using var cancellation = new CancellationTokenSource();
        _scanCancellation = cancellation;
        RefreshButton.IsEnabled = ExportButton.IsEnabled = ManagerButton.IsEnabled = false;
        LoadingBar.Visibility = Visibility.Visible;
        StatusText.Text = "Reading extension manifests and Firefox add-on metadata…";
        WarningText.Text = "";
        try
        {
            var result = await _service.ScanAsync(cancellation.Token);
            if (!_active) return;
            _allItems = result.Items;
            Filter();
            WarningText.Text = string.Join(" ", result.Warnings);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (_active) StatusText.Text = $"Could not read extension metadata: {ex.Message}"; }
        finally
        {
            _scanCancellation = null;
            if (_active)
            {
                RefreshButton.IsEnabled = true;
                ExportButton.IsEnabled = Items.Count > 0;
                ManagerButton.IsEnabled = ExtensionsList.SelectedItem is BrowserExtensionItem;
                LoadingBar.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void Filter()
    {
        var query = SearchBox.Text.Trim();
        Items.Clear();
        foreach (var item in _allItems.Where(x => query.Length == 0 || x.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || x.Browser.Contains(query, StringComparison.CurrentCultureIgnoreCase) || x.Profile.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || x.Permissions.Concat(x.OptionalPermissions).Any(p => p.Contains(query, StringComparison.CurrentCultureIgnoreCase)))) Items.Add(item);
        StatusText.Text = $"{Items.Count} of {_allItems.Count} installed-file records. Multiple versions can be retained by a browser; these are not a live enabled count.";
        ExportButton.IsEnabled = Items.Count > 0 && _scanCancellation is null;
    }

    private void SearchChanged(object sender, TextChangedEventArgs e) { if (StatusText is not null) Filter(); }
    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ManagerButton is null) return;
        ManagerButton.IsEnabled = _scanCancellation is null && ExtensionsList.SelectedItem is BrowserExtensionItem;
        if (ExtensionsList.SelectedItem is not BrowserExtensionItem item)
        {
            if (SelectionText is not null) SelectionText.Text = "Select an extension to see its identifier, metadata source and permission list.";
            return;
        }
        SelectionText.Text = $"{item.Name}\n{item.Description}\n\nID: {item.Id}\nBrowser: {item.Browser} · Profile: {item.Profile}\n{item.Status}\nSource: {item.MetadataPath}\n\nListed permissions / sites:\n{(item.Permissions.Count == 0 ? "None listed in this metadata." : string.Join("\n", item.Permissions))}\n\nOptional permissions / sites:\n{(item.OptionalPermissions.Count == 0 ? "None listed in this metadata." : string.Join("\n", item.OptionalPermissions))}\n\nManage in the browser: {item.ManagerAddress}";
    }

    private void CopyManagerClick(object sender, RoutedEventArgs e)
    {
        if (ExtensionsList.SelectedItem is not BrowserExtensionItem item) return;
        try
        {
            var package = new DataPackage();
            package.SetText(item.ManagerAddress);
            Clipboard.SetContent(package);
            StatusText.Text = $"Copied {item.ManagerAddress}. Paste it into {item.Browser}'s address bar to manage extensions.";
        }
        catch (Exception ex) { StatusText.Text = $"Could not copy the manager address: {ex.Message}"; }
    }

    private async void ExportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (App.MainWindow is null) return;
            var snapshot = Items.ToArray();
            var path = await InventoryExportHelper.SaveCsvAsync($"SystemMate_Extensions_{DateTime.Now:yyyyMMdd_HHmmss}",
                new[] { "Name", "Browser", "Profile", "Version", "Id", "Status", "ListedPermissions", "OptionalPermissions" },
                snapshot.Select(x => new[] { x.Name, x.Browser, x.Profile, x.Version, x.Id, x.Status, string.Join("; ", x.Permissions), string.Join("; ", x.OptionalPermissions) }), App.MainWindow);
            if (path is not null && _active) StatusText.Text = $"Exported {snapshot.Length} displayed records to {path}";
        }
        catch (Exception ex) { if (_active) StatusText.Text = $"Export failed: {ex.Message}"; }
    }
}

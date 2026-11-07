using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using SystemMate.Helpers;
using SystemMate.Models;
using SystemMate.Services;
using Windows.System;

namespace SystemMate.Views;

public sealed partial class StartupPage : Page
{
    private readonly StartupService _service = new();
    private IReadOnlyList<StartupItem> _allItems = Array.Empty<StartupItem>();
    private CancellationTokenSource? _scanCancellation;
    private bool _active;
    private bool _changing;
    public ObservableCollection<StartupItem> Items { get; } = new();

    public StartupPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _active = true;
        await RefreshAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _active = false;
        _scanCancellation?.Cancel();
        base.OnNavigatedFrom(e);
    }

    private async Task RefreshAsync()
    {
        if (_scanCancellation is not null) return;
        using var cancellation = new CancellationTokenSource();
        _scanCancellation = cancellation;
        SetBusy(true);
        StatusText.Text = "Reading startup entries…";
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
        catch (Exception ex) { if (_active) StatusText.Text = $"Could not read startup entries: {ex.Message}"; }
        finally
        {
            _scanCancellation = null;
            if (_active) SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        RefreshButton.IsEnabled = !busy && !_changing;
        ExportButton.IsEnabled = !busy && !_changing && Items.Count > 0;
        LoadingBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        UpdateActions();
    }

    private void Filter()
    {
        var query = SearchBox.Text.Trim();
        Items.Clear();
        foreach (var item in _allItems.Where(x => query.Length == 0 || x.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || x.Command.Contains(query, StringComparison.CurrentCultureIgnoreCase) || x.Source.Contains(query, StringComparison.CurrentCultureIgnoreCase))) Items.Add(item);
        StatusText.Text = $"{Items.Count} of {_allItems.Count} startup entries · Unknown status does not establish that an entry is enabled.";
        ExportButton.IsEnabled = Items.Count > 0 && _scanCancellation is null && !_changing;
        UpdateActions();
    }

    private void UpdateActions()
    {
        if (StartupList is null || DisableButton is null || RestoreButton is null) return;
        var item = StartupList.SelectedItem as StartupItem;
        DisableButton.IsEnabled = !_changing && _scanCancellation is null && item?.CanDisable == true;
        RestoreButton.IsEnabled = !_changing && _scanCancellation is null && item?.CanRestore == true;
    }

    private async void RefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (StatusText is not null) Filter(); }
    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StartupList.SelectedItem is StartupItem item) SelectionText.Text = $"{item.Name} · {item.Source}\n{item.Command}";
        else if (SelectionText is not null) SelectionText.Text = "Select a startup entry to view its details.";
        UpdateActions();
    }

    private async void DisableClick(object sender, RoutedEventArgs e) => await ChangeSelectedAsync(restore: false);
    private async void RestoreClick(object sender, RoutedEventArgs e) => await ChangeSelectedAsync(restore: true);

    private async Task ChangeSelectedAsync(bool restore)
    {
        if (_changing || StartupList.SelectedItem is not StartupItem item || (restore ? !item.CanRestore : !item.CanDisable)) return;
        var dialog = new ContentDialog
        {
            Title = restore ? "Restore this startup entry?" : "Disable this startup entry?",
            Content = $"{item.Name}\n\n{item.Command}\n\n" + (restore
                ? "The saved current-user Run entry will be restored for future sign-ins. Any existing entry with the same name will be kept."
                : "A local backup will be saved and verified before this current-user Run entry is removed. You can restore it here later. This does not stop a running app."),
            PrimaryButtonText = restore ? "Restore" : "Disable",
            CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot, RequestedTheme = ActualTheme
        };
        _changing = true;
        SetBusy(false);
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || !_active) return;
            if (restore) await _service.RestoreAsync(item); else await _service.DisableAsync(item);
            await RefreshAsync();
            if (_active) StatusText.Text = restore ? $"Restored {item.Name}. The original backup is retained." : $"Disabled {item.Name}. A verified backup is available for restoration.";
        }
        catch (Exception ex)
        {
            if (_active)
            {
                await RefreshAsync();
                StatusText.Text = $"Startup change could not be completed: {ex.Message}";
            }
        }
        finally { _changing = false; if (_active) SetBusy(false); }
    }

    private async void ExportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (App.MainWindow is null) return;
            var snapshot = Items.ToArray();
            var path = await InventoryExportHelper.SaveCsvAsync($"SystemMate_Startup_{DateTime.Now:yyyyMMdd_HHmmss}",
                new[] { "Name", "Source", "Status", "Command" }, snapshot.Select(x => new[] { x.Name, x.Source, x.Status, x.Command }), App.MainWindow);
            if (path is not null && _active) StatusText.Text = $"Exported {snapshot.Length} displayed entries to {path}";
        }
        catch (Exception ex) { if (_active) StatusText.Text = $"Export failed: {ex.Message}"; }
    }

    private async void ManageClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await Launcher.LaunchUriAsync(new Uri("ms-settings:startupapps"))) StatusText.Text = "Windows could not open Startup settings.";
        }
        catch (Exception ex) { StatusText.Text = $"Could not open Windows settings: {ex.Message}"; }
    }
}

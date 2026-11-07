using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using SystemMate.Helpers;
using SystemMate.Models;
using SystemMate.Services;
using Windows.System;

namespace SystemMate.Views;

public sealed partial class AppsPage : Page
{
    private readonly AppInventoryService _service = new();
    private IReadOnlyList<AppInventoryItem> _allItems = Array.Empty<AppInventoryItem>();
    private CancellationTokenSource? _scanCancellation;
    private bool _active;
    public ObservableCollection<AppInventoryItem> Items { get; } = new();

    public AppsPage() => InitializeComponent();

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
        RefreshButton.IsEnabled = ExportButton.IsEnabled = false;
        LoadingBar.Visibility = Visibility.Visible;
        StatusText.Text = "Reading installed desktop applications…";
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
        catch (Exception ex) { if (_active) StatusText.Text = $"Could not read applications: {ex.Message}"; }
        finally
        {
            _scanCancellation = null;
            if (_active)
            {
                RefreshButton.IsEnabled = true;
                ExportButton.IsEnabled = Items.Count > 0;
                LoadingBar.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void Filter()
    {
        var query = SearchBox.Text.Trim();
        Items.Clear();
        foreach (var item in _allItems.Where(x => query.Length == 0 || x.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || x.Publisher.Contains(query, StringComparison.CurrentCultureIgnoreCase) || x.Version.Contains(query, StringComparison.CurrentCultureIgnoreCase))) Items.Add(item);
        StatusText.Text = $"{Items.Count} of {_allItems.Count} desktop application entries · Select an app to view details.";
        ExportButton.IsEnabled = Items.Count > 0 && _scanCancellation is null;
    }

    private async void RefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (StatusText is not null) Filter(); }
    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AppsList.SelectedItem is AppInventoryItem item)
            SelectionText.Text = $"{item.Name} · Reported date: {(item.ReportedDate.Length == 0 ? "Not reported" : item.ReportedDate)}\nLocation: {(item.Location.Length == 0 ? "Not reported" : item.Location)}\n{item.RegistryPath}";
        else if (SelectionText is not null) SelectionText.Text = "Select an app to view its details.";
    }

    private async void ExportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (App.MainWindow is null) return;
            var snapshot = Items.ToArray();
            var path = await InventoryExportHelper.SaveCsvAsync($"SystemMate_Apps_{DateTime.Now:yyyyMMdd_HHmmss}",
                new[] { "Name", "Publisher", "Version", "ReportedDate", "EstimatedBytes", "Scope", "Location" },
                snapshot.Select(x => new[] { x.Name, x.Publisher, x.Version, x.ReportedDate, x.EstimatedSizeBytes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "", x.Scope, x.Location }), App.MainWindow);
            if (path is not null && _active) StatusText.Text = $"Exported {snapshot.Length} displayed entries to {path}";
        }
        catch (Exception ex) { if (_active) StatusText.Text = $"Export failed: {ex.Message}"; }
    }

    private async void ManageClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await Launcher.LaunchUriAsync(new Uri("ms-settings:appsfeatures"))) StatusText.Text = "Windows could not open Installed apps.";
        }
        catch (Exception ex) { StatusText.Text = $"Could not open Windows settings: {ex.Message}"; }
    }
}

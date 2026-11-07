using System.Diagnostics;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SystemMate.Models;
using SystemMate.Services;
using Windows.Storage.Pickers;

namespace SystemMate.Views;

public sealed partial class ToolsPage : Page
{
    private readonly WindowsCommandService _commands = new();
    private readonly ToolInventoryService _inventory = new();
    private IReadOnlyList<InventoryEntry> _entries = [];
    private CancellationTokenSource? _runningTool;
    private CancellationTokenSource? _loadingInventory;
    private string? _inventoryWarning;
    private bool _ready;

    public ToolsPage()
    {
        InitializeComponent();
        _ready = true;
        CategoryPicker.ItemsSource = new[] { "All tools" }.Concat(WindowsCommandService.Tools.Select(t => t.Category).Distinct()).ToArray();
        CategoryPicker.SelectedIndex = 0;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _runningTool?.Cancel();
        _loadingInventory?.Cancel();
        base.OnNavigatedFrom(e);
    }

    private void FilterTools()
    {
        if (!_ready) return;
        var category = CategoryPicker.SelectedItem as string;
        var search = ToolSearch.Text.Trim();
        ToolList.ItemsSource = WindowsCommandService.Tools.Where(t =>
            (category is null or "All tools" || t.Category == category) &&
            (t.Title + " " + t.Description).Contains(search, StringComparison.CurrentCultureIgnoreCase)).ToArray();
    }
    private void CategoryChanged(object sender, SelectionChangedEventArgs e) => FilterTools();
    private void ToolSearchChanged(object sender, TextChangedEventArgs e) => FilterTools();

    private async void RunToolClick(object sender, RoutedEventArgs e)
    {
        if (_runningTool is not null || (sender as FrameworkElement)?.DataContext is not SystemTool tool) return;
        using var cancellation = new CancellationTokenSource();
        _runningTool = cancellation;
        ToolList.IsEnabled = false;
        try
        {
            if (tool.ChangesSystem || tool.RequiresAdministrator || tool.Id == "app-updates")
            {
                var dialog = new ContentDialog
                {
                    Title = tool.Title,
                    Content = tool.Description + "\n\n" + tool.AccessDescription +
                        (tool.Id == "app-updates" ? "\nPackage sources may be contacted online. Source agreements will not be accepted automatically." : "") +
                        $"\n\nCommand: {tool.CommandPreview}",
                    PrimaryButtonText = tool.ChangesSystem ? "Continue with change" : "Continue",
                    CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
                    XamlRoot = XamlRoot, RequestedTheme = ActualTheme
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            }
            cancellation.Token.ThrowIfCancellationRequested();
            CancelToolButton.IsEnabled = !tool.RequiresAdministrator;
            StatusBar.Message = $"Running: {tool.Title}";
            StatusBar.Severity = InfoBarSeverity.Informational;
            var result = await _commands.RunAsync(tool.Id, cancellation.Token);
            OutputText.Text = $"{tool.Title}\n{DateTime.Now:g}\n\n{result.Output}";
            StatusBar.Message = result.OpenedExternalWindow ? "Tool opened. Review the result in its administrator window."
                : $"{tool.Title} finished · exit code {result.ExitCode}";
            StatusBar.Severity = result.ExitCode is > 0 or < 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
        }
        catch (OperationCanceledException) { StatusBar.Message = "Check cancelled."; }
        catch (Exception ex) { StatusBar.Message = $"Tool could not run: {ex.Message}"; StatusBar.Severity = InfoBarSeverity.Warning; }
        finally { _runningTool = null; CancelToolButton.IsEnabled = false; ToolList.IsEnabled = true; }
    }
    private void CancelToolClick(object sender, RoutedEventArgs e) => _runningTool?.Cancel();

    private string InventoryKind => (InventoryPicker.SelectedItem as ComboBoxItem)?.Tag as string ?? "drivers";
    private void InventoryKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _entries = [];
        _inventoryWarning = null;
        FilterInventory();
        InventoryDescription.Text = InventoryKind switch
        {
            "services" => "Inspect service status and startup mode. Use Windows Services to review dependencies before making changes.",
            "restore" => "Read available restore points. Windows may require administrator access or enabled System Protection; no restore points are removed here.",
            _ => "View installed driver versions and providers. An older date alone does not mean a driver needs updating; use Windows Update or your device manufacturer."
        };
        ManageInventoryButton.Content = InventoryKind switch { "services" => "Open Windows Services", "restore" => "Open System Protection", _ => "Open Device Manager" };
    }

    private async void LoadInventoryClick(object sender, RoutedEventArgs e)
    {
        if (_loadingInventory is not null) return;
        using var cancellation = new CancellationTokenSource();
        _loadingInventory = cancellation;
        InventoryPicker.IsEnabled = LoadInventoryButton.IsEnabled = false;
        InventoryStatusText.Text = "Reading this PC…";
        try
        {
            var result = await _inventory.GetAsync(InventoryKind, cancellation.Token);
            _entries = result.Entries;
            _inventoryWarning = result.Warning;
            FilterInventory();
        }
        catch (OperationCanceledException) { InventoryStatusText.Text = "Inventory cancelled."; }
        catch (Exception ex) { InventoryStatusText.Text = ex.Message; }
        finally { _loadingInventory = null; InventoryPicker.IsEnabled = LoadInventoryButton.IsEnabled = true; }
    }

    private void FilterInventory()
    {
        if (!_ready) return;
        var search = InventorySearch.Text.Trim();
        var rows = _entries.Where(r => (r.Name + " " + r.Detail + " " + r.State).Contains(search, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        InventoryList.ItemsSource = rows;
        InventoryStatusText.Text = _inventoryWarning ?? $"{rows.Length:N0} shown · {_entries.Count:N0} entries read";
        ExportInventoryButton.IsEnabled = rows.Length > 0;
    }
    private void InventorySearchChanged(object sender, TextChangedEventArgs e) => FilterInventory();

    private void ManageInventoryClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var info = InventoryKind == "restore"
                ? new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "SystemPropertiesProtection.exe"))
                : new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "mmc.exe"));
            if (InventoryKind != "restore") info.ArgumentList.Add(Path.Combine(Environment.SystemDirectory, InventoryKind == "services" ? "services.msc" : "devmgmt.msc"));
            info.UseShellExecute = true;
            Process.Start(info);
        }
        catch (Exception ex) { InventoryStatusText.Text = ex.Message; }
    }

    private async void ExportInventoryClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = $"SystemMate-{InventoryKind}" };
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow!));
            picker.FileTypeChoices.Add("CSV", new List<string> { ".csv" });
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            static string Csv(string value)
            {
                if (value.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@') value = "'" + value;
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            var rows = InventoryList.ItemsSource as InventoryEntry[] ?? [];
            var content = "Name,Details,State\r\n" + string.Join("\r\n", rows.Select(r => string.Join(',', Csv(r.Name), Csv(r.Detail), Csv(r.State))));
            await File.WriteAllTextAsync(file.Path, content, new UTF8Encoding(true));
            InventoryStatusText.Text = $"Exported {rows.Length:N0} entries.";
        }
        catch (Exception ex) { InventoryStatusText.Text = $"Export failed: {ex.Message}"; }
    }

    private async void OpenSettingsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if ((sender as FrameworkElement)?.Tag is string uri && !await Windows.System.Launcher.LaunchUriAsync(new Uri(uri)))
                StatusBar.Message = "This settings page is unavailable on this Windows version.";
        }
        catch (Exception ex) { StatusBar.Message = ex.Message; }
    }

    private void OpenRecycleBinClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = true };
            info.ArgumentList.Add("shell:RecycleBinFolder");
            Process.Start(info);
        }
        catch (Exception ex) { StatusBar.Message = ex.Message; }
    }
}

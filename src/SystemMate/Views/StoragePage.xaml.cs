using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Text;
using SystemMate.Models;
using SystemMate.Services;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace SystemMate.Views;

public sealed partial class StoragePage : Page
{
    private readonly StorageAnalysisService _service = new();
    private CancellationTokenSource? _scanCancellation;
    private StorageAnalysisResult? _result;

    public StoragePage() => InitializeComponent();

    private static void InitializePicker(object picker)
    {
        var owner = App.MainWindow ?? throw new InvalidOperationException("The application window is unavailable.");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
    }

    private async void ChooseFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            InitializePicker(picker);
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null) return;
            ClearResults();
            FolderPathBox.Text = folder.Path;
            AnalyzeButton.IsEnabled = true;
            StatusText.Text = "Folder selected. Analyze to measure files and compare contents.";
        }
        catch (Exception exception) { StatusText.Text = $"Could not choose folder: {exception.Message}"; }
    }

    private async void AnalyzeClick(object sender, RoutedEventArgs e)
    {
        if (_scanCancellation is not null || string.IsNullOrWhiteSpace(FolderPathBox.Text)) return;
        ClearResults();
        using var cancellation = new CancellationTokenSource();
        _scanCancellation = cancellation;
        SetBusy(true);
        StatusText.Text = "Analyzing selected folder…";
        try
        {
            var progress = new Progress<StorageAnalysisProgress>(value =>
            {
                if (_scanCancellation != cancellation || cancellation.IsCancellationRequested) return;
                StatusText.Text = $"{value.Stage} · {value.FileCount:N0} files · {StorageAnalysisResult.FormatBytes(value.TotalBytes)}";
                CurrentPathText.Text = value.CurrentPath;
            });
            _result = await _service.AnalyzeAsync(FolderPathBox.Text,
                new StorageAnalysisOptions { FindDuplicates = DuplicatesToggle.IsOn }, progress, cancellation.Token);
            FilesList.ItemsSource = _result.LargestFiles;
            FoldersList.ItemsSource = _result.Folders;
            TypesList.ItemsSource = _result.FileTypes;
            DuplicatesList.ItemsSource = _result.DuplicateGroups;
            StatusText.Text = _result.IsPartial ? "Partial analysis — an analysis limit was reached." : "Analysis complete.";
            if (_result.SkippedEntries > 0) StatusText.Text += $" {_result.SkippedEntries:N0} inaccessible, linked or changed entries skipped.";
            SummaryText.Text = (_result.IsPartial ? "Partial report. " : string.Empty)
                + $"{_result.TotalDisplay} in {_result.FileCount:N0} files. "
                + (_result.DuplicatesChecked
                    ? $"{_result.DuplicateGroups.Count:N0} duplicate groups · {_result.DuplicateDisplay} in additional copies (logical bytes, not guaranteed recoverable space)."
                    : "Duplicate content comparison was turned off.");
            if (_result.DuplicateGroupsLimited) SummaryText.Text += " Duplicate group display limit reached.";
        }
        catch (OperationCanceledException) { StatusText.Text = "Analysis cancelled. No files were changed."; }
        catch (Exception exception) { StatusText.Text = $"Analysis failed: {exception.Message}"; }
        finally
        {
            _scanCancellation = null;
            SetBusy(false);
            CurrentPathText.Text = string.Empty;
        }
    }

    private void SetBusy(bool busy)
    {
        ChooseFolderButton.IsEnabled = !busy;
        AnalyzeButton.IsEnabled = !busy && !string.IsNullOrWhiteSpace(FolderPathBox.Text);
        CancelButton.IsEnabled = busy;
        DuplicatesToggle.IsEnabled = !busy;
        ExportButton.IsEnabled = !busy && _result is not null;
        ScanProgress.IsIndeterminate = busy;
        ScanProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearResults()
    {
        _result = null;
        FilesList.ItemsSource = FoldersList.ItemsSource = TypesList.ItemsSource = DuplicatesList.ItemsSource = null;
        SummaryText.Text = string.Empty;
        ExportButton.IsEnabled = false;
    }

    private void CancelClick(object sender, RoutedEventArgs e) => _scanCancellation?.Cancel();

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _scanCancellation?.Cancel();
        base.OnNavigatedFrom(e);
    }

    private void ResultSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged can fire before the named lists are loaded by InitializeComponent.
        if (FilesList is null) return;
        FilesList.Visibility = ResultSelector.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        FoldersList.Visibility = ResultSelector.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        TypesList.Visibility = ResultSelector.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        DuplicatesList.Visibility = ResultSelector.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ShowFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Button { Tag: string path } && Path.GetDirectoryName(path) is string folder)
                if (!await Windows.System.Launcher.LaunchFolderPathAsync(folder))
                    StatusText.Text = "Windows could not open the containing folder.";
        }
        catch (Exception exception) { StatusText.Text = $"Could not open folder: {exception.Message}"; }
    }

    private async void ExportClick(object sender, RoutedEventArgs e)
    {
        if (_result is not { } result) return;
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = "SystemMate-storage-analysis" };
            picker.FileTypeChoices.Add("CSV report", new List<string> { ".csv" });
            InitializePicker(picker);
            var destination = await picker.PickSaveFileAsync();
            if (destination is null) return;
            var csv = new StringBuilder("Section,Name,Bytes,FileCount,SHA256\r\n");
            AddRow("Summary", result.RootPath, result.TotalBytes, result.FileCount, "");
            AddRow("Analyzed at", result.AnalyzedAt.ToString("O"), 0, 0, "");
            AddRow("Status", result.IsPartial ? "Partial analysis" : "Completed analysis", 0, result.SkippedEntries, "");
            AddRow("Limits", "Top 100 files/folders/types; up to 500 duplicate groups; logical bytes, not allocated disk space", 0, 0, "");
            if (result.DuplicateGroupsLimited) AddRow("Status", "Duplicate group limit reached", 0, 0, "");
            foreach (var file in result.LargestFiles) AddRow("Largest file", file.FullPath, file.SizeBytes, 1, "");
            foreach (var folder in result.Folders) AddRow("Folder", folder.Name, folder.SizeBytes, folder.FileCount, "");
            foreach (var type in result.FileTypes) AddRow("File type", type.Name, type.SizeBytes, type.FileCount, "");
            foreach (var group in result.DuplicateGroups)
                foreach (var path in group.FilePaths) AddRow("Duplicate", path, group.FileSizeBytes, group.FilePaths.Count, group.Sha256);
            await FileIO.WriteTextAsync(destination, csv.ToString(), Windows.Storage.Streams.UnicodeEncoding.Utf8);
            StatusText.Text = "CSV report exported.";

            void AddRow(string section, string name, long bytes, int count, string hash)
                => csv.AppendLine($"{Csv(section)},{Csv(name)},{bytes},{count},{Csv(hash)}");
        }
        catch (Exception exception) { StatusText.Text = $"Export failed: {exception.Message}"; }
    }

    private static string Csv(string value)
    {
        // Prevent file/folder names being interpreted as formulas when opened in a spreadsheet.
        var trimmed = value.TrimStart();
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0]) || value.Length > 0 && "\t\r\n".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}

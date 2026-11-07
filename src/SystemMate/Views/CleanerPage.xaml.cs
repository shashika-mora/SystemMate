using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SystemMate.ViewModels;

namespace SystemMate.Views;

public sealed partial class CleanerPage : Page
{
    public CleanerViewModel ViewModel { get; }
    private bool _confirming;
    private bool _active;

    public CleanerPage()
    {
        ViewModel = new CleanerViewModel(App.Cleaner);
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _active = true;
        base.OnNavigatedTo(e);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _active = false;
        if (ViewModel.CancelOperationCommand.CanExecute(null)) ViewModel.CancelOperationCommand.Execute(null);
        base.OnNavigatedFrom(e);
    }

    private async void CleanSelectedClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (!ViewModel.CanCleanNow || _confirming || !_active) return;
        _confirming = true;
        try
        {

            if (App.Settings.GetConfirmBeforeDelete())
            {
                var dialog = new ContentDialog
                {
                    Title = "Confirm cleanup",
                    Content = $"This will permanently delete {ViewModel.TotalSizeDisplay} across {ViewModel.TotalFileCount:N0} files.\n\nAn attempt is recorded before each deletion, followed by its actual outcome.",
                    PrimaryButtonText = "Clean selected",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = XamlRoot,
                    RequestedTheme = ActualTheme,
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            }

            if (_active) await ViewModel.CleanCommand.ExecuteAsync(null);
        }
        catch (Exception ex) { ViewModel.StatusMessage = $"Cleanup could not continue: {ex.Message}"; }
        finally { _confirming = false; }
    }
}

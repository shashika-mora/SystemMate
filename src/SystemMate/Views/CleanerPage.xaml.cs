using Microsoft.UI.Xaml.Controls;
using SystemMate.ViewModels;

namespace SystemMate.Views;

public sealed partial class CleanerPage : Page
{
    public CleanerViewModel ViewModel { get; }

    public CleanerPage()
    {
        ViewModel = new CleanerViewModel(App.Cleaner);
        this.InitializeComponent();
    }

    private async void CleanSelectedClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (!ViewModel.CanCleanNow) return;

        // Show confirmation dialog before any deletion
        if (App.Settings.GetConfirmBeforeDelete())
        {
            var dialog = new ContentDialog
            {
                Title             = "Confirm cleanup",
                Content           = $"This will permanently delete {ViewModel.TotalSizeDisplay} across {ViewModel.TotalFileCount:N0} files.\n\nA full history record will be saved before anything is removed.",
                PrimaryButtonText = "Clean selected",
                CloseButtonText   = "Cancel",
                DefaultButton     = ContentDialogButton.Close,
                XamlRoot          = XamlRoot,
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;
        }

        await ViewModel.CleanCommand.ExecuteAsync(null);
    }
}

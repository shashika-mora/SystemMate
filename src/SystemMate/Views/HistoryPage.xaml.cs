using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SystemMate.ViewModels;

namespace SystemMate.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; }

    public HistoryPage()
    {
        ViewModel = new HistoryViewModel(App.History, App.Settings)
        {
            ConfirmClearAsync = ConfirmClearAsync
        };
        this.InitializeComponent();
    }

    private async Task<bool> ConfirmClearAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "Clear history?",
            Content = "This permanently removes all cleanup history.",
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }
}

using Microsoft.UI.Xaml.Controls;
using SystemMate.ViewModels;

namespace SystemMate.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = new SettingsViewModel(App.Settings);
        this.InitializeComponent();
    }
}

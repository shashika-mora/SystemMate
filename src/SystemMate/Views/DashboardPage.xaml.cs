using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SystemMate.ViewModels;

namespace SystemMate.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }

    public DashboardPage()
    {
        ViewModel = new DashboardViewModel(App.SystemInfo);
        this.InitializeComponent();
        SetGreeting();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.StartMonitoring(DispatcherQueue.GetForCurrentThread());
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Dispose();
    }

    private void SetGreeting()
    {
        var hour = DateTime.Now.Hour;
        var greeting = hour switch
        {
            < 12 => "Good morning",
            < 18 => "Good afternoon",
            _    => "Good evening"
        };
        GreetingText.Text = $"{greeting}, {Environment.UserName}";
    }

    private void ScanJunkClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => Frame.Navigate(typeof(CleanerPage));

    private void AnalyzeStorageClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    { /* v0.3 */ }

    private void ReviewStartupClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    { /* v0.2 */ }
}

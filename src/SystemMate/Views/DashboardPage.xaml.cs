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
        => (App.MainWindow as MainWindow)?.NavigateTo("cleaner");

    private void AnalyzeStorageClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => (App.MainWindow as MainWindow)?.NavigateTo("storage");

    private void ReviewStartupClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => (App.MainWindow as MainWindow)?.NavigateTo("startup");

    private void ExploreClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.FrameworkElement { Tag: string tag })
            (App.MainWindow as MainWindow)?.NavigateTo(tag);
    }
}

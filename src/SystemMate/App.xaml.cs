using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SystemMate.Services;
using SystemMate.ViewModels;

namespace SystemMate;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

    // Simple service locator (no DI container dependency in WinUI 3 entry point)
    public static SystemInfoService SystemInfo { get; } = new SystemInfoService();
    public static CleanerService Cleaner { get; } = new CleanerService();
    public static HistoryService History { get; } = new HistoryService();
    public static SettingsService Settings { get; } = new SettingsService();

    public App()
    {
        this.InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}

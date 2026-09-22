using Microsoft.UI.Xaml;
using SystemMate.Services;

namespace SystemMate;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

    // ── Services (initialized in dependency order) ──────────────────────────
    // History must be created before Cleaner (Cleaner logs to History)
    public static HistoryService  History  { get; } = new HistoryService();
    public static SettingsService Settings { get; } = new SettingsService();
    public static SystemInfoService SystemInfo { get; } = new SystemInfoService();

    // CleanerService receives History via constructor — no App.X reference inside service
    public static CleanerService  Cleaner  { get; } = new CleanerService(History);

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

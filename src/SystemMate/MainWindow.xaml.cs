using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SystemMate.Views;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace SystemMate;

public sealed partial class MainWindow : Window
{
    private readonly UISettings _uiSettings = new();
    private readonly AccessibilitySettings _accessibilitySettings = new();
    private AppWindow? _appWindow;
    private Microsoft.UI.System.ThemeSettings? _themeSettings;
    private bool _closed;

    public MainWindow()
    {
        this.InitializeComponent();
        ConfigureWindow();
        if (Content is FrameworkElement root)
            root.ActualThemeChanged += Root_ActualThemeChanged;
        SetTheme();
        App.Settings.ThemeChanged += Settings_ThemeChanged;
        _uiSettings.ColorValuesChanged += SystemColorsChanged;
        Closed += MainWindow_Closed;
        SubtitleText.Text = Environment.OSVersion.Version.Build >= 22000 ? "Windows 11" : "Windows 10";
    }

    private void ConfigureWindow()
    {
        // Set window size and title bar
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = _appWindow = AppWindow.GetFromWindowId(windowId);
        _themeSettings = Microsoft.UI.System.ThemeSettings.CreateForWindowId(windowId);
        _themeSettings.Changed += ContrastSettingsChanged;

        appWindow.Resize(new SizeInt32(1280, 820));
        appWindow.Title = "SystemMate";

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = appWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = false;
        }
    }

    private void Settings_ThemeChanged(object? sender, EventArgs e) => SetTheme();

    private void SetTheme()
    {
        var theme = App.Settings.GetTheme();
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => SystemUsesDarkTheme() ? ElementTheme.Dark : ElementTheme.Light
            };
            UpdateTitleBar(root.ActualTheme);
        }
    }

    private bool SystemUsesDarkTheme()
    {
        // Windows reports a light foreground when applications use dark mode.
        var foreground = _uiSettings.GetColorValue(UIColorType.Foreground);
        return 5 * foreground.G + 2 * foreground.R + foreground.B > 8 * 128;
    }

    private void SystemColorsChanged(UISettings sender, object args)
        => DispatcherQueue.TryEnqueue(() => { if (!_closed) SetTheme(); });

    private void ContrastSettingsChanged(Microsoft.UI.System.ThemeSettings sender, object args)
        => DispatcherQueue.TryEnqueue(() => { if (!_closed) SetTheme(); });

    private void Root_ActualThemeChanged(FrameworkElement sender, object args)
        => UpdateTitleBar(sender.ActualTheme);

    private void UpdateTitleBar(ElementTheme theme)
    {
        if (_appWindow is null || !AppWindowTitleBar.IsCustomizationSupported()) return;

        var titleBar = _appWindow.TitleBar;
        if (_themeSettings?.HighContrast ?? _accessibilitySettings.HighContrast)
        {
            // Clearing overrides lets Windows choose the user's contrast palette.
            titleBar.BackgroundColor = null;
            titleBar.ForegroundColor = null;
            titleBar.ButtonBackgroundColor = null;
            titleBar.ButtonForegroundColor = null;
            titleBar.InactiveBackgroundColor = null;
            titleBar.InactiveForegroundColor = null;
            titleBar.ButtonInactiveBackgroundColor = null;
            titleBar.ButtonInactiveForegroundColor = null;
            titleBar.ButtonHoverBackgroundColor = null;
            titleBar.ButtonHoverForegroundColor = null;
            titleBar.ButtonPressedBackgroundColor = null;
            titleBar.ButtonPressedForegroundColor = null;
            return;
        }

        var dark = theme == ElementTheme.Dark;
        var background = dark ? Windows.UI.Color.FromArgb(255, 18, 28, 41) : Windows.UI.Color.FromArgb(255, 237, 241, 247);
        var foreground = dark ? Windows.UI.Color.FromArgb(255, 243, 246, 252) : Windows.UI.Color.FromArgb(255, 24, 36, 55);
        var muted = dark ? Windows.UI.Color.FromArgb(255, 166, 181, 202) : Windows.UI.Color.FromArgb(255, 92, 107, 128);
        var hover = dark ? Windows.UI.Color.FromArgb(255, 36, 50, 70) : Windows.UI.Color.FromArgb(255, 220, 226, 236);
        titleBar.BackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.InactiveBackgroundColor = background;
        titleBar.InactiveForegroundColor = muted;
        titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ButtonInactiveForegroundColor = muted;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = hover;
        titleBar.ButtonPressedForegroundColor = foreground;
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _closed = true;
        App.Settings.ThemeChanged -= Settings_ThemeChanged;
        _uiSettings.ColorValuesChanged -= SystemColorsChanged;
        if (_themeSettings is not null) _themeSettings.Changed -= ContrastSettingsChanged;
        if (Content is FrameworkElement root)
            root.ActualThemeChanged -= Root_ActualThemeChanged;
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        // Navigate to dashboard on startup
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    public void NavigateTo(string tag)
    {
        var item = NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag as string == tag);
        if (item is not null) NavView.SelectedItem = item;
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage));
            return;
        }

        if (args.SelectedItem is NavigationViewItem item)
        {
            var tag = item.Tag?.ToString();
            var pageType = tag switch
            {
                "dashboard" => typeof(DashboardPage),
                "cleaner"   => typeof(CleanerPage),
                "storage"   => typeof(StoragePage),
                "apps"      => typeof(AppsPage),
                "startup"   => typeof(StartupPage),
                "browsers"  => typeof(BrowserExtensionsPage),
                "performance" => typeof(PerformancePage),
                "tools"     => typeof(ToolsPage),
                "history"   => typeof(HistoryPage),
                _ => typeof(DashboardPage)
            };

            if (ContentFrame.CurrentSourcePageType != pageType)
                ContentFrame.Navigate(pageType);
        }
    }
}

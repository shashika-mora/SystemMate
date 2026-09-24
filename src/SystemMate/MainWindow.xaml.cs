using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SystemMate.Views;
using Windows.Graphics;

namespace SystemMate;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();
        ConfigureWindow();
        SetTheme();
        App.Settings.ThemeChanged += Settings_ThemeChanged;
        Closed += (_, _) => App.Settings.ThemeChanged -= Settings_ThemeChanged;
        SubtitleText.Text = $"Windows {Environment.OSVersion.Version.Major}";
    }

    private void ConfigureWindow()
    {
        // Set window size and title bar
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        appWindow.Resize(new SizeInt32(1200, 750));
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
                _ => ElementTheme.Default
            };
        }
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        // Navigate to dashboard on startup
        NavView.SelectedItem = NavView.MenuItems[0];
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
                "history"   => typeof(HistoryPage),
                _ => typeof(DashboardPage)
            };

            if (ContentFrame.CurrentSourcePageType != pageType)
                ContentFrame.Navigate(pageType);
        }
    }
}

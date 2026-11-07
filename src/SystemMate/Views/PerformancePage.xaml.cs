using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SystemMate.Models;
using SystemMate.Services;

namespace SystemMate.Views;

public sealed partial class PerformancePage : Page
{
    private readonly PerformanceService _service = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private CancellationTokenSource? _lifetime;
    private bool _sampling;
    private Guid? _previousPlan;
    private bool _changingPlan;
    private readonly Queue<double> _cpuHistory = new();

    public PerformancePage()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => await RefreshSampleAsync();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _lifetime = new();
        RefreshPlans();
        _timer.Start();
        await RefreshSampleAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _timer.Stop();
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        _service.Dispose();
        base.OnNavigatedFrom(e);
    }

    private async Task RefreshSampleAsync()
    {
        if (_sampling || _lifetime is null) return;
        _sampling = true;
        var token = _lifetime.Token;
        try
        {
            var sample = await _service.SampleAsync(token);
            if (token.IsCancellationRequested) return;
            ProcessList.ItemsSource = sample.Processes;
            ProcessorText.Text = sample.ProcessorName;
            ProcessorDetailText.Text = $"{sample.LogicalProcessors} logical processors · " +
                (sample.FrequencyMhz is > 0 ? $"{sample.FrequencyMhz:N0} MHz reported clock" : "Clock unavailable");
            CpuUsageText.Text = sample.TotalCpuPercent is double totalCpu ? $"{totalCpu:F1}% CPU" : "CPU counter unavailable";
            if (sample.TotalCpuPercent is double cpu)
            {
                _cpuHistory.Enqueue(cpu);
                while (_cpuHistory.Count > 60) _cpuHistory.Dequeue();
                var values = _cpuHistory.ToArray();
                var points = new Microsoft.UI.Xaml.Media.PointCollection();
                for (var i = 0; i < values.Length; i++) points.Add(new Windows.Foundation.Point(i * 600d / 59, 100 - values[i]));
                CpuLine.Points = points;
            }
            StatusBar.Message = $"Updated {DateTime.Now:T} · process samples refresh every 2 seconds";
            StatusBar.Severity = InfoBarSeverity.Informational;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { StatusBar.Message = ex.Message; StatusBar.Severity = InfoBarSeverity.Warning; }
        finally { _sampling = false; }
    }

    private void RefreshPlans()
    {
        try
        {
            var plans = _service.GetPowerPlans();
            PlanPicker.ItemsSource = plans;
            PlanPicker.SelectedItem = plans.FirstOrDefault(p => p.IsActive);
            ApplyPlanButton.IsEnabled = PlanPicker.SelectedItem is PowerPlan { IsActive: false };
            PlanStatusText.Text = plans.Count == 1 ? "This PC exposes one power plan. Windows power settings may offer additional power modes." : "Choose a different plan, then review the change before applying it.";
        }
        catch (Exception ex) { PlanStatusText.Text = $"Power plans unavailable: {ex.Message}"; ApplyPlanButton.IsEnabled = false; }
    }

    private void PlanSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ApplyPlanButton is not null) ApplyPlanButton.IsEnabled = !_changingPlan && PlanPicker.SelectedItem is PowerPlan { IsActive: false };
    }

    private async void ApplyPlanClick(object sender, RoutedEventArgs e)
    {
        if (PlanPicker.SelectedItem is not PowerPlan plan || plan.IsActive) return;
        await ChangePlanAsync(plan.Id, $"Use {plan.Name}?", "This changes the active Windows power plan. Higher performance can increase power consumption and heat. The previous plan will be available to restore.", false);
    }

    private async void RestorePlanClick(object sender, RoutedEventArgs e)
    {
        if (_previousPlan is not Guid id) return;
        await ChangePlanAsync(id, "Restore previous power plan?", "Windows will return to the power plan active before your first change on this page.", true);
    }

    private async Task ChangePlanAsync(Guid id, string title, string description, bool restoring)
    {
        if (_changingPlan || _lifetime is null) return;
        _changingPlan = true;
        ApplyPlanButton.IsEnabled = RestorePlanButton.IsEnabled = false;
        try
        {
            if (!await ConfirmAsync(title, description) || _lifetime is null) return;
            // Save recovery information before mutation, including cases where verification fails.
            _previousPlan ??= _service.GetActivePlanId();
            _service.SetActivePlan(id);
            if (restoring) _previousPlan = null;
            RefreshPlans();
            PlanStatusText.Text = restoring ? "Previous power plan restored." : "Selected power plan activated.";
        }
        catch (Exception ex)
        {
            RefreshPlans();
            PlanStatusText.Text = $"Could not apply or verify the power plan: {ex.Message} Review the active plan above.";
        }
        finally
        {
            _changingPlan = false;
            ApplyPlanButton.IsEnabled = PlanPicker.SelectedItem is PowerPlan { IsActive: false };
            RestorePlanButton.IsEnabled = _previousPlan.HasValue;
        }
    }

    private async Task<bool> ConfirmAsync(string title, string content) => await new ContentDialog
    {
        Title = title, Content = content, PrimaryButtonText = "Apply", CloseButtonText = "Cancel",
        DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot, RequestedTheme = ActualTheme
    }.ShowAsync() == ContentDialogResult.Primary;

    private async void PowerSettingsClick(object sender, RoutedEventArgs e)
    {
        try { await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:powersleep")); }
        catch (Exception ex) { PlanStatusText.Text = ex.Message; }
    }

    private void TaskManagerClick(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "Taskmgr.exe")) { UseShellExecute = true }); }
        catch (Exception ex) { StatusBar.Message = ex.Message; }
    }
}

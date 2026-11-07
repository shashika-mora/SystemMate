using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SystemMate.Controls;

public sealed partial class HealthRow : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(HealthRow),
            new PropertyMetadata(string.Empty, OnLabelChanged));

    public static readonly DependencyProperty DetailProperty =
        DependencyProperty.Register(nameof(Detail), typeof(string), typeof(HealthRow),
            new PropertyMetadata(string.Empty, OnDetailChanged));

    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(nameof(Status), typeof(string), typeof(HealthRow),
            new PropertyMetadata("good", OnStatusChanged));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Detail
    {
        get => (string)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    /// <summary>Status: "good" | "warn" | "error" | "neutral"</summary>
    public string Status
    {
        get => (string)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public HealthRow()
    {
        this.InitializeComponent();
        UpdateStatus();
    }

    private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((HealthRow)d).LabelText.Text = e.NewValue?.ToString() ?? string.Empty;

    private static void OnDetailChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((HealthRow)d).DetailText.Text = e.NewValue?.ToString() ?? string.Empty;

    private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((HealthRow)d).UpdateStatus();

    private void UpdateStatus()
    {
        var state = Status switch
        {
            "good" => "Good",
            "warn" => "Warning",
            "error" => "Error",
            _ => "Neutral"
        };
        VisualStateManager.GoToState(this, state, false);
    }
}

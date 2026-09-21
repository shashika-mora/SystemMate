using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

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
    }

    private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((HealthRow)d).LabelText.Text = e.NewValue?.ToString() ?? string.Empty;

    private static void OnDetailChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((HealthRow)d).DetailText.Text = e.NewValue?.ToString() ?? string.Empty;

    private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var row = (HealthRow)d;
        var status = e.NewValue?.ToString() ?? "neutral";

        var (color, symbol) = status switch
        {
            "good"    => (Colors.LimeGreen, Symbol.Accept),
            "warn"    => (Colors.Orange,    Symbol.Important),
            "error"   => (Colors.OrangeRed, Symbol.Cancel),
            _         => (Colors.Gray,      Symbol.Help),
        };

        var brush = new SolidColorBrush(color);
        row.StatusDot.Fill     = brush;
        row.StatusIcon.Foreground = brush;
        row.StatusIcon.Symbol  = symbol;
    }
}

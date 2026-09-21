using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace SystemMate.Helpers;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var flag = value is bool b && b;
        if (parameter is string s && s == "Inverse") flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility v && v == Visibility.Visible;
}

public sealed class PercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is double d ? $"{d:F0}%" : "—";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public sealed class ByteSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not long bytes) return "—";
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
            >= 1_048_576     => $"{bytes / 1_048_576.0:F0} MB",
            >= 1_024         => $"{bytes / 1_024.0:F0} KB",
            _                => $"{bytes} B"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public sealed class DateTimeDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is DateTime dt ? dt.ToString("ddd, MMM d yyyy  HH:mm") : "—";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

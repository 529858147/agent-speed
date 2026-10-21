using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AgentSpeed.Controls;

[ValueConversion(typeof(bool), typeof(Visibility))]
internal sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}

[ValueConversion(typeof(bool), typeof(double))]
internal sealed class DimOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? 0.5 : 1.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

[ValueConversion(typeof(bool), typeof(Thickness))]
internal sealed class SubagentIndentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? new Thickness(12, 0, 0, 0) : new Thickness(0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

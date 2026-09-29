using System.Globalization;
using System.Windows.Data;

namespace ClaudeUsageTracker.App.Views.Converters;

/// <summary>
/// Binds a group of RadioButtons to one enum property: IsChecked is true when the
/// value equals ConverterParameter (the member name), and checking a button sets it.
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value?.ToString() == parameter as string;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true && parameter is string name ? Enum.Parse(targetType, name) : Binding.DoNothing;
}

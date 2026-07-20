using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.App.Views.Converters;

public sealed class UsageStatusToBrushConverter : IValueConverter
{
    private static readonly System.Windows.Media.Brush SafeBrush = new SolidColorBrush(Color.FromRgb(52, 199, 89));
    private static readonly System.Windows.Media.Brush ModerateBrush = new SolidColorBrush(Color.FromRgb(255, 149, 0));
    private static readonly System.Windows.Media.Brush CriticalBrush = new SolidColorBrush(Color.FromRgb(255, 59, 48));

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            UsageStatusLevel.Safe => SafeBrush,
            UsageStatusLevel.Moderate => ModerateBrush,
            UsageStatusLevel.Critical => CriticalBrush,
            _ => SafeBrush
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

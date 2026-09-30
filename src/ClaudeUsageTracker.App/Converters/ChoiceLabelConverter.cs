using System.Globalization;
using System.Windows.Data;
using ClaudeUsageTracker.App.Localization;
using ClaudeUsageTracker.App.ViewModels;

namespace ClaudeUsageTracker.App.Converters;

/// <summary>
/// Label for a <see cref="ChoiceOption"/> in a drop-down. Used in a MultiBinding whose second
/// binding is any <see cref="Loc"/> indexer entry: that binding changes on every language switch,
/// which re-runs this converter so the labels (including the closed box's) follow the language.
/// </summary>
public sealed class ChoiceLabelConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
        => values[0] is ChoiceOption option ? option.Label : string.Empty;

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

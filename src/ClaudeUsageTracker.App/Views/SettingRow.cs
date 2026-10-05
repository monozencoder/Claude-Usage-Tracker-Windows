using System.Windows;
using System.Windows.Controls;

namespace ClaudeUsageTracker.App.Views;

/// <summary>
/// One row of a settings group: a title and a description on the left, and the row's control —
/// this element's content — on the right. The layout is its style's, in Themes/SettingsStyles.xaml.
/// <para>
/// <see cref="TitleProperty"/> and <see cref="DescriptionProperty"/> are attached properties so
/// that a toggle row, which is a CheckBox drawn as the whole row rather than a control inside
/// one, can carry the same two texts.
/// </para>
/// </summary>
public class SettingRow : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.RegisterAttached(
        "Title", typeof(string), typeof(SettingRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.RegisterAttached(
        "Description", typeof(string), typeof(SettingRow), new PropertyMetadata(string.Empty));

    public string Title
    {
        get => GetTitle(this);
        set => SetTitle(this, value);
    }

    public string Description
    {
        get => GetDescription(this);
        set => SetDescription(this, value);
    }

    public static string GetTitle(DependencyObject element) => (string)element.GetValue(TitleProperty);

    public static void SetTitle(DependencyObject element, string value) => element.SetValue(TitleProperty, value);

    public static string GetDescription(DependencyObject element) => (string)element.GetValue(DescriptionProperty);

    public static void SetDescription(DependencyObject element, string value) => element.SetValue(DescriptionProperty, value);
}

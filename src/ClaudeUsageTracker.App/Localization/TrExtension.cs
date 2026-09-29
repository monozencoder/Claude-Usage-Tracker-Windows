using System.Windows.Data;
using System.Windows.Markup;

namespace ClaudeUsageTracker.App.Localization;

/// <summary>
/// <c>{loc:Tr Settings_Save}</c> — a one-way binding to <see cref="Loc"/>'s indexer, so the
/// text follows language changes live.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
        => new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}

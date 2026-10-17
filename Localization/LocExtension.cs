using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;

namespace AIUsageMonitor.Localization;

[ContentProperty(nameof(Key))]
[AcceptEmptyServiceProvider]
public sealed class LocExtension : IMarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public object ProvideValue(IServiceProvider serviceProvider) => LocalizationManager.Text(Key);
}

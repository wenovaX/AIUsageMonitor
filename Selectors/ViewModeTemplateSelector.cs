using AIUsageMonitor.Models;

namespace AIUsageMonitor.Selectors;

public class ViewModeTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ColumnTemplate { get; set; }
    public DataTemplate? ListTemplate { get; set; }
    public DataTemplate? CompactTemplate { get; set; }

    public string CurrentMode { get; set; } = "Columns";

    protected override DataTemplate? OnSelectTemplate(object item, BindableObject container)
    {
        return CurrentMode switch
        {
            "List" => ListTemplate,
            "Compact" => CompactTemplate,
            _ => ColumnTemplate
        };
    }
}

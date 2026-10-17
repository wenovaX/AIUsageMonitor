using System.Collections.ObjectModel;

namespace AIUsageMonitor.Models;

public sealed class ModelCatalogSection : ObservableCollection<DiscoveredModelEntry>
{
    public string Title { get; }

    public ModelCatalogSection(string title, IEnumerable<DiscoveredModelEntry> entries)
        : base(entries)
    {
        Title = title;
    }
}

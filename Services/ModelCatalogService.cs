using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using AIUsageMonitor.Models;

namespace AIUsageMonitor.Services;

public class ModelCatalogService
{
    public static readonly string[] DefaultDisplayOrder =
    {
        "Gemini 3.1 Pro (High)",
        "Gemini 3.1 Pro (Low)",
        "Gemini 3 Flash",
        "Claude Sonnet 4.6 (Thinking)",
        "Claude Opus 4.6 (Thinking)",
        "GPT-OSS 120B (Medium)"
    };

    public static ModelCatalogService? Instance { get; private set; }

    private readonly string _configPath;
    private bool _isInternalChange;
    private bool _suspendCatalogChanged;
    private bool _hasPendingCatalogChanged;

    public ObservableCollection<DiscoveredModelEntry> Models { get; } = new();

    public event EventHandler? CatalogChanged;

    public ModelCatalogService()
    {
        Instance = this;
        _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIUsageMonitor",
            "discovered_models.json");

        Models.CollectionChanged += OnModelsCollectionChanged;
        Load();
    }

    public bool IsEnabled(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return false;

        var match = Models.FirstOrDefault(model =>
            string.Equals(model.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));

        return match?.Enabled ?? false;
    }

    public int GetSortOrder(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return int.MaxValue;

        var index = Models
            .Select((model, i) => new { model.DisplayName, Index = i })
            .FirstOrDefault(x => string.Equals(x.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));

        return index?.Index ?? int.MaxValue;
    }

    public bool MergeDiscoveredModels(IEnumerable<string> displayNames)
    {
        var discoveredNames = displayNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (discoveredNames.Count == 0)
            return false;

        var updated = Models
            .Select(Clone)
            .ToList();

        var existingNames = updated
            .Select(model => model.DisplayName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var name in discoveredNames)
        {
            if (existingNames.Add(name))
            {
                updated.Add(new DiscoveredModelEntry
                {
                    DisplayName = name,
                    Enabled = true
                });
            }
        }

        updated = OrderModels(updated).ToList();
        if (HasSameModelState(updated))
            return false;

        ApplyModels(updated);
        return true;
    }

    public bool UpdateAvailableModels(IEnumerable<string> displayNames)
    {
        var discoveredNames = displayNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var updated = Models.Select(Clone).ToList();

        foreach (var defaultName in DefaultDisplayOrder)
        {
            if (updated.Any(model => string.Equals(model.DisplayName, defaultName, StringComparison.OrdinalIgnoreCase)))
                continue;

            updated.Add(new DiscoveredModelEntry
            {
                DisplayName = defaultName,
                Enabled = true,
                IsAvailable = discoveredNames.Contains(defaultName)
            });
        }

        var existingNames = updated
            .Select(model => model.DisplayName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var name in discoveredNames)
        {
            if (existingNames.Add(name))
            {
                updated.Add(new DiscoveredModelEntry
                {
                    DisplayName = name,
                    Enabled = false,
                    IsAvailable = true
                });
            }
        }

        foreach (var entry in updated)
        {
            entry.IsAvailable = discoveredNames.Contains(entry.DisplayName);
        }

        updated = OrderModels(updated).ToList();
        if (HasSameModelState(updated, includeAvailability: true))
            return false;

        ApplyModels(updated);
        return true;
    }

    public void ResetToDefaults()
    {
        var defaults = DefaultDisplayOrder.Select(name => new DiscoveredModelEntry
        {
            DisplayName = name,
            Enabled = true,
            IsAvailable = true
        }).ToList();

        if (HasSameModelState(defaults))
            return;

        ApplyModels(defaults);
    }

    public void SuspendCatalogChangedNotifications()
    {
        _suspendCatalogChanged = true;
    }

    public void ResumeCatalogChangedNotifications()
    {
        _suspendCatalogChanged = false;

        if (_hasPendingCatalogChanged)
        {
            _hasPendingCatalogChanged = false;
            CatalogChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<DiscoveredModelConfig>(json);
                if (config is not null && config.Models.Count > 0)
                {
                    ApplyModels(OrderModels(Sanitize(config.Models)), saveAfter: false);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Load error", ex);
        }

        ApplyModels(DefaultDisplayOrder.Select(name => new DiscoveredModelEntry
        {
            DisplayName = name,
            Enabled = true,
            IsAvailable = true
        }), saveAfter: true);
    }

    private void Save()
    {
        if (_isInternalChange)
            return;

        try
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var config = new DiscoveredModelConfig
            {
                Models = Models.Select(Clone).ToList()
            };

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            Log.Error("Save error", ex);
        }
    }

    private void OnModelsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (DiscoveredModelEntry item in e.OldItems)
                item.PropertyChanged -= OnModelPropertyChanged;
        }

        if (e.NewItems != null)
        {
            foreach (DiscoveredModelEntry item in e.NewItems)
                item.PropertyChanged += OnModelPropertyChanged;
        }

        Save();
        PublishCatalogChanged();
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiscoveredModelEntry.Enabled))
        {
            Save();
            PublishCatalogChanged();
        }
    }

    private void ApplyModels(IEnumerable<DiscoveredModelEntry> entries, bool saveAfter = true)
    {
        var snapshot = entries.Select(Clone).ToList();

        _isInternalChange = true;
        try
        {
            Models.Clear();
            foreach (var entry in snapshot)
                Models.Add(entry);
        }
        finally
        {
            _isInternalChange = false;
        }

        if (saveAfter)
            Save();

        PublishCatalogChanged();
    }

    private void PublishCatalogChanged()
    {
        if (_suspendCatalogChanged)
        {
            _hasPendingCatalogChanged = true;
            return;
        }

        CatalogChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool HasSameModelState(IReadOnlyList<DiscoveredModelEntry> updated, bool includeAvailability = false)
    {
        if (updated.Count != Models.Count)
            return false;

        for (var i = 0; i < updated.Count; i++)
        {
            var current = Models[i];
            var next = updated[i];
            if (!string.Equals(current.DisplayName, next.DisplayName, StringComparison.OrdinalIgnoreCase))
                return false;

            if (current.Enabled != next.Enabled)
                return false;

            if (includeAvailability && current.IsAvailable != next.IsAvailable)
                return false;
        }

        return true;
    }

    private static IEnumerable<DiscoveredModelEntry> Sanitize(IEnumerable<DiscoveredModelEntry> entries)
    {
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var displayName = entry.DisplayName?.Trim();
            if (string.IsNullOrWhiteSpace(displayName) || !seenNames.Add(displayName))
                continue;

            yield return new DiscoveredModelEntry
            {
                DisplayName = displayName,
                Enabled = entry.Enabled,
                IsAvailable = entry.IsAvailable
            };
        }
    }

    private static IEnumerable<DiscoveredModelEntry> OrderModels(IEnumerable<DiscoveredModelEntry> entries)
    {
        return entries
            .OrderBy(entry =>
            {
                var index = Array.FindIndex(DefaultDisplayOrder,
                    name => string.Equals(name, entry.DisplayName, StringComparison.OrdinalIgnoreCase));
                return index >= 0 ? index : int.MaxValue;
            })
            .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase);
    }

private static DiscoveredModelEntry Clone(DiscoveredModelEntry entry) => new()
{
    DisplayName = entry.DisplayName,
    Enabled = entry.Enabled,
    IsAvailable = entry.IsAvailable
};
}

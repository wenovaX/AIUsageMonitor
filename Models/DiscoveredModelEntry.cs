using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using AIUsageMonitor.Services;

namespace AIUsageMonitor.Models;

public class DiscoveredModelEntry : INotifyPropertyChanged
{
    private string _displayName = string.Empty;
    private bool _enabled = true;
    private bool _isAvailable = true;

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (_displayName == value)
                return;

            _displayName = value;
            OnPropertyChanged();
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;

            _enabled = value;
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public bool IsAvailable
    {
        get => _isAvailable;
        set
        {
            if (_isAvailable == value)
                return;

            _isAvailable = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMissing));
        }
    }

    [JsonIgnore]
    public bool IsGemini => DisplayName.Contains("Gemini", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsDefault => ModelCatalogService.DefaultDisplayOrder.Any(name =>
        string.Equals(name, DisplayName, StringComparison.OrdinalIgnoreCase));

    [JsonIgnore]
    public bool IsMissing => !IsAvailable;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class DiscoveredModelConfig
{
    [JsonPropertyName("models")]
    public List<DiscoveredModelEntry> Models { get; set; } = new();
}

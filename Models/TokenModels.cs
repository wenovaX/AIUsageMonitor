using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace AIUsageMonitor.Models;

public class CloudAccount : INotifyPropertyChanged
{
    private ObservableCollection<ModelQuotaInfo> _quotas = new();
    private double _credits;
    private string _access_token = "";
    private ObservableCollection<string> _hiddenModels = new();
    private bool _isSettingsOpen;
    private bool _isAnonymous;

    public string id { get; set; } = Guid.NewGuid().ToString();
    public string name { get; set; } = "";
    public string email { get; set; } = "";
    public string avatar_url { get; set; } = "";
    public string provider { get; set; } = "Google";
    
    public string access_token 
    { 
        get => _access_token; 
        set { _access_token = value; OnPropertyChanged(); } 
    }
    
    public string? refresh_token { get; set; }
    public DateTime? expires_at { get; set; }
    public DateTime? last_updated { get; set; }

    public double credits 
    { 
        get => _credits; 
        set { _credits = value; OnPropertyChanged(); } 
    }

    [JsonIgnore]
    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set { _isSettingsOpen = value; OnPropertyChanged(); }
    }

    // For Simple Binding to avoid MultiBinding errors
    public bool IsAnonymous
    {
        get => _isAnonymous;
        set 
        { 
            _isAnonymous = value; 
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(DisplayEmail));
        }
    }

    [JsonIgnore]
    public string DisplayName => IsAnonymous ? $"Account {DisplayIndex + 1}" : name;
    
    [JsonIgnore]
    public string DisplayEmail => IsAnonymous ? provider : email;

    public ObservableCollection<string> HiddenModels
    {
        get => _hiddenModels;
        set { _hiddenModels = value; OnPropertyChanged(); NotifyQuotaChanges(); }
    }

    public ObservableCollection<ModelQuotaInfo> quotas 
    { 
        get => _quotas; 
        set { 
            _quotas = value; 
            OnPropertyChanged(); 
            NotifyQuotaChanges();
            OnPropertyChanged(nameof(VisibilityItems));
        } 
    }

    public void NotifyQuotaChanges()
    {
        OnPropertyChanged(nameof(GeminiQuotas)); 
        OnPropertyChanged(nameof(ClaudeQuotas)); 
        OnPropertyChanged(nameof(OtherQuotas)); 
        OnPropertyChanged(nameof(HasGeminiQuotas)); 
        OnPropertyChanged(nameof(HasClaudeQuotas)); 
        OnPropertyChanged(nameof(HasOtherQuotas));
        OnPropertyChanged(nameof(VisibilityItems));
    }

    private static readonly List<string> SortOrder = new()
    {
        "Gemini 3 Flash",
        "Gemini 3.1 Pro (High)",
        "Gemini 3.1 Pro (Low)",
        "Claude Opus 4.6",
        "Claude Sonnet 4.6",
        "GPT-OSS 120B"
    };

    private IEnumerable<ModelQuotaInfo> GetSortedFilteredQuotas(string category)
    {
        var filtered = quotas.Where(q => !HiddenModels.Contains(q.display_name));
        
        if (category == "Gemini")
            filtered = filtered.Where(q => q.display_name.Contains("Gemini"));
        else if (category == "Claude")
            filtered = filtered.Where(q => q.display_name.Contains("Claude") || q.display_name.Contains("Anthropic"));
        else
            filtered = filtered.Where(q => !q.display_name.Contains("Gemini") && !q.display_name.Contains("Claude") && !q.display_name.Contains("Anthropic"));

        return filtered.OrderBy(q => {
            int idx = SortOrder.IndexOf(q.display_name);
            return idx == -1 ? 999 : idx;
        });
    }

    [JsonIgnore]
    public IEnumerable<ModelQuotaInfo> GeminiQuotas => GetSortedFilteredQuotas("Gemini");
    
    [JsonIgnore]
    public IEnumerable<ModelQuotaInfo> ClaudeQuotas => GetSortedFilteredQuotas("Claude");
    
    [JsonIgnore]
    public IEnumerable<ModelQuotaInfo> OtherQuotas => GetSortedFilteredQuotas("Other");

    [JsonIgnore]
    public IEnumerable<ModelVisibilityItem> VisibilityItems => quotas.Select(q => new ModelVisibilityItem 
    { 
        DisplayName = q.display_name, 
        IsVisible = !HiddenModels.Contains(q.display_name),
        ParentAccount = this
    }).OrderBy(v => {
        int idx = SortOrder.IndexOf(v.DisplayName);
        return idx == -1 ? 999 : idx;
    });

    [JsonIgnore]
    public bool HasGeminiQuotas => GeminiQuotas.Any();
    
    [JsonIgnore]
    public bool HasClaudeQuotas => ClaudeQuotas.Any();
    
    [JsonIgnore]
    public bool HasOtherQuotas => OtherQuotas.Any();

    [JsonIgnore]
    public int DisplayIndex 
    { 
        get => _displayIndex; 
        set { _displayIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); } 
    }
    private int _displayIndex;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class ModelVisibilityItem : INotifyPropertyChanged
{
    public string DisplayName { get; set; } = "";
    private bool _isVisible;
    public bool IsVisible 
    { 
        get => _isVisible; 
        set 
        { 
            if (_isVisible == value) return;
            _isVisible = value; 
            OnPropertyChanged();
            
            if (_isVisible) ParentAccount?.HiddenModels.Remove(DisplayName);
            else if (!(ParentAccount?.HiddenModels.Contains(DisplayName) ?? true)) ParentAccount?.HiddenModels.Add(DisplayName);
            
            ParentAccount?.NotifyQuotaChanges();
        } 
    }
    
    [JsonIgnore]
    public CloudAccount? ParentAccount { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class ModelQuotaInfo : INotifyPropertyChanged
{
    private int _percentage;
    private string _resetTime = "";

    public string display_name { get; set; } = "";
    
    public int percentage 
    { 
        get => _percentage; 
        set { _percentage = value; OnPropertyChanged(); } 
    }
    
    public string resetTime 
    { 
        get => _resetTime; 
        set { _resetTime = value; OnPropertyChanged(); } 
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class QuotaData
{
    public Dictionary<string, ModelQuotaInfo> models { get; set; } = new();
}

public class TokenResponse
{
    public string access_token { get; set; } = "";
    public string refresh_token { get; set; } = "";
    public int expires_in { get; set; }
}

public class UserInfo
{
    public string name { get; set; } = "";
    public string email { get; set; } = "";
    public string picture { get; set; } = "";
}

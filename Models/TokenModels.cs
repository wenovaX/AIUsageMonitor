using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using System.Diagnostics;
using System.Net.Http;
using AIUsageMonitor.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace AIUsageMonitor.Models;

public class CloudAccount : INotifyPropertyChanged
{
    private static readonly HttpClient AvatarHttpClient = CreateAvatarHttpClient();

    private ObservableCollection<ModelQuotaInfo> _quotas = new();
    private double _credits;
    private string _access_token = "";
    private ObservableCollection<string> _hiddenModels = new();
    private string _avatarUrl = "";
    private bool _isSettingsOpen;
    private bool _isAnonymous;
    private bool _isRefreshing;
    private bool _isRefreshQueued;
    private bool _hasError;
    private bool _hasAvatarImage;
    private double _cardHeightHint = 320;
    private ImageSource? _avatarImageSource;
    private int _avatarLoadVersion;

    public string id { get; set; } = Guid.NewGuid().ToString();
    public string name { get; set; } = "";
    public string email { get; set; } = "";
    public string avatar_url
    {
        get => _avatarUrl;
        set
        {
            if (_avatarUrl == value)
                return;

            _avatarUrl = value;
            OnPropertyChanged();
            _ = LoadAvatarImageAsync(value);
        }
    }
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

    [JsonIgnore]
    public bool IsRefreshing
    {
        get => _isRefreshing;
        set { _isRefreshing = value; OnPropertyChanged(); }
    }

    [JsonIgnore]
    public bool IsRefreshQueued
    {
        get => _isRefreshQueued;
        set { _isRefreshQueued = value; OnPropertyChanged(); }
    }

    [JsonIgnore]
    public bool HasError
    {
        get => _hasError;
        set { _hasError = value; OnPropertyChanged(); }
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

    [JsonIgnore]
    public ImageSource? AvatarImageSource
    {
        get => _avatarImageSource;
        private set
        {
            _avatarImageSource = value;
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public bool HasAvatarImage
    {
        get => _hasAvatarImage;
        private set
        {
            _hasAvatarImage = value;
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public double CardHeightHint
    {
        get => _cardHeightHint;
        set
        {
            _cardHeightHint = value;
            OnPropertyChanged();
        }
    }

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

    private IReadOnlyList<ModelQuotaInfo> GetSortedFilteredQuotas(string category)
    {
        var quotaSnapshot = quotas.ToList();
        var hiddenModels = HiddenModels.ToHashSet(StringComparer.OrdinalIgnoreCase);
        IEnumerable<ModelQuotaInfo> filtered = quotaSnapshot.Where(q =>
            !hiddenModels.Contains(q.display_name) &&
            (ModelCatalogService.Instance?.IsEnabled(q.display_name) ?? true));
        
        if (category == "Gemini")
            filtered = filtered.Where(q => q.display_name.Contains("Gemini"));
        else if (category == "Claude")
            filtered = filtered.Where(q => q.display_name.Contains("Claude") || q.display_name.Contains("Anthropic"));
        else
            filtered = filtered.Where(q => !q.display_name.Contains("Gemini"));

        return filtered.OrderBy(q => {
            var idx = ModelCatalogService.Instance?.GetSortOrder(q.display_name) ?? int.MaxValue;
            return idx == int.MaxValue ? 999 : idx;
        }).ToList();
    }

    [JsonIgnore]
    public IReadOnlyList<ModelQuotaInfo> GeminiQuotas => GetSortedFilteredQuotas("Gemini");
    
    [JsonIgnore]
    public IReadOnlyList<ModelQuotaInfo> ClaudeQuotas => GetSortedFilteredQuotas("Claude");
    
    [JsonIgnore]
    public IReadOnlyList<ModelQuotaInfo> OtherQuotas => GetSortedFilteredQuotas("Other");

    [JsonIgnore]
    public IReadOnlyList<ModelVisibilityItem> VisibilityItems
    {
        get
        {
            var hiddenModels = HiddenModels.ToHashSet(StringComparer.OrdinalIgnoreCase);

            return quotas
                .ToList()
                .Where(q => ModelCatalogService.Instance?.IsEnabled(q.display_name) ?? true)
                .Select(q => new ModelVisibilityItem
                {
                    DisplayName = q.display_name,
                    IsVisible = !hiddenModels.Contains(q.display_name),
                    ParentAccount = this
                })
                .OrderBy(v =>
                {
                    var idx = ModelCatalogService.Instance?.GetSortOrder(v.DisplayName) ?? int.MaxValue;
                    return idx == int.MaxValue ? 999 : idx;
                })
                .ToList();
        }
    }

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

    private async Task LoadAvatarImageAsync(string? avatarUrl)
    {
        var version = Interlocked.Increment(ref _avatarLoadVersion);

        if (string.IsNullOrWhiteSpace(avatarUrl))
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (version != _avatarLoadVersion)
                    return;

                AvatarImageSource = null;
                HasAvatarImage = false;
            });
            return;
        }

        try
        {
            var bytes = await AvatarHttpClient.GetByteArrayAsync(avatarUrl);
            if (version != _avatarLoadVersion)
                return;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (version != _avatarLoadVersion)
                    return;

                AvatarImageSource = ImageSource.FromStream(() => new MemoryStream(bytes));
                HasAvatarImage = true;
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Avatar] Failed to load {avatarUrl}: {ex.Message}");

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (version != _avatarLoadVersion)
                    return;

                AvatarImageSource = null;
                HasAvatarImage = false;
            });
        }
    }

    private static HttpClient CreateAvatarHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 AIUsageMonitor");
        return client;
    }
}

public class GoogleAccountRow
{
    public ObservableCollection<CloudAccount> Accounts { get; } = new();
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

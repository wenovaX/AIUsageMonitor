using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIUsageMonitor.Models;

public class CursorAccount : INotifyPropertyChanged
{
    private string _name = "Cursor Account";
    private string _email = "";
    private string _sessionToken = "";
    private string _loginEmail = "";
    private string _loginPassword = "";
    private int _monthlyUsed;
    private int _monthlyLimit = 500;
    private double _contextUsagePercent;
    private string _contextComposerName = "";
    private DateTime? _contextResetDate;
    private bool _contextBottomHit;
    private bool _isRefreshing;
    private bool _isRefreshQueued;
    private bool _hasError;
    private string _lastErrorMessage = "";

    public string id { get; set; } = Guid.NewGuid().ToString();

    public string name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); }
    }

    public string email
    {
        get => _email;
        set { _email = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayEmail)); }
    }

    public string session_token
    {
        get => _sessionToken;
        set { _sessionToken = value; OnPropertyChanged(); }
    }

    public string login_email
    {
        get => _loginEmail;
        set { _loginEmail = value; OnPropertyChanged(); }
    }

    public string login_password
    {
        get => _loginPassword;
        set { _loginPassword = value; OnPropertyChanged(); }
    }

    public int monthly_used
    {
        get => _monthlyUsed;
        set
        {
            _monthlyUsed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MonthlyRemaining));
            OnPropertyChanged(nameof(MonthlyUsedPercent));
            OnPropertyChanged(nameof(MonthlyDisplay));
            OnPropertyChanged(nameof(RemainingDisplay));
        }
    }

    public int monthly_limit
    {
        get => _monthlyLimit;
        set
        {
            _monthlyLimit = value <= 0 ? 1 : value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MonthlyRemaining));
            OnPropertyChanged(nameof(MonthlyUsedPercent));
            OnPropertyChanged(nameof(MonthlyDisplay));
            OnPropertyChanged(nameof(RemainingDisplay));
        }
    }

    public double context_usage_percent
    {
        get => _contextUsagePercent;
        set
        {
            _contextUsagePercent = Math.Clamp(value, 0, 100);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ContextUsageProgress));
            OnPropertyChanged(nameof(ContextUsageDisplay));
            OnPropertyChanged(nameof(ContextRemainingDisplay));
        }
    }

    public string context_composer_name
    {
        get => _contextComposerName;
        set { _contextComposerName = value; OnPropertyChanged(); OnPropertyChanged(nameof(ContextTitle)); }
    }

    public DateTime? context_reset_date
    {
        get => _contextResetDate;
        set
        {
            _contextResetDate = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ContextResetDisplay));
        }
    }

    public bool context_bottom_hit
    {
        get => _contextBottomHit;
        set
        {
            _contextBottomHit = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ContextStatusDisplay));
        }
    }

    public DateTime? last_updated { get; set; }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        set { _isRefreshing = value; OnPropertyChanged(); }
    }

    public bool IsRefreshQueued
    {
        get => _isRefreshQueued;
        set { _isRefreshQueued = value; OnPropertyChanged(); }
    }

    public bool HasError
    {
        get => _hasError;
        set { _hasError = value; OnPropertyChanged(); }
    }

    public string LastErrorMessage
    {
        get => _lastErrorMessage;
        set { _lastErrorMessage = value; OnPropertyChanged(); }
    }

    public string DisplayName => string.IsNullOrWhiteSpace(name) ? "Cursor Account" : name;
    public string DisplayEmail => string.IsNullOrWhiteSpace(email) ? "cursor.sh" : email;
    public int MonthlyRemaining => Math.Max(0, monthly_limit - monthly_used);
    public int MonthlyUsedPercent => monthly_limit <= 0 ? 0 : Math.Min(100, (int)Math.Round(monthly_used * 100.0 / monthly_limit));
    public string MonthlyDisplay => $"{monthly_used}/{monthly_limit}";
    public string RemainingDisplay => $"{MonthlyRemaining} left";
    public double ContextUsageProgress => context_usage_percent / 100.0;
    public string ContextUsageDisplay => $"{context_usage_percent:F1}%";
    public string ContextRemainingDisplay => $"{Math.Max(0, 100 - context_usage_percent):F1}% left";
    public string ContextTitle => string.IsNullOrWhiteSpace(context_composer_name)
        ? "Context Usage"
        : $"Context Usage - {context_composer_name}";
    public string ContextResetDisplay => context_reset_date.HasValue
        ? $"Reset {context_reset_date.Value.ToLocalTime():MM/dd HH:mm}"
        : "Reset unknown";
    public string ContextStatusDisplay => context_bottom_hit ? "Bottom hit" : "Tracking";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

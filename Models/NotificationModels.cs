using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace AIUsageMonitor.Models;

public class NotificationSettings : INotifyPropertyChanged
{
    private bool _enableResetNotifications = true;
    private bool _enableLimitNotifications = true;
    private int _limitThresholdPercent = 90;
    private bool _enableSlackNotifications = false;
    private string _slackWebhookUrl = string.Empty;
    private string _slackStatus = "Ready";
    private Color _slackStatusColor = Color.FromArgb("#64748b"); // Slate 500

    private bool _enableBackgroundRefresh = false;
    private int _refreshIntervalMinutes = 30;

    public bool EnableBackgroundRefresh
    {
        get => _enableBackgroundRefresh;
        set { _enableBackgroundRefresh = value; OnPropertyChanged(); }
    }

    public int RefreshIntervalMinutes
    {
        get => _refreshIntervalMinutes;
        set { _refreshIntervalMinutes = value; OnPropertyChanged(); }
    }

    public bool EnableResetNotifications
    {
        get => _enableResetNotifications;
        set { _enableResetNotifications = value; OnPropertyChanged(); }
    }

    public bool EnableLimitNotifications
    {
        get => _enableLimitNotifications;
        set { _enableLimitNotifications = value; OnPropertyChanged(); }
    }

    public int LimitThresholdPercent
    {
        get => _limitThresholdPercent;
        set { _limitThresholdPercent = value; OnPropertyChanged(); }
    }

    public bool EnableSlackNotifications
    {
        get => _enableSlackNotifications;
        set { _enableSlackNotifications = value; OnPropertyChanged(); }
    }

    public string SlackWebhookUrl
    {
        get => _slackWebhookUrl;
        set { _slackWebhookUrl = value; OnPropertyChanged(); }
    }

    [JsonIgnore]
    public string SlackStatus
    {
        get => _slackStatus;
        set { _slackStatus = value; OnPropertyChanged(); }
    }

    [JsonIgnore]
    public Color SlackStatusColor
    {
        get => _slackStatusColor;
        set { _slackStatusColor = value; OnPropertyChanged(); }
    }

    // ── Scheduled Reset Alerts (Bot Token) ──
    private bool _enableScheduledAlerts = false;
    private string _slackBotToken = string.Empty;
    private string _slackChannelId = string.Empty;
    private string _scheduledStatus = "Ready";
    private Color _scheduledStatusColor = Color.FromArgb("#64748b");

    public bool EnableScheduledAlerts
    {
        get => _enableScheduledAlerts;
        set { _enableScheduledAlerts = value; OnPropertyChanged(); }
    }

    public string SlackBotToken
    {
        get => _slackBotToken;
        set { _slackBotToken = value; OnPropertyChanged(); }
    }

    public string SlackChannelId
    {
        get => _slackChannelId;
        set { _slackChannelId = value; OnPropertyChanged(); }
    }

    [JsonIgnore]
    public string ScheduledStatus
    {
        get => _scheduledStatus;
        set { _scheduledStatus = value; OnPropertyChanged(); }
    }

    [JsonIgnore]
    public Color ScheduledStatusColor
    {
        get => _scheduledStatusColor;
        set { _scheduledStatusColor = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Slack 다이제스트에 전달할 계정 상태 요약 항목
/// </summary>
public record AccountDigestEntry(
    string Provider,        // "Codex", "Google", "Cursor"
    string AccountName,     // 이메일 또는 계정 식별자
    int UsagePercent,       // 현재 사용량 (%)
    bool IsAvailable,       // 사용 가능 여부
    long ResetAt            // 리셋 시각 (Unix timestamp)
)
{
    public string ResetTimeFormatted =>
        ResetAt > 0
            ? DateTimeOffset.FromUnixTimeSeconds(ResetAt).ToLocalTime().ToString("MM/dd HH:mm")
            : "N/A";
}

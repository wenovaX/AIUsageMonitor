using AIUsageMonitor.Models;
using System.Text.Json;

namespace AIUsageMonitor.Services;

public class NotificationService
{
    private const string FileName = "notification_settings.json";
    private readonly string _filePath;
    private readonly HttpClient _httpClient = new();
    private readonly Dictionary<string, long> _lastNotifiedResetTimes = new();
    private string _lastDigestHash = string.Empty;
    public NotificationSettings Settings { get; private set; } = new();

    public event Action<string, string>? OnNotificationTriggered;

    public NotificationService()
    {
        _filePath = AppDataPaths.GetDataFilePath(FileName, FileSystem.AppDataDirectory);
        LoadSettings();
        
        // Auto-save whenever settings change
        Settings.PropertyChanged += (s, e) => SaveSettings();
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var settings = JsonSerializer.Deserialize<NotificationSettings>(json);
                if (settings != null) Settings = settings;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load notification settings: {ex.Message}");
        }
    }

    private void SaveSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(Settings);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to save notification settings: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks the state change of a specific account to determine if a notification should be triggered.
    /// </summary>
    public void CheckAndNotify(string provider, string accountName, int oldUsage, int newUsage, bool isReset = false, long resetAt = 0)
    {
        string accountKey = $"{provider}_{accountName}";

        if (isReset && Settings.EnableResetNotifications)
        {
            // Prevent duplicates: check if we already notified for this reset time
            if (_lastNotifiedResetTimes.TryGetValue(accountKey, out long lastReset) && lastReset == resetAt)
                return;

            _lastNotifiedResetTimes[accountKey] = resetAt;
            TriggerNotification(
                $"{provider} Account Reset", 
                $"Account '{accountName}' has been reset and is ready to use!"
            );
        }
        else if (Settings.EnableLimitNotifications && newUsage >= Settings.LimitThresholdPercent && oldUsage < Settings.LimitThresholdPercent)
        {
            TriggerNotification(
                $"{provider} Limit Warning", 
                $"Account '{accountName}' usage reached {newUsage}%. Time to swap!"
            );
        }
    }

    private void TriggerNotification(string title, string message)
    {
        Log.Info($"[NOTIFICATION] {title}: {message}");
        OnNotificationTriggered?.Invoke(title, message);
    }

    /// <summary>
    /// Gathers available accounts and sends them in a digest form to Slack.
    /// Deduplicates by ignoring consecutive duplicate content.
    /// </summary>
    public async Task SendAccountDigestAsync(List<AccountDigestEntry> entries)
    {
        if (!Settings.EnableSlackNotifications || string.IsNullOrWhiteSpace(Settings.SlackWebhookUrl))
            return;

        var available = entries.Where(e => e.IsAvailable).ToList();
        if (available.Count == 0)
            return;

        // Hash-based duplicate prevention: skip if resetAt combination is identical
        var digestKey = string.Join("|", available.Select(e => $"{e.Provider}_{e.AccountName}_{e.ResetAt}"));
        var digestHash = digestKey.GetHashCode().ToString();
        if (digestHash == _lastDigestHash)
            return;

        _lastDigestHash = digestHash;

        // 메시지 구성
        var lines = new List<string> { "*[AIUsageMonitor] Available Accounts*" };
        foreach (var group in available.GroupBy(e => e.Provider))
        {
            lines.Add($"\n📌 *{group.Key}*");
            foreach (var entry in group)
            {
                lines.Add($"  • {entry.AccountName} — {entry.UsagePercent}% used (resets {entry.ResetTimeFormatted})");
            }
        }

        await SendSlackMessageAsync("Available Accounts Digest", string.Join("\n", lines));
    }

    /// <summary>
    /// Sends a test notification to the configured Slack Webhook URL to verify integration.
    /// </summary>
    public async Task SendTestAsync()
    {
        if (string.IsNullOrWhiteSpace(Settings.SlackWebhookUrl))
        {
            Settings.SlackStatus = "Error: Webhook URL is empty";
            Settings.SlackStatusColor = Color.FromArgb("#f59e0b"); // Amber 500
            return;
        }

        if (!Settings.SlackWebhookUrl.StartsWith("https://hooks.slack.com/"))
        {
            Settings.SlackStatus = "Error: Invalid Slack Webhook URL format";
            Settings.SlackStatusColor = Color.FromArgb("#f59e0b"); // Amber 500
            return;
        }

        Settings.SlackStatus = "Sending...";
        Settings.SlackStatusColor = Color.FromArgb("#60a5fa"); // Sky 400
        await SendSlackMessageAsync("AIUsageMonitor Test", "✅ Slack integration is working! You will receive alerts here.");
    }

    private async Task SendSlackMessageAsync(string title, string message)
    {
        try
        {
            var payload = new
            {
                text = $"*[{title}]*\n{message}"
            };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync(Settings.SlackWebhookUrl, content);
            if (response.IsSuccessStatusCode)
            {
                Settings.SlackStatus = $"Success ({DateTime.Now:HH:mm:ss})";
                Settings.SlackStatusColor = Color.FromArgb("#10b981"); // Emerald 500
            }
            else
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                Settings.SlackStatus = $"Error: {response.StatusCode} ({errorBody})";
                Settings.SlackStatusColor = Color.FromArgb("#ef4444"); // Rose 500
                Log.Error($"Slack Notification Failed: {response.StatusCode} - {errorBody}");
            }
        }
        catch (Exception ex)
        {
            Settings.SlackStatus = $"Exception: {ex.Message}";
            Settings.SlackStatusColor = Color.FromArgb("#ef4444"); // Rose 500
            Log.Error($"Slack Error: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════
    //  Scheduled Reset Alerts (Bot Token)
    // ═══════════════════════════════════════════

    private readonly HashSet<string> _scheduledResetKeys = new();

    /// <summary>
    /// Registers scheduled Slack reset alerts for accounts whose reset time is in the future.
    /// Skips if an alert has already been registered with the same signature key.
    /// </summary>
    public async Task ScheduleResetAlertsAsync(List<AccountDigestEntry> entries)
    {
        if (!Settings.EnableScheduledAlerts
            || string.IsNullOrWhiteSpace(Settings.SlackBotToken)
            || string.IsNullOrWhiteSpace(Settings.SlackChannelId))
            return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int scheduled = 0;

        foreach (var entry in entries)
        {
            // Only target accounts where the reset time is in the future and currently unavailable
            if (entry.ResetAt <= now || entry.IsAvailable)
                continue;

            string key = $"{entry.Provider}_{entry.AccountName}_{entry.ResetAt}";
            if (_scheduledResetKeys.Contains(key))
                continue;

            var message = $"🔄 *{entry.Provider}* account `{entry.AccountName}` is now reset and available!\n" +
                          $"Previous usage: {entry.UsagePercent}%";

            bool success = await ScheduleSlackMessageAsync(message, entry.ResetAt);
            if (success)
            {
                _scheduledResetKeys.Add(key);
                scheduled++;
            }
        }

        if (scheduled > 0)
        {
            Settings.ScheduledStatus = $"Scheduled {scheduled} alert(s) ({DateTime.Now:HH:mm:ss})";
            Settings.ScheduledStatusColor = Color.FromArgb("#10b981");
        }
    }

    /// <summary>
    /// Schedules a test message to be sent 1 minute later via the Slack Bot API to verify scheduled alerts.
    /// </summary>
    public async Task SendScheduledTestAsync()
    {
        if (string.IsNullOrWhiteSpace(Settings.SlackBotToken))
        {
            Settings.ScheduledStatus = "Error: Bot Token is empty";
            Settings.ScheduledStatusColor = Color.FromArgb("#f59e0b");
            return;
        }

        if (string.IsNullOrWhiteSpace(Settings.SlackChannelId))
        {
            Settings.ScheduledStatus = "Error: Channel ID is empty";
            Settings.ScheduledStatusColor = Color.FromArgb("#f59e0b");
            return;
        }

        // Schedule test for 1 minute later
        long postAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60;
        Settings.ScheduledStatus = "Scheduling test (1 min)...";
        Settings.ScheduledStatusColor = Color.FromArgb("#60a5fa");

        bool success = await ScheduleSlackMessageAsync(
            "✅ *AIUsageMonitor* scheduled alert test!\nThis message was scheduled 1 minute ago.",
            postAt
        );

        if (!success)
        {
            // Status already updated by ScheduleSlackMessageAsync on failure
        }
        else
        {
            Settings.ScheduledStatus = $"Test scheduled for 1 min later ({DateTime.Now:HH:mm:ss})";
            Settings.ScheduledStatusColor = Color.FromArgb("#10b981");
        }
    }

    private async Task<bool> ScheduleSlackMessageAsync(string message, long postAt)
    {
        try
        {
            var payload = new
            {
                channel = Settings.SlackChannelId,
                text = message,
                post_at = postAt
            };
            var json = JsonSerializer.Serialize(payload);
            var request = new HttpRequestMessage(HttpMethod.Post, "https://slack.com/api/chat.scheduleMessage")
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Settings.SlackBotToken);

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(responseBody);
            bool ok = doc.RootElement.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();

            if (ok)
            {
                Log.Info($"[SCHEDULED] Message scheduled for {DateTimeOffset.FromUnixTimeSeconds(postAt).ToLocalTime():MM/dd HH:mm}");
                return true;
            }
            else
            {
                string error = doc.RootElement.TryGetProperty("error", out var errProp) ? errProp.GetString() ?? "unknown" : "unknown";
                Settings.ScheduledStatus = $"Error: {error}";
                Settings.ScheduledStatusColor = Color.FromArgb("#ef4444");
                Log.Error($"Slack Schedule Failed: {error}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Settings.ScheduledStatus = $"Exception: {ex.Message}";
            Settings.ScheduledStatusColor = Color.FromArgb("#ef4444");
            Log.Error($"Slack Schedule Error: {ex.Message}");
            return false;
        }
    }
}

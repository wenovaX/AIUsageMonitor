using System.Collections.ObjectModel;
using System.Diagnostics;
using AIUsageMonitor.Models;

namespace AIUsageMonitor.Services;

public class GoogleRadarService
{
    private readonly GoogleApiService _googleApi;
    private readonly OAuthService _oauthService = new();

    public GoogleRadarService()
    {
        _googleApi = new GoogleApiService();
    }

    public string RedirectUri => _oauthService.RedirectUri;

    public string GetAuthUrl() => _googleApi.GetAuthUrl(RedirectUri);

    public async Task<string> ReceiveCodeAsync(string authUrl, CancellationToken cancellationToken = default) => await _oauthService.ReceiveCodeAsync(authUrl, cancellationToken);

    public async Task<TokenResponse> ExchangeCodeAsync(string code) => await _googleApi.ExchangeCodeAsync(code, RedirectUri);

    public async Task<UserInfo> GetUserInfoAsync(string accessToken) => await _googleApi.GetUserInfoAsync(accessToken);

    private bool _isFirstSuccessfulQuotaLoad = true;

    public async Task UpdateAccountDataAsync(CloudAccount account)
    {
        if (string.IsNullOrEmpty(account.refresh_token)) return;

        var stopwatch = Stopwatch.StartNew();

        var tokens = await _googleApi.RefreshTokenAsync(account.refresh_token);
        account.access_token = tokens.access_token;

        var credits = await _googleApi.FetchCreditsAsync(account.access_token);
        var quota = await _googleApi.FetchQuotaAsync(account.access_token);

        if (quota == null || quota.models.Count == 0)
        {
            throw new Exception("Failed to fetch quota or received empty data.");
        }

        // Global validation check: Ensure the response isn't completely zeroed out unexpectedly (Stale Cache Bug)
        // System models (chat_, tab_) often bypass quotas, so we must check if the actual AI models are valid.
        var aiModels = quota.models.Values.Where(m => !m.display_name.StartsWith("chat_", StringComparison.OrdinalIgnoreCase) && 
                                                      !m.display_name.StartsWith("tab_", StringComparison.OrdinalIgnoreCase)).ToList();
        
        bool hasAnyValidAiData = aiModels.Any(m => m.percentage > 0 || (!string.IsNullOrWhiteSpace(m.resetTime) && m.resetTime != "0h 0m"));
        
        // If there are AI models, but ALL of them are 0% and have stale "0h 0m" reset times, it's a backend glitch.
        if (aiModels.Count > 0 && !hasAnyValidAiData && account.quotas.Count > 0)
        {
            var dumpErr = string.Join(", ", quota.models.Values.Select(m => $"{m.display_name}:{m.percentage}%|{m.resetTime}"));
            Log.Error($"InvalidData: {account.email} -> {dumpErr}");
            
            // --- RAW JSON DUMP FOR DEBUGGING GOOGLE API ---
            Log.Info($"RawJsonDump: {account.email} -> {quota.RawJsonDump}");
            
            throw new Exception("Received suspiciously invalid or zeroed quota data (ignored).");
        }

        var discoveredModels = quota.models.Values
            .Select(model => model.display_name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (_isFirstSuccessfulQuotaLoad && discoveredModels.Count > 0)
        {
            _isFirstSuccessfulQuotaLoad = false;
            var modelNames = string.Join(", ", discoveredModels);

            Log.Info($"FirstSuccess: {account.email}: {modelNames}");
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            account.credits = credits;
            account.UpdateQuotas(quota.models.Values);
            account.last_updated = DateTime.Now;
        });

        stopwatch.Stop();
        var dumpSuccess = string.Join(", ", quota.models.Values.Select(m => $"{m.display_name}({m.percentage}%|{m.resetTime})"));
        Log.Info($"RefreshCompleted: {account.email}: models={quota.models.Count}, elapsedMs={stopwatch.ElapsedMilliseconds}");
        Log.Info($"QuotaDump: {account.email} -> {dumpSuccess}");
    }
}

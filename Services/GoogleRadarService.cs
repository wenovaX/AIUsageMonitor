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

        var discoveredModels = quota.models.Values
            .Select(model => model.display_name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (_isFirstSuccessfulQuotaLoad && discoveredModels.Count > 0)
        {
            _isFirstSuccessfulQuotaLoad = false;
            var modelNames = string.Join(", ", discoveredModels);

            Debug.WriteLine($"[Antigravity][FirstSuccess] {account.email}: {modelNames}");
        }

        account.credits = credits;
        account.quotas = new ObservableCollection<ModelQuotaInfo>(quota.models.Values);
        account.last_updated = DateTime.Now;

        stopwatch.Stop();
        Debug.WriteLine($"[Antigravity][RefreshCompleted] {account.email}: models={quota.models.Count}, elapsedMs={stopwatch.ElapsedMilliseconds}");
    }
}

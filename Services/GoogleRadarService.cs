using System.Collections.ObjectModel;
using AIUsageMonitor.Models;

namespace AIUsageMonitor.Services;

public class GoogleRadarService
{
    private readonly GoogleApiService _googleApi;
    private readonly OAuthService _oauthService = new();

    public GoogleRadarService(ModelFilterService filterService)
    {
        _googleApi = new GoogleApiService(filterService);
    }

    public string RedirectUri => _oauthService.RedirectUri;

    public string GetAuthUrl() => _googleApi.GetAuthUrl(RedirectUri);

    public async Task<string> ReceiveCodeAsync(string authUrl, CancellationToken cancellationToken = default) => await _oauthService.ReceiveCodeAsync(authUrl, cancellationToken);

    public async Task<TokenResponse> ExchangeCodeAsync(string code) => await _googleApi.ExchangeCodeAsync(code, RedirectUri);

    public async Task<UserInfo> GetUserInfoAsync(string accessToken) => await _googleApi.GetUserInfoAsync(accessToken);

    public async Task UpdateAccountDataAsync(CloudAccount account)
    {
        if (string.IsNullOrEmpty(account.refresh_token)) return;

        var tokens = await _googleApi.RefreshTokenAsync(account.refresh_token);
        account.access_token = tokens.access_token;

        var credits = await _googleApi.FetchCreditsAsync(account.access_token);
        var quota = await _googleApi.FetchQuotaAsync(account.access_token);

        account.credits = credits;
        account.quotas = new ObservableCollection<ModelQuotaInfo>(quota.models.Values);
        account.last_updated = DateTime.Now;
    }
}

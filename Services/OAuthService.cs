namespace AIUsageMonitor.Services;

public class OAuthService
{
    private const int Port = 9988;
    private readonly string _redirectUri = $"http://localhost:{Port}/oauth-callback";
    private string _state = string.Empty;

    public string RedirectUri => _redirectUri;

    public string CreateState() => _state = LocalOAuthCallbackListener.CreateState();

    public Task<string> ReceiveCodeAsync(string authUrl, CancellationToken cancellationToken = default) =>
        LocalOAuthCallbackListener.ReceiveCodeAsync(
            _redirectUri,
            authUrl,
            _state,
            "Google",
            cancellationToken);
}

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIUsageMonitor.Services;

public class OpenAIAuthService
{
	private const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";
	private const string AuthEndpoint = "https://auth.openai.com/oauth/authorize";
	private const string TokenEndpoint = "https://auth.openai.com/oauth/token";
	private const string Scopes = "openid profile email";
	private const int Port = 1455;

	private readonly string _redirectUri = $"http://localhost:{Port}/auth/callback";
	private readonly HttpClient _httpClient = new();

	private string _codeVerifier = "";
	private string _state = "";

	// PKCE Generation
	private void GeneratePKCE()
	{
		var bytes = new byte[32];
		using var rng = RandomNumberGenerator.Create();
		rng.GetBytes(bytes);
		_codeVerifier = Base64UrlEncode(bytes);
		_state = Guid.NewGuid().ToString("N");
	}

	private string GetCodeChallenge()
	{
		using var sha256 = SHA256.Create();
		var hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(_codeVerifier));
		return Base64UrlEncode(hash);
	}

	// Auth URL
	public string GetAuthUrl()
	{
		GeneratePKCE();
		var codeChallenge = GetCodeChallenge();

		var queryParams = new Dictionary<string, string>
		{
			["client_id"] = ClientId,
			["redirect_uri"] = _redirectUri,
			["response_type"] = "code",
			["scope"] = Scopes,
			["code_challenge"] = codeChallenge,
			["code_challenge_method"] = "S256",
			["state"] = _state
		};

		var query = string.Join("&", queryParams.Select(
			kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

		return $"{AuthEndpoint}?{query}";
	}

	// Localhost Callback Listener
	public async Task<string> ReceiveCodeAsync(string authUrl, CancellationToken cancellationToken = default)
	{
		return await LocalOAuthCallbackListener.ReceiveCodeAsync(
			_redirectUri,
			authUrl,
			_state,
			"OpenAI",
			cancellationToken);
	}

	// Token Exchange
	public async Task<OpenAITokenResponse> ExchangeCodeForTokensAsync(string code)
	{
		var body = new FormUrlEncodedContent(new[]
		{
			new KeyValuePair<string, string>("grant_type", "authorization_code"),
			new KeyValuePair<string, string>("code", code),
			new KeyValuePair<string, string>("redirect_uri", _redirectUri),
			new KeyValuePair<string, string>("client_id", ClientId),
			new KeyValuePair<string, string>("code_verifier", _codeVerifier),
		});

		var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint);
		request.Content = body;
		request.Headers.Add("Accept", "application/json");

		var response = await _httpClient.SendAsync(request);
		var json = await response.Content.ReadAsStringAsync();

		Log.Info($"Token response status: {response.StatusCode}");

		if (!response.IsSuccessStatusCode)
			throw new Exception($"Token exchange failed ({response.StatusCode}): {json}");

		return JsonSerializer.Deserialize<OpenAITokenResponse>(json)
			?? throw new Exception("Failed to parse token response.");
	}

	// Token Refresh
	public async Task<OpenAITokenResponse> RefreshTokenAsync(string refreshToken)
	{
		var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint);
		request.Headers.Add("Accept", "application/json");
		request.Content = new StringContent(
			JsonSerializer.Serialize(new
			{
				client_id = ClientId,
				grant_type = "refresh_token",
				refresh_token = refreshToken,
				scope = Scopes,
			}),
			Encoding.UTF8,
			"application/json");

		var response = await _httpClient.SendAsync(request);
		var json = await response.Content.ReadAsStringAsync();

		if (!response.IsSuccessStatusCode)
			throw new Exception($"Token refresh failed ({response.StatusCode}): {json}");

		return JsonSerializer.Deserialize<OpenAITokenResponse>(json)
			?? throw new Exception("Failed to parse refresh response.");
	}

	// Helpers
	private static string Base64UrlEncode(byte[] data)
	{
		return Convert.ToBase64String(data)
			.Replace('+', '-')
			.Replace('/', '_')
			.TrimEnd('=');
	}
}

public class OpenAITokenResponse
{
	public string access_token { get; set; } = "";
	public string refresh_token { get; set; } = "";
	public string id_token { get; set; } = "";
	public string token_type { get; set; } = "";
	public int expires_in { get; set; }
	public string scope { get; set; } = "";
}

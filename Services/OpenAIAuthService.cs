using System.Diagnostics;
using System.Net;
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
		using var listener = new HttpListener();
		try
		{
			Debug.WriteLine($"[OpenAI OAuth] Starting listener on {_redirectUri}/");
			listener.Prefixes.Add(_redirectUri + "/");
			listener.Start();
		}
		catch (HttpListenerException ex)
		{
			Debug.WriteLine($"[OpenAI OAuth] Listener Error: {ex.Message}");
			if (ex.ErrorCode == 5)
				throw new Exception("Access Denied: Please run as Admin or add URL ACL for the port.");
			else if (ex.ErrorCode == 183)
				throw new Exception("Port 1455 already in use. Please close other programs using it.");
			throw;
		}

		try
		{
			Debug.WriteLine($"[OpenAI OAuth] Opening browser: {authUrl}");
			await Browser.Default.OpenAsync(authUrl, BrowserLaunchMode.External);

			Debug.WriteLine("[OpenAI OAuth] Waiting for callback...");
			var contextTask = listener.GetContextAsync();
			var cancelTask = Task.Delay(Timeout.Infinite, cancellationToken);
			var completedTask = await Task.WhenAny(contextTask, cancelTask);

			if (completedTask == cancelTask)
			{
				listener.Stop();
				throw new OperationCanceledException("Login was cancelled by user.");
			}

			var context = await contextTask;
			var request = context.Request;

			Debug.WriteLine($"[OpenAI OAuth] Request received: {request.Url}");
			var code = request.QueryString["code"];
			var error = request.QueryString["error"];
			var returnedState = request.QueryString["state"];

			if (!string.IsNullOrEmpty(returnedState) && returnedState != _state)
			{
				throw new Exception("OAuth state mismatch. Possible CSRF attack.");
			}

			using (var response = context.Response)
			{
				string responseString = "<html><body style='font-family: sans-serif; text-align: center; padding-top: 50px; background: #0f172a; color: #f8fafc;'>" +
					"<h1>AIUsageMonitor</h1>" +
					(string.IsNullOrEmpty(error)
						? "<p style='color: #10b981;'>OpenAI login successful! You can close this window now.</p>"
						: $"<p style='color: #ef4444;'>Error: {error}</p>") +
					"</body></html>";

				byte[] buffer = Encoding.UTF8.GetBytes(responseString);
				response.ContentLength64 = buffer.Length;
				await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
			}

			listener.Stop();

			if (!string.IsNullOrEmpty(error))
				throw new Exception($"OpenAI OAuth error: {error}");

			return code ?? throw new Exception("No authorization code received.");
		}
		catch (OperationCanceledException)
		{
			Debug.WriteLine("[OpenAI OAuth] Login cancelled by user.");
			if (listener.IsListening) listener.Stop();
			throw;
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[OpenAI OAuth] Runtime Error: {ex.Message}");
			if (listener.IsListening) listener.Stop();
			throw;
		}
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

		Debug.WriteLine($"[OpenAI OAuth] Token response status: {response.StatusCode}");

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

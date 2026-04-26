using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIUsageMonitor.Services;

public class CodexOAuthService
{
    private const string ClientId = "Iv1.b507a08c87ecfe98"; // VS Code Copilot Client ID
    private const string Scopes = "read:user";
    
    private readonly HttpClient _httpClient = new();

    public class DeviceCodeResponse
    {
        [JsonPropertyName("device_code")]
        public string DeviceCode { get; set; } = "";
        
        [JsonPropertyName("user_code")]
        public string UserCode { get; set; } = "";
        
        [JsonPropertyName("verification_uri")]
        public string VerificationUri { get; set; } = "";
        
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
        
        [JsonPropertyName("interval")]
        public int Interval { get; set; }
    }

    public class AccessTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = "";
        
        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = "";
        
        [JsonPropertyName("scope")]
        public string Scope { get; set; } = "";
    }

    public async Task<DeviceCodeResponse?> RequestDeviceCodeAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/device/code");
        request.Headers.Add("Accept", "application/json");
        
        var body = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", ClientId),
            new KeyValuePair<string, string>("scope", Scopes)
        });
        request.Content = body;

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<DeviceCodeResponse>(json);
    }

    public async Task<string> PollForTokenAsync(string deviceCode, int intervalSeconds, CancellationToken cancellationToken)
    {
        var url = "https://github.com/login/oauth/access_token";
        
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(intervalSeconds * 1000, cancellationToken);
            
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Accept", "application/json");
            
            var body = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", ClientId),
                new KeyValuePair<string, string>("device_code", deviceCode),
                new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:device_code")
            });
            request.Content = body;

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var errorElement))
            {
                var error = errorElement.GetString();
                if (error == "authorization_pending")
                {
                    continue; // Keep polling
                }
                if (error == "slow_down")
                {
                    intervalSeconds += 5; // Add 5s penalty
                    continue;
                }
                if (error == "expired_token")
                {
                    throw new Exception("The device code has expired.");
                }
                
                throw new Exception($"Authentication failed: {error}");
            }
            
            if (doc.RootElement.TryGetProperty("access_token", out var tokenElement))
            {
                return tokenElement.GetString() ?? "";
            }
        }
        
        throw new OperationCanceledException("Token polling was cancelled.");
    }
}

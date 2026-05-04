using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIUsageMonitor.Services;

public class CodexApiService
{
	private const string UsageEndpoint = "https://chatgpt.com/backend-api/wham/usage";
	private const string UserInfoEndpoint = "https://chatgpt.com/backend-api/me";
	private readonly HttpClient _httpClient = new();

	public class CodexUsageResponse
	{
		[JsonPropertyName("plan_type")]
		public string PlanType { get; set; } = "";

		[JsonPropertyName("rate_limit")]
		public RateLimitDetails? RateLimit { get; set; }

		[JsonPropertyName("credits")]
		public CreditDetails? Credits { get; set; }

		[JsonPropertyName("promo")]
		public PromoDetails? Promo { get; set; }
	}

	public class PromoDetails
	{
		[JsonPropertyName("message")]
		public string Message { get; set; } = "";
	}

	public class RateLimitDetails
	{
		[JsonPropertyName("primary_window")]
		public WindowSnapshot? PrimaryWindow { get; set; }

		[JsonPropertyName("secondary_window")]
		public WindowSnapshot? SecondaryWindow { get; set; }
	}

	public class WindowSnapshot
	{
		[JsonPropertyName("used_percent")]
		public int UsedPercent { get; set; }

		[JsonPropertyName("reset_at")]
		public long ResetAt { get; set; }

		[JsonPropertyName("limit_window_seconds")]
		public int LimitWindowSeconds { get; set; }
	}

	public class CreditDetails
	{
		[JsonPropertyName("has_credits")]
		public bool HasCredits { get; set; }

		[JsonPropertyName("unlimited")]
		public bool Unlimited { get; set; }

		[JsonPropertyName("balance")]
		public JsonElement? Balance { get; set; }

		public double? GetBalance()
		{
			if (Balance == null) return null;
			if (Balance.Value.ValueKind == JsonValueKind.Number)
				return Balance.Value.GetDouble();
			if (Balance.Value.ValueKind == JsonValueKind.String &&
				double.TryParse(Balance.Value.GetString(), out var val))
				return val;
			return null;
		}
	}

	public class UserInfoResponse
	{
		[JsonPropertyName("email")]
		public string Email { get; set; } = "";

		[JsonPropertyName("name")]
		public string Name { get; set; } = "";

		[JsonPropertyName("picture")]
		public string Picture { get; set; } = "";
	}

	public async Task<CodexUsageResponse?> FetchUsageAsync(string accessToken, string? accountId = null)
	{
		var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
		request.Headers.Add("Authorization", $"Bearer {accessToken}");
		request.Headers.Add("User-Agent", "AIUsageMonitor");
		request.Headers.Add("Accept", "application/json");

		if (!string.IsNullOrEmpty(accountId))
			request.Headers.Add("ChatGPT-Account-Id", accountId);

		try
		{
			Log.Info("Fetching usage data from OpenAI...");
			var response = await _httpClient.SendAsync(request);
			var json = await response.Content.ReadAsStringAsync();

			Log.Info($"Usage response received. Status: {response.StatusCode} (Length: {json.Length})");

			if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
				response.StatusCode == System.Net.HttpStatusCode.Forbidden)
				throw new Exception("Token expired or unauthorized. Please re-login.");

			if (!response.IsSuccessStatusCode)
				throw new Exception($"Usage API error ({response.StatusCode}): {json}");

            // JSON이 너무 길면 잘라서라도 출력
            string logJson = json.Length > 1000 ? json.Substring(0, 1000) + "...[TRUNCATED]" : json;
			Log.Info($"[RAW JSON] {logJson}");
            
			return JsonSerializer.Deserialize<CodexUsageResponse>(json);
		}
		catch (Exception ex)
		{
			Log.Error("Network request failed in FetchUsageAsync", ex);
			throw;
		}
	}

	public async Task<UserInfoResponse?> FetchUserInfoAsync(string accessToken)
	{
		var request = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
		request.Headers.Add("Authorization", $"Bearer {accessToken}");
		request.Headers.Add("User-Agent", "AIUsageMonitor");
		request.Headers.Add("Accept", "application/json");

		var response = await _httpClient.SendAsync(request);
		var json = await response.Content.ReadAsStringAsync();

		Log.Info($"UserInfo status: {response.StatusCode}");

		if (!response.IsSuccessStatusCode)
			return null; // Non-critical: we just won't show user info

		return JsonSerializer.Deserialize<UserInfoResponse>(json);
	}

	public static string FormatResetTime(long unixTimestamp)
	{
		var resetTime = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).LocalDateTime;
		var remaining = resetTime - DateTime.Now;

		if (remaining.TotalMinutes < 1) return "now";
		if (remaining.TotalHours < 1) return $"{(int)remaining.TotalMinutes}m";
		if (remaining.TotalHours < 24) return $"{(int)remaining.TotalHours}h {remaining.Minutes}m";
		return $"{(int)remaining.TotalDays}d {remaining.Hours}h";
	}

	public static string FormatWindowName(int windowSeconds)
	{
		var hours = windowSeconds / 3600;
		if (hours >= 168) return "Weekly";
		if (hours >= 24) return $"{hours / 24}d";
		return $"{hours}h";
	}

	public static string FormatResetDate(long unixTimestamp)
	{
		if (unixTimestamp == 0) return "—";
		var resetTime = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).LocalDateTime;
		return resetTime.ToString("MM/dd HH:mm");
	}

	// Parse OpenAI JWT id_token to extract user info (name, email)
	public static UserInfoResponse? ParseIdToken(string idToken)
	{
		if (string.IsNullOrEmpty(idToken)) return null;

		try
		{
			var parts = idToken.Split('.');
			if (parts.Length < 2) return null;

			// Base64Url decode the payload (2nd segment)
			var payload = parts[1];
			payload = payload.Replace('-', '+').Replace('_', '/');
			switch (payload.Length % 4)
			{
				case 2: payload += "=="; break;
				case 3: payload += "="; break;
			}

			var bytes = Convert.FromBase64String(payload);
			var json = System.Text.Encoding.UTF8.GetString(bytes);

			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;

			var result = new UserInfoResponse();

			if (root.TryGetProperty("name", out var nameEl))
				result.Name = nameEl.GetString() ?? "";
			if (root.TryGetProperty("email", out var emailEl))
				result.Email = emailEl.GetString() ?? "";
			if (root.TryGetProperty("picture", out var picEl))
				result.Picture = picEl.GetString() ?? "";

			return result;
		}
		catch (Exception ex)
		{
			Log.Error("Failed to parse id_token", ex);
			return null;
		}
	}

}

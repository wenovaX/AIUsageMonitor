using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AIUsageMonitor.Models;
using System.Diagnostics;

namespace AIUsageMonitor.Services;

public class CursorApiService
{
    private readonly HttpClient _httpClient;

    public CursorApiService()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        _httpClient = new HttpClient(handler);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 AIUsageMonitor");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<(string? Email, int Used, int Limit, DateTime? ResetDate)> FetchMonthlyUsageAsync(string sessionToken)
    {
        var normalizedToken = NormalizeSessionToken(sessionToken);
        if (string.IsNullOrWhiteSpace(normalizedToken))
            throw new Exception("Cursor session token is empty.");
        Trace.WriteLine($"[CursorAPI] FetchMonthlyUsageAsync start. tokenLen={normalizedToken.Length}");

        // Cursor local DB token format: userId%3A%3A<jwt>
        if (normalizedToken.Contains("%3A%3A", StringComparison.OrdinalIgnoreCase))
        {
            var workosUsage = await TryFetchUsageWithWorkosAsync("https://cursor.com", normalizedToken)
                ?? await TryFetchUsageWithWorkosAsync("https://cursor.sh", normalizedToken);
            if (workosUsage is null)
                throw new Exception("Failed to fetch Cursor usage with Workos session token.");

            var emailFromToken = TryExtractEmailFromWorkosToken(normalizedToken);
            var emailFromApi = await TryFetchEmailWithWorkosAsync("https://cursor.com", normalizedToken)
                ?? await TryFetchEmailWithWorkosAsync("https://cursor.sh", normalizedToken);

            return (emailFromApi ?? emailFromToken, workosUsage.Value.Used, workosUsage.Value.Limit, workosUsage.Value.ResetDate);
        }

        var cookieNames = new[]
        {
            "__Secure-next-auth.session-token",
            "__Secure-authjs.session-token",
            "next-auth.session-token",
            "authjs.session-token"
        };

        (int Used, int Limit)? usage = null;
        foreach (var cookieName in cookieNames)
        {
            usage = await TryFetchUsageAsync("https://cursor.sh", normalizedToken, cookieName);
            if (usage is null)
                usage = await TryFetchUsageAsync("https://cursor.com", normalizedToken, cookieName);
            if (usage is not null)
                break;
        }

        if (usage is null)
            throw new Exception("Failed to fetch Cursor usage. Check session token.");

        string? email = null;
        foreach (var cookieName in cookieNames)
        {
            email = await TryFetchEmailAsync("https://cursor.sh", normalizedToken, cookieName)
                ?? await TryFetchEmailAsync("https://cursor.com", normalizedToken, cookieName);
            if (!string.IsNullOrWhiteSpace(email))
                break;
        }

        return (email, usage.Value.Used, usage.Value.Limit, null);
    }

    private async Task<(int Used, int Limit, DateTime? ResetDate)?> TryFetchUsageWithWorkosAsync(string baseUrl, string workosSessionToken)
    {
        var userId = ExtractWorkosUserId(workosSessionToken);
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/usage?user={Uri.EscapeDataString(userId)}");
        request.Headers.Add("Cookie", $"WorkosCursorSessionToken={workosSessionToken}");

        var response = await _httpClient.SendAsync(request);
        Trace.WriteLine($"[CursorAPI] GET {baseUrl}/api/usage user={userId} cookie=WorkosCursorSessionToken status={(int)response.StatusCode}");
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        Trace.WriteLine($"[CursorAPI] /api/usage raw: {Truncate(json, 4000)}");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        var totalUsed = 0;
        var candidateLimits = new List<int>();
        DateTime? resetDate = null;

        if (root.TryGetProperty("startOfMonth", out var startEl) &&
            startEl.ValueKind == JsonValueKind.String &&
            DateTime.TryParse(startEl.GetString(), out var startOfMonth))
        {
            resetDate = startOfMonth.AddMonths(1);
        }

        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object)
                continue;

            var modelUsed = prop.Value.TryGetProperty("numRequests", out var usedEl) && usedEl.ValueKind == JsonValueKind.Number
                ? usedEl.GetInt32()
                : 0;
            var modelLimit = prop.Value.TryGetProperty("maxRequestUsage", out var limitEl) && limitEl.ValueKind == JsonValueKind.Number
                ? limitEl.GetInt32()
                : 0;

            totalUsed += Math.Max(0, modelUsed);
            if (modelLimit > 0)
                candidateLimits.Add(modelLimit);

            Trace.WriteLine($"[CursorAPI] usage model={prop.Name}, used={modelUsed}, limit={modelLimit}");
        }

        var resolvedLimit = candidateLimits.Count == 0 ? 500 : candidateLimits.Max();
        Trace.WriteLine($"[CursorAPI] usage aggregate: used={totalUsed}, limit={resolvedLimit}, limitCandidates={string.Join(",", candidateLimits)}");

        return (totalUsed, resolvedLimit, resetDate);
    }

    private async Task<string?> TryFetchEmailWithWorkosAsync(string baseUrl, string workosSessionToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/user/me");
        request.Headers.Add("Cookie", $"WorkosCursorSessionToken={workosSessionToken}");

        var response = await _httpClient.SendAsync(request);
        Trace.WriteLine($"[CursorAPI] GET {baseUrl}/api/user/me cookie=WorkosCursorSessionToken status={(int)response.StatusCode}");
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("email", out var emailEl) ? emailEl.GetString() : null;
    }

    public async Task<(string? Email, int Used, int Limit, DateTime? ResetDate, string? SessionToken)> FetchMonthlyUsageWithCredentialsAsync(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            throw new Exception("Cursor login email/password is empty.");

        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            CookieContainer = cookieContainer,
            UseCookies = true
        };

        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 AIUsageMonitor");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var baseUrl = "https://cursor.com";
        var csrf = await GetCsrfTokenAsync(client, baseUrl);
        await LoginWithCredentialsAsync(client, baseUrl, csrf, email, password);

        var usage = await TryFetchUsageWithCookieAsync(client, baseUrl)
            ?? await TryFetchUsageWithCookieAsync(client, "https://cursor.sh");
        if (usage is null)
            throw new Exception("Failed to fetch Cursor usage after login.");

        var resolvedEmail = await TryFetchEmailWithCookieAsync(client, baseUrl)
            ?? await TryFetchEmailWithCookieAsync(client, "https://cursor.sh")
            ?? email;

        var sessionToken = ExtractSessionToken(cookieContainer);
        return (resolvedEmail, usage.Value.Used, usage.Value.Limit, null, sessionToken);
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client, string baseUrl)
    {
        var response = await client.GetAsync($"{baseUrl}/api/csrf");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("csrfToken", out var tokenEl))
            throw new Exception("Cursor csrf token not found.");
        var token = tokenEl.GetString();
        if (string.IsNullOrWhiteSpace(token))
            throw new Exception("Cursor csrf token is empty.");
        return token;
    }

    private static async Task LoginWithCredentialsAsync(HttpClient client, string baseUrl, string csrfToken, string email, string password)
    {
        var payload = new Dictionary<string, string>
        {
            ["email"] = email,
            ["password"] = password,
            ["csrfToken"] = csrfToken,
            ["callbackUrl"] = $"{baseUrl}/settings",
            ["json"] = "true"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/auth/callback/credentials")
        {
            Content = new FormUrlEncodedContent(payload)
        };
        request.Headers.Referrer = new Uri($"{baseUrl}/api/auth/login");

        var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Exception($"Cursor login failed ({(int)response.StatusCode}): {body}");
        }
    }

    private async Task<(int Used, int Limit)?> TryFetchUsageAsync(string baseUrl, string sessionToken, string cookieName)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/user/usage");
        request.Headers.Add("Cookie", $"{cookieName}={sessionToken}");

        var response = await _httpClient.SendAsync(request);
        Trace.WriteLine($"[CursorAPI] GET {baseUrl}/api/user/usage cookie={cookieName} status={(int)response.StatusCode}");
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("premium_models", out var premium))
            return null;

        var used = premium.TryGetProperty("used", out var usedEl) && usedEl.ValueKind == JsonValueKind.Number
            ? usedEl.GetInt32()
            : 0;
        var limit = premium.TryGetProperty("limit", out var limitEl) && limitEl.ValueKind == JsonValueKind.Number
            ? limitEl.GetInt32()
            : 500;

        return (used, limit);
    }

    private async Task<string?> TryFetchEmailAsync(string baseUrl, string sessionToken, string cookieName)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/user/me");
        request.Headers.Add("Cookie", $"{cookieName}={sessionToken}");

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("email", out var emailEl) ? emailEl.GetString() : null;
    }

    private static string NormalizeSessionToken(string input)
    {
        var value = input.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        const string key = "__Secure-next-auth.session-token=";
        if (value.StartsWith(key, StringComparison.OrdinalIgnoreCase))
        {
            value = value[key.Length..];
        }

        const string key2 = "__Secure-authjs.session-token=";
        if (value.StartsWith(key2, StringComparison.OrdinalIgnoreCase))
        {
            value = value[key2.Length..];
        }

        var semicolonIndex = value.IndexOf(';');
        if (semicolonIndex >= 0)
        {
            value = value[..semicolonIndex];
        }

        return value.Trim();
    }

    private static string? ExtractWorkosUserId(string workosSessionToken)
    {
        var sep = workosSessionToken.IndexOf("%3A%3A", StringComparison.OrdinalIgnoreCase);
        if (sep <= 0)
            return null;
        return workosSessionToken[..sep];
    }

    private static string? TryExtractEmailFromWorkosToken(string workosSessionToken)
    {
        var sep = workosSessionToken.IndexOf("%3A%3A", StringComparison.OrdinalIgnoreCase);
        if (sep < 0 || sep + 6 >= workosSessionToken.Length)
            return null;

        var jwt = workosSessionToken[(sep + 6)..];
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2)
                return null;

            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            var bytes = Convert.FromBase64String(payload);
            using var doc = JsonDocument.Parse(bytes);
            return doc.RootElement.TryGetProperty("email", out var emailEl) ? emailEl.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;
        return value[..maxLength] + "...(truncated)";
    }

    private static string? ExtractSessionToken(CookieContainer cookieContainer)
    {
        var names = new[]
        {
            "__Secure-next-auth.session-token",
            "__Secure-authjs.session-token",
            "next-auth.session-token",
            "authjs.session-token"
        };

        var uris = new[]
        {
            new Uri("https://cursor.com"),
            new Uri("https://www.cursor.com"),
            new Uri("https://cursor.sh"),
            new Uri("https://www.cursor.sh")
        };

        foreach (var uri in uris)
        {
            var cookies = cookieContainer.GetCookies(uri).Cast<Cookie>();
            foreach (var name in names)
            {
                var found = cookies.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (found is not null && !string.IsNullOrWhiteSpace(found.Value))
                    return found.Value;
            }
        }

        return null;
    }

    private static async Task<(int Used, int Limit)?> TryFetchUsageWithCookieAsync(HttpClient client, string baseUrl)
    {
        var response = await client.GetAsync($"{baseUrl}/api/user/usage");
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("premium_models", out var premium))
            return null;

        var used = premium.TryGetProperty("used", out var usedEl) && usedEl.ValueKind == JsonValueKind.Number
            ? usedEl.GetInt32()
            : 0;
        var limit = premium.TryGetProperty("limit", out var limitEl) && limitEl.ValueKind == JsonValueKind.Number
            ? limitEl.GetInt32()
            : 500;

        return (used, limit);
    }

    private static async Task<string?> TryFetchEmailWithCookieAsync(HttpClient client, string baseUrl)
    {
        var response = await client.GetAsync($"{baseUrl}/api/user/me");
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("email", out var emailEl) ? emailEl.GetString() : null;
    }
}

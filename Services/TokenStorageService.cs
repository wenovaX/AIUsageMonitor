using Microsoft.Maui.Storage;
using System.Diagnostics;

namespace AIUsageMonitor.Services;

public class TokenStorageService
{
    private const string AccessTokenSuffix = "_access_token";
    private const string RefreshTokenSuffix = "_refresh_token";

    public async Task SaveTokensAsync(string accountId, string? accessToken, string? refreshToken)
    {
        // Backward‑compatible overload – just stores tokens without expiration info
        await SaveTokensAsync(accountId, accessToken, refreshToken, null);
    }

    // New overload that also stores the token expiration timestamp (UTC)
    public async Task SaveTokensAsync(string accountId, string? accessToken, string? refreshToken, int? expiresInSeconds)
    {
        try
        {
            if (!string.IsNullOrEmpty(accessToken))
            {
                await SecureStorage.Default.SetAsync($"{accountId}{AccessTokenSuffix}", accessToken);
            }
            else
            {
                SecureStorage.Default.Remove($"{accountId}{AccessTokenSuffix}");
            }

            if (!string.IsNullOrEmpty(refreshToken))
            {
                await SecureStorage.Default.SetAsync($"{accountId}{RefreshTokenSuffix}", refreshToken);
            }
            else
            {
                SecureStorage.Default.Remove($"{accountId}{RefreshTokenSuffix}");
            }

            // Store expirations as an ISO‑8601 UTC string (if we have a value)
            if (expiresInSeconds.HasValue)
            {
                var expiresAt = DateTime.UtcNow.AddSeconds(expiresInSeconds.Value);
                await SecureStorage.Default.SetAsync($"{accountId}_expires_at", expiresAt.ToString("o"));
            }
            else
            {
                SecureStorage.Default.Remove($"{accountId}_expires_at");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SecureStorage] Failed to save tokens for {accountId}: {ex.Message}");
        }
    }

    // Returns (access, refresh, expiresAt) – expiresAt may be null if not stored
    public async Task<(string? AccessToken, string? RefreshToken, DateTime? ExpiresAt)> LoadTokensAsync(string accountId)
    {
        try
        {
            var accessToken = await SecureStorage.Default.GetAsync($"{accountId}{AccessTokenSuffix}");
            var refreshToken = await SecureStorage.Default.GetAsync($"{accountId}{RefreshTokenSuffix}");
            var expiresStr = await SecureStorage.Default.GetAsync($"{accountId}_expires_at");
            DateTime? expiresAt = null;
            if (!string.IsNullOrEmpty(expiresStr) && DateTime.TryParse(expiresStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                expiresAt = dt;
            return (accessToken, refreshToken, expiresAt);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SecureStorage] Failed to load tokens for {accountId}: {ex.Message}");
            return (null, null, null);
        }
    }

    public void RemoveTokens(string accountId)
    {
        try
        {
            SecureStorage.Default.Remove($"{accountId}{AccessTokenSuffix}");
            SecureStorage.Default.Remove($"{accountId}{RefreshTokenSuffix}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SecureStorage] Failed to remove tokens for {accountId}: {ex.Message}");
        }
    }
}

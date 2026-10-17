using Microsoft.Maui.Storage;
using System.Text.Json;

namespace AIUsageMonitor.Services;

public class TokenStorageService
{
    private const string AccessTokenSuffix = "_access_token";
    private const string RefreshTokenSuffix = "_refresh_token";
    private const string FileName = "tokens.json";
    private const string ProtectionPurpose = "AIUsageMonitor.Tokens.v1";
    private readonly string _filePath = AppDataPaths.GetDataFilePath(FileName);
    private readonly SemaphoreSlim _fileSemaphore = new(1, 1);

    /// <summary>
    /// Saves the access and refresh tokens for a specific account without expiration information.
    /// </summary>
    public async Task SaveTokensAsync(string accountId, string? accessToken, string? refreshToken)
    {
        // Backward-compatible overload: stores tokens without expiration info.
        await SaveTokensAsync(accountId, accessToken, refreshToken, null);
    }

    // New overload that also stores the token expiration timestamp (UTC)
    /// <summary>
    /// Saves the access and refresh tokens along with an optional token expiration duration in seconds.
    /// </summary>
    public async Task SaveTokensAsync(string accountId, string? accessToken, string? refreshToken, int? expiresInSeconds)
    {
        await _fileSemaphore.WaitAsync();
        try
        {
            var tokens = await LoadStoreAsync();
            tokens[accountId] = new StoredTokenSet
            {
                AccessToken = LocalDataProtector.Protect(accessToken, ProtectionPurpose),
                RefreshToken = LocalDataProtector.Protect(refreshToken, ProtectionPurpose),
                ExpiresAt = expiresInSeconds.HasValue
                    ? DateTime.UtcNow.AddSeconds(expiresInSeconds.Value)
                    : null
            };

            await SaveStoreAsync(tokens);
            RemoveLegacySecureStorageTokens(accountId);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save tokens", ex);
        }
        finally
        {
            _fileSemaphore.Release();
        }
    }

    // Returns (access, refresh, expiresAt) – expiresAt may be null if not stored
    /// <summary>
    /// Loads the access and refresh tokens, as well as the expiration time, for a specific account.
    /// </summary>
    public async Task<(string? AccessToken, string? RefreshToken, DateTime? ExpiresAt)> LoadTokensAsync(string accountId)
    {
        await _fileSemaphore.WaitAsync();
        try
        {
            var tokens = await LoadStoreAsync();
            if (tokens.TryGetValue(accountId, out var stored))
            {
                return (
                    LocalDataProtector.Unprotect(stored.AccessToken, ProtectionPurpose),
                    LocalDataProtector.Unprotect(stored.RefreshToken, ProtectionPurpose),
                    stored.ExpiresAt);
            }

            var legacy = await LoadLegacySecureStorageTokensAsync(accountId);
            if (!string.IsNullOrEmpty(legacy.AccessToken) || !string.IsNullOrEmpty(legacy.RefreshToken) || legacy.ExpiresAt.HasValue)
            {
                tokens[accountId] = new StoredTokenSet
                {
                    AccessToken = LocalDataProtector.Protect(legacy.AccessToken, ProtectionPurpose),
                    RefreshToken = LocalDataProtector.Protect(legacy.RefreshToken, ProtectionPurpose),
                    ExpiresAt = legacy.ExpiresAt
                };
                await SaveStoreAsync(tokens);
                RemoveLegacySecureStorageTokens(accountId);
            }

            return legacy;
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load tokens", ex);
            return (null, null, null);
        }
        finally
        {
            _fileSemaphore.Release();
        }
    }

    /// <summary>
    /// Removes all stored tokens for a specific account.
    /// </summary>
    public async Task RemoveTokensAsync(string accountId)
    {
        await _fileSemaphore.WaitAsync();
        try
        {
            var tokens = await LoadStoreAsync();
            if (tokens.Remove(accountId))
            {
                await SaveStoreAsync(tokens);
            }

            RemoveLegacySecureStorageTokens(accountId);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to remove tokens", ex);
        }
        finally
        {
            _fileSemaphore.Release();
        }
    }

    private async Task<Dictionary<string, StoredTokenSet>> LoadStoreAsync()
    {
        if (!File.Exists(_filePath))
            return new Dictionary<string, StoredTokenSet>();

        return await JsonFileUtility.ReadAsync<Dictionary<string, StoredTokenSet>>(_filePath)
            ?? new Dictionary<string, StoredTokenSet>();
    }

    private async Task SaveStoreAsync(Dictionary<string, StoredTokenSet> tokens)
    {
        await JsonFileUtility.WriteAsync(
            _filePath,
            tokens,
            new JsonSerializerOptions { WriteIndented = true });
    }

    private async Task<(string? AccessToken, string? RefreshToken, DateTime? ExpiresAt)> LoadLegacySecureStorageTokensAsync(string accountId)
    {
        var accessToken = await SecureStorage.Default.GetAsync($"{accountId}{AccessTokenSuffix}");
        var refreshToken = await SecureStorage.Default.GetAsync($"{accountId}{RefreshTokenSuffix}");
        var expiresStr = await SecureStorage.Default.GetAsync($"{accountId}_expires_at");
        DateTime? expiresAt = null;

        if (!string.IsNullOrEmpty(expiresStr) &&
            DateTime.TryParse(expiresStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
        {
            expiresAt = dt;
        }

        return (accessToken, refreshToken, expiresAt);
    }

    private static void RemoveLegacySecureStorageTokens(string accountId)
    {
        SecureStorage.Default.Remove($"{accountId}{AccessTokenSuffix}");
        SecureStorage.Default.Remove($"{accountId}{RefreshTokenSuffix}");
        SecureStorage.Default.Remove($"{accountId}_expires_at");
    }

    private sealed class StoredTokenSet
    {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public DateTime? ExpiresAt { get; set; }
    }
}

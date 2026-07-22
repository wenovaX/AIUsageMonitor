using Microsoft.Maui.Storage;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIUsageMonitor.Services;

public class TokenStorageService
{
    private const string AccessTokenSuffix = "_access_token";
    private const string RefreshTokenSuffix = "_refresh_token";
    private const string FileName = "tokens.json";
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
                AccessToken = Protect(accessToken),
                RefreshToken = Protect(refreshToken),
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
                return (Unprotect(stored.AccessToken), Unprotect(stored.RefreshToken), stored.ExpiresAt);
            }

            var legacy = await LoadLegacySecureStorageTokensAsync(accountId);
            if (!string.IsNullOrEmpty(legacy.AccessToken) || !string.IsNullOrEmpty(legacy.RefreshToken) || legacy.ExpiresAt.HasValue)
            {
                tokens[accountId] = new StoredTokenSet
                {
                    AccessToken = Protect(legacy.AccessToken),
                    RefreshToken = Protect(legacy.RefreshToken),
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

        var json = await File.ReadAllTextAsync(_filePath);
        return JsonSerializer.Deserialize<Dictionary<string, StoredTokenSet>>(json)
            ?? new Dictionary<string, StoredTokenSet>();
    }

    private async Task SaveStoreAsync(Dictionary<string, StoredTokenSet> tokens)
    {
        var json = JsonSerializer.Serialize(tokens, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_filePath, json);
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

    private static string Protect(string? plain)
    {
        if (string.IsNullOrWhiteSpace(plain))
            return string.Empty;

        using var aes = Aes.Create();
        aes.Key = GetStableKey();
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        var plainBytes = Encoding.UTF8.GetBytes(plain);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        return $"enc1:{Convert.ToBase64String(aes.IV)}:{Convert.ToBase64String(cipherBytes)}";
    }

    private static string Unprotect(string? cipher)
    {
        if (string.IsNullOrWhiteSpace(cipher))
            return string.Empty;

        if (!cipher.StartsWith("enc1:", StringComparison.Ordinal))
            return cipher;

        var parts = cipher.Split(':');
        if (parts.Length != 3)
            return cipher;

        using var aes = Aes.Create();
        aes.Key = GetStableKey();
        aes.IV = Convert.FromBase64String(parts[1]);
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        var encrypted = Convert.FromBase64String(parts[2]);
        var plainBytes = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private static byte[] GetStableKey()
    {
        var source = $"{Environment.MachineName}|{Environment.UserName}|AIUsageMonitor.Tokens.v1";
        return SHA256.HashData(Encoding.UTF8.GetBytes(source));
    }

    private sealed class StoredTokenSet
    {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public DateTime? ExpiresAt { get; set; }
    }
}

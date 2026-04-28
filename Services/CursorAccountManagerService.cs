using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using AIUsageMonitor.Models;
using System.Security.Cryptography;

namespace AIUsageMonitor.Services;

public class CursorAccountManagerService
{
    private const string FileName = "cursor_accounts.json";
    private readonly string _filePath;
    private readonly SemaphoreSlim _saveSemaphore = new(1, 1);

    public ObservableCollection<CursorAccount> Accounts { get; } = new();
    public string StorageDirectory => FileSystem.AppDataDirectory;

    public CursorAccountManagerService()
    {
        _filePath = Path.Combine(FileSystem.AppDataDirectory, FileName);
    }

    public async Task LoadAccountsAsync()
    {
        if (!File.Exists(_filePath))
            return;

        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            var list = JsonSerializer.Deserialize<List<PersistedCursorAccount>>(json);
            if (list is null)
                return;

            Accounts.Clear();
            foreach (var account in list)
            {
                Accounts.Add(new CursorAccount
                {
                    id = account.id,
                    name = account.name,
                    email = account.email,
                    session_token = Unprotect(account.session_token),
                    login_email = account.login_email,
                    login_password = Unprotect(account.login_password),
                    monthly_used = account.monthly_used,
                    monthly_limit = account.monthly_limit,
                    context_usage_percent = account.context_usage_percent,
                    context_composer_name = account.context_composer_name,
                    context_reset_date = account.context_reset_date,
                    context_bottom_hit = account.context_bottom_hit,
                    last_updated = account.last_updated
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load cursor accounts: {ex.Message}");
        }
    }

    public async Task SaveAccountsAsync()
    {
        await _saveSemaphore.WaitAsync();
        try
        {
            var persisted = Accounts.Select(a => new PersistedCursorAccount
            {
                id = a.id,
                name = a.name,
                email = a.email,
                session_token = Protect(a.session_token),
                login_email = a.login_email,
                login_password = Protect(a.login_password),
                monthly_used = a.monthly_used,
                monthly_limit = a.monthly_limit,
                context_usage_percent = a.context_usage_percent,
                context_composer_name = a.context_composer_name,
                context_reset_date = a.context_reset_date,
                context_bottom_hit = a.context_bottom_hit,
                last_updated = a.last_updated
            }).ToList();

            var json = JsonSerializer.Serialize(persisted);
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save cursor accounts: {ex.Message}");
        }
        finally
        {
            _saveSemaphore.Release();
        }
    }

    public void AddOrUpdateAccount(CursorAccount account)
    {
        var existing = Accounts.FirstOrDefault(a =>
            a.id == account.id ||
            (!string.IsNullOrWhiteSpace(a.session_token) && a.session_token == account.session_token) ||
            (!string.IsNullOrWhiteSpace(a.login_email) &&
             a.login_email.Equals(account.login_email, StringComparison.OrdinalIgnoreCase)));

        if (existing is not null)
        {
            existing.name = account.name;
            existing.email = account.email;
            existing.session_token = account.session_token;
            existing.login_email = account.login_email;
            existing.login_password = account.login_password;
            existing.monthly_used = account.monthly_used;
            existing.monthly_limit = account.monthly_limit;
            existing.context_usage_percent = account.context_usage_percent;
            existing.context_composer_name = account.context_composer_name;
            existing.context_reset_date = account.context_reset_date;
            existing.context_bottom_hit = account.context_bottom_hit;
            existing.last_updated = DateTime.Now;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(account.id))
                account.id = Guid.NewGuid().ToString();
            account.last_updated = DateTime.Now;
            Accounts.Add(account);
        }

        _ = SaveAccountsAsync();
    }

    public void RemoveAccount(string accountId)
    {
        var account = Accounts.FirstOrDefault(a => a.id == accountId);
        if (account is null)
            return;

        Accounts.Remove(account);
        _ = SaveAccountsAsync();
    }

    private static string Protect(string? plain)
    {
        if (string.IsNullOrWhiteSpace(plain))
            return string.Empty;

        try
        {
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
        catch
        {
            return plain;
        }
    }

    private static string Unprotect(string? cipher)
    {
        if (string.IsNullOrWhiteSpace(cipher))
            return string.Empty;

        try
        {
            if (!cipher.StartsWith("enc1:", StringComparison.Ordinal))
                return cipher;

            var parts = cipher.Split(':');
            if (parts.Length != 3)
                return cipher;

            var iv = Convert.FromBase64String(parts[1]);
            var encrypted = Convert.FromBase64String(parts[2]);

            using var aes = Aes.Create();
            aes.Key = GetStableKey();
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            var plainBytes = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return cipher;
        }
    }

    private static byte[] GetStableKey()
    {
        var source = $"{Environment.MachineName}|{Environment.UserName}|AIUsageMonitor.Cursor.v1";
        return SHA256.HashData(Encoding.UTF8.GetBytes(source));
    }

    private sealed class PersistedCursorAccount
    {
        public string id { get; set; } = Guid.NewGuid().ToString();
        public string name { get; set; } = "Cursor Account";
        public string email { get; set; } = "";
        public string session_token { get; set; } = "";
        public string login_email { get; set; } = "";
        public string login_password { get; set; } = "";
        public int monthly_used { get; set; }
        public int monthly_limit { get; set; } = 500;
        public double context_usage_percent { get; set; }
        public string context_composer_name { get; set; } = "";
        public DateTime? context_reset_date { get; set; }
        public bool context_bottom_hit { get; set; }
        public DateTime? last_updated { get; set; }
    }
}

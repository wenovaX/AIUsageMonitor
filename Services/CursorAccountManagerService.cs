using System.Collections.ObjectModel;
using AIUsageMonitor.Models;

namespace AIUsageMonitor.Services;

public class CursorAccountManagerService
{
    private const string FileName = "cursor_accounts.json";
    private const string ProtectionPurpose = "AIUsageMonitor.Cursor.v1";
    private readonly string _filePath;
    private readonly SemaphoreSlim _saveSemaphore = new(1, 1);

    public ObservableCollection<CursorAccount> Accounts { get; } = new();
    public string StorageDirectory => AppDataPaths.DataDirectory;

    public CursorAccountManagerService()
    {
        _filePath = AppDataPaths.GetDataFilePath(FileName, FileSystem.AppDataDirectory);
    }

    /// <summary>
    /// Loads stored Cursor accounts from file and decrypts sensitive token/password fields.
    /// </summary>
    public async Task LoadAccountsAsync()
    {
        if (!File.Exists(_filePath))
            return;

        try
        {
            var list = await JsonFileUtility.ReadAsync<List<PersistedCursorAccount>>(_filePath);
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

    /// <summary>
    /// Saves all current Cursor accounts to local storage, encrypting sensitive fields before serialization.
    /// </summary>
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

            await JsonFileUtility.WriteAsync(_filePath, persisted);
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

    /// <summary>
    /// Adds a new Cursor account or updates an existing one based on stable account identity.
    /// </summary>
    public void AddOrUpdateAccount(CursorAccount account)
    {
        var existing = Accounts.FirstOrDefault(a => IsSameAccount(a, account));

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
            existing.HasError = account.HasError;
            existing.LastErrorMessage = account.LastErrorMessage;
            RemoveDuplicateAccounts(existing);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(account.id))
                account.id = Guid.NewGuid().ToString();
            account.last_updated = DateTime.Now;
            Accounts.Add(account);
        }

        SortAccounts();
        _ = SaveAccountsAsync();
    }

    private static bool IsSameAccount(CursorAccount existing, CursorAccount incoming)
    {
        return
            (!string.IsNullOrWhiteSpace(existing.id) &&
             existing.id.Equals(incoming.id, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(existing.session_token) &&
             existing.session_token == incoming.session_token) ||
            (!string.IsNullOrWhiteSpace(existing.email) &&
             existing.email.Equals(incoming.email, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(existing.login_email) &&
             existing.login_email.Equals(incoming.login_email, StringComparison.OrdinalIgnoreCase));
    }

    private void RemoveDuplicateAccounts(CursorAccount canonical)
    {
        var duplicates = Accounts
            .Where(account => !ReferenceEquals(account, canonical) && IsSameAccount(account, canonical))
            .ToList();

        foreach (var duplicate in duplicates)
        {
            Accounts.Remove(duplicate);
        }
    }

    /// <summary>
    /// Sorts Cursor accounts by their monthly usage and composer context percentages.
    /// </summary>
    public void SortAccounts()
    {
        if (Accounts.Count <= 1)
            return;

        var sorted = Accounts.OrderByDescending(a => a.MonthlyUsedPercent)
                            .ThenByDescending(a => a.context_usage_percent)
                            .ToList();

        for (int i = 0; i < sorted.Count; i++)
        {
            var oldIndex = Accounts.IndexOf(sorted[i]);
            if (oldIndex != i)
            {
                Accounts.Move(oldIndex, i);
            }
        }
    }

    /// <summary>
    /// Removes a specific Cursor account from local storage.
    /// </summary>
    public async Task RemoveAccountAsync(string accountId)
    {
        var account = Accounts.FirstOrDefault(a => a.id == accountId);
        if (account is null)
            return;

        Accounts.Remove(account);
        await SaveAccountsAsync();
    }

    private static string Protect(string? plain)
    {
        try
        {
            return LocalDataProtector.Protect(plain, ProtectionPurpose);
        }
        catch
        {
            return plain ?? string.Empty;
        }
    }

    private static string Unprotect(string? cipher)
    {
        if (string.IsNullOrWhiteSpace(cipher))
            return string.Empty;

        try
        {
            return LocalDataProtector.Unprotect(cipher, ProtectionPurpose);
        }
        catch
        {
            return cipher ?? string.Empty;
        }
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

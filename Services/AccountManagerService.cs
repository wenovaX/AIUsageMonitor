using System.Collections.ObjectModel;
using System.Text.Json;
using AIUsageMonitor.Models;
using Microsoft.Extensions.DependencyInjection;

namespace AIUsageMonitor.Services;

public class AccountManagerService
{
    private const string FileName = "accounts.json";
    private readonly string _filePath;
    private readonly TokenStorageService _tokenStorage;
    private readonly SemaphoreSlim _saveSemaphore = new SemaphoreSlim(1, 1);
    public ObservableCollection<CloudAccount> Accounts { get; } = new();

    public AccountManagerService()
    {
        _filePath = AppDataPaths.GetDataFilePath(FileName, FileSystem.AppDataDirectory);
        _tokenStorage = MauiProgram.Services.GetRequiredService<TokenStorageService>();
    }

    /// <summary>
    /// Loads stored Google/Gemini accounts from file and migrates tokens to local token storage if necessary.
    /// </summary>
    public async Task LoadAccountsAsync()
    {
        if (!File.Exists(_filePath)) return;

        try
        {
            var json = await JsonFileUtility.ReadTextAsync(_filePath);
            var list = JsonFileUtility.Deserialize<List<CloudAccount>>(json);
            if (list != null)
            {
                using var doc = JsonDocument.Parse(json);
                Accounts.Clear();
                // Deduplicate by email just in case the file is corrupted
                var uniqueList = list.GroupBy(a => a.email.ToLower()).Select(g => g.First());
                foreach (var acc in uniqueList)
                {
                    var legacyTokens = LegacyAccountTokenReader.FindForAccount(doc.RootElement, acc.id);
                    var oldAccess = legacyTokens.AccessToken;
                    var oldRefresh = legacyTokens.RefreshToken;

                    // Migration logic: If tokens exist in JSON, move them to local token storage.
                    var (storedAccess, storedRefresh, _) = await _tokenStorage.LoadTokensAsync(acc.id);
                    
                    System.Diagnostics.Debug.WriteLine($"[Migration:Google] Acc: {acc.email} | JSON(acc:{!string.IsNullOrEmpty(oldAccess)}, ref:{!string.IsNullOrEmpty(oldRefresh)}) | Local(acc:{!string.IsNullOrEmpty(storedAccess)}, ref:{!string.IsNullOrEmpty(storedRefresh)})");

                    if (string.IsNullOrEmpty(storedAccess) && (!string.IsNullOrEmpty(oldAccess) || !string.IsNullOrEmpty(oldRefresh)))
                    {
                        System.Diagnostics.Debug.WriteLine($"[Migration:Google] -> MIGRATING old tokens to local token storage for {acc.email}");
                        await _tokenStorage.SaveTokensAsync(acc.id, oldAccess ?? "", oldRefresh ?? "");
                        acc.access_token = oldAccess ?? "";
                        acc.refresh_token = oldRefresh;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[Migration:Google] -> KEEPING local tokens for {acc.email}");
                        acc.access_token = storedAccess ?? "";
                        acc.refresh_token = storedRefresh;
                    }

                    Accounts.Add(acc);
                }

                await SaveAccountsAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load accounts: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves all current Google/Gemini accounts to local storage and updates secure token storage.
    /// </summary>
    public async Task SaveAccountsAsync()
    {
        await _saveSemaphore.WaitAsync();
        try
        {
            // Ensure all tokens are in local token storage before saving JSON.
            foreach (var acc in Accounts)
            {
                await _tokenStorage.SaveTokensAsync(acc.id, acc.access_token, acc.refresh_token);
            }

            await JsonFileUtility.WriteAsync(_filePath, Accounts.ToList());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save accounts: {ex.Message}");
        }
        finally
        {
            _saveSemaphore.Release();
        }
    }

    /// <summary>
    /// Adds a new Google/Gemini account or updates an existing one based on email matching.
    /// </summary>
    public void AddOrUpdateAccount(CloudAccount account)
    {
        if (string.IsNullOrEmpty(account.email)) return;
        var existing = Accounts.FirstOrDefault(a => a.email.ToLower() == account.email.ToLower());
        if (existing != null)
        {
            existing.access_token = account.access_token;
            existing.refresh_token = account.refresh_token;
            existing.name = account.name;
            existing.avatar_url = account.avatar_url;
            existing.credits = account.credits;
            existing.quotas = account.quotas;
            existing.last_updated = DateTime.Now;
            existing.HasError = account.HasError;
            existing.LastErrorMessage = account.LastErrorMessage;
        }
        else
        {
            if (string.IsNullOrEmpty(account.id)) account.id = Guid.NewGuid().ToString();
            account.last_updated = DateTime.Now;
            Accounts.Add(account);
        }
        SortAccounts();
        _ = SaveAccountsAsync();
    }

    /// <summary>
    /// Sorts Google/Gemini accounts by their non-hidden model usage percentages in descending order.
    /// </summary>
    public void SortAccounts()
    {
        if (Accounts.Count <= 1)
            return;

        // Sort by total percentage of visible quotas (descending)
        var sorted = Accounts.OrderByDescending(GetTotalUsagePercentage).ToList();

        for (int i = 0; i < sorted.Count; i++)
        {
            var oldIndex = Accounts.IndexOf(sorted[i]);
            if (oldIndex != i)
            {
                Accounts.Move(oldIndex, i);
            }
        }
    }

    private double GetTotalUsagePercentage(CloudAccount acc)
    {
        if (acc.quotas == null || acc.quotas.Count == 0) return 0;

        var hidden = acc.HiddenModels?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();
        
        // Only sum percentages for models that are NOT hidden
        return acc.quotas
            .Where(q => !hidden.Contains(q.display_name))
            .Sum(q => q.percentage);
    }

    /// <summary>
    /// Removes a specific Google/Gemini account from local storage and deletes its secure tokens.
    /// </summary>
    public async Task RemoveAccountAsync(string accountId)
    {
        var acc = Accounts.FirstOrDefault(a => a.id == accountId);
        if (acc != null)
        {
            Accounts.Remove(acc);
            await _tokenStorage.RemoveTokensAsync(accountId);
            await SaveAccountsAsync();
        }
    }

    /// <summary>
    /// Imports Google/Gemini accounts from a JSON file at the specified path.
    /// </summary>
    public async Task ImportAccountsAsync(string sourcePath)
    {
        try
        {
            var json = await JsonFileUtility.ReadTextAsync(sourcePath);
            var list = JsonFileUtility.Deserialize<List<CloudAccount>>(json);
            if (list != null)
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var acc in list)
                {
                    var legacyTokens = LegacyAccountTokenReader.FindForAccount(doc.RootElement, acc.id);
                    if (!string.IsNullOrEmpty(legacyTokens.AccessToken))
                        acc.access_token = legacyTokens.AccessToken;
                    if (!string.IsNullOrEmpty(legacyTokens.RefreshToken))
                        acc.refresh_token = legacyTokens.RefreshToken;
                    AddOrUpdateAccount(acc);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to import accounts: {ex.Message}");
            throw;
        }
    }
}

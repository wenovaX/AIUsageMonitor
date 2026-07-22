using System.Collections.ObjectModel;
using System.Text.Json;
using AIUsageMonitor.Models;
using Microsoft.Extensions.DependencyInjection;

namespace AIUsageMonitor.Services;

public class CodexAccountManagerService
{
    private const string FileName = "codex_accounts.json";
    private readonly string _filePath;
    private readonly TokenStorageService _tokenStorage;
    private readonly SemaphoreSlim _saveSemaphore = new SemaphoreSlim(1, 1);
    public ObservableCollection<CodexAccount> Accounts { get; } = new();

    public CodexAccountManagerService()
    {
        _filePath = AppDataPaths.GetDataFilePath(FileName, FileSystem.AppDataDirectory);
        _tokenStorage = MauiProgram.Services.GetRequiredService<TokenStorageService>();
    }

    /// <summary>
    /// Loads stored OpenAI/Codex accounts from file and migrates tokens to local token storage if necessary.
    /// </summary>
    public async Task LoadAccountsAsync()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            var list = JsonSerializer.Deserialize<List<CodexAccount>>(json);
            if (list != null)
            {
                using var doc = JsonDocument.Parse(json);
                Accounts.Clear();
                foreach (var acc in list)
                {
                    string? oldAccess = null;
                    string? oldRefresh = null;
                    
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var element in doc.RootElement.EnumerateArray())
                        {
                            if (element.TryGetProperty("id", out var idProp) && idProp.GetString() == acc.id)
                            {
                                oldAccess = element.TryGetProperty("access_token", out var aProp) ? aProp.GetString() : null;
                                oldRefresh = element.TryGetProperty("refresh_token", out var rProp) ? rProp.GetString() : null;
                                break;
                            }
                        }
                    }

                    // Migration logic
                    var (storedAccess, storedRefresh, _) = await _tokenStorage.LoadTokensAsync(acc.id);
                    
                    Log.Info($"Migration: Acc: {acc.email ?? acc.name} | JSON(acc:{!string.IsNullOrEmpty(oldAccess)}, ref:{!string.IsNullOrEmpty(oldRefresh)}) | Local(acc:{!string.IsNullOrEmpty(storedAccess)}, ref:{!string.IsNullOrEmpty(storedRefresh)})");

                    if (string.IsNullOrEmpty(storedAccess) && (!string.IsNullOrEmpty(oldAccess) || !string.IsNullOrEmpty(oldRefresh)))
                    {
                        Log.Info($"Migration: -> MIGRATING old tokens to local token storage for {acc.email ?? acc.name}");
                        await _tokenStorage.SaveTokensAsync(acc.id, oldAccess ?? "", oldRefresh ?? "");
                        acc.access_token = oldAccess ?? "";
                        acc.refresh_token = oldRefresh ?? "";
                    }
                    else
                    {
                        Log.Info($"Migration: -> KEEPING local tokens for {acc.email ?? acc.name}");
                        acc.access_token = storedAccess ?? "";
                        acc.refresh_token = storedRefresh ?? "";
                    }
                    Accounts.Add(acc);
                }

                await SaveAccountsAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load codex accounts", ex);
        }
    }

    /// <summary>
    /// Saves all current OpenAI/Codex accounts to local storage and updates secure token storage.
    /// </summary>
    public async Task SaveAccountsAsync()
    {
        await _saveSemaphore.WaitAsync();
        try
        {
            // Ensure all tokens are in local token storage before saving JSON.
            foreach (var acc in Accounts)
            {
                var (_, _, expiresAt) = await _tokenStorage.LoadTokensAsync(acc.id);
                int? expiresInSeconds = null;
                if (expiresAt.HasValue)
                {
                    var remaining = expiresAt.Value - DateTime.UtcNow;
                    expiresInSeconds = remaining > TimeSpan.Zero
                        ? (int)Math.Ceiling(remaining.TotalSeconds)
                        : 0;
                }

                await _tokenStorage.SaveTokensAsync(acc.id, acc.access_token, acc.refresh_token, expiresInSeconds);
            }

            var json = JsonSerializer.Serialize(Accounts.ToList());
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save codex accounts", ex);
        }
        finally
        {
            _saveSemaphore.Release();
        }
    }

    /// <summary>
    /// Adds a new OpenAI/Codex account or updates an existing one based on stable account identity.
    /// </summary>
    public void AddOrUpdateAccount(CodexAccount account)
    {
        var existing = FindMatchingAccount(account);
        if (existing != null)
        {
            existing.access_token = account.access_token;
            if (!string.IsNullOrWhiteSpace(account.refresh_token))
                existing.refresh_token = account.refresh_token;
            existing.name = account.name;
            existing.email = account.email;
            existing.account_id = account.account_id;
            existing.plan_type = account.plan_type;
            existing.credits = account.credits;
            existing.has_credits = account.has_credits;
            existing.unlimited_credits = account.unlimited_credits;
            existing.primaryUsedPercent = account.primaryUsedPercent;
            existing.primaryWindowLabel = account.primaryWindowLabel;
            existing.primaryResetDescription = account.primaryResetDescription;
            existing.secondaryUsedPercent = account.secondaryUsedPercent;
            existing.secondaryWindowLabel = 
                string.IsNullOrWhiteSpace(account.secondaryWindowLabel) ? 
                string.Empty : $"{account.secondaryWindowLabel} ";
            if (!string.IsNullOrEmpty(account.login_method))
                existing.login_method = account.login_method;
            existing.HasError = account.HasError;
            existing.LastErrorMessage = account.LastErrorMessage;
            existing.IsTrialExpired = account.IsTrialExpired;
            existing.PrimaryResetAt = account.PrimaryResetAt;
            existing.PromoMessage = account.PromoMessage;
            existing.last_updated = DateTime.Now;
            RemoveDuplicateAccounts(existing);
        }
        else
        {
            if (string.IsNullOrEmpty(account.id)) account.id = Guid.NewGuid().ToString();
            account.last_updated = DateTime.Now;
            Accounts.Add(account);
        }
        _ = SaveAccountsAsync();
        SortAccounts();
    }

    public CodexAccount? FindMatchingAccount(CodexAccount account) =>
        Accounts.FirstOrDefault(existing => IsSameAccount(existing, account));

    private static bool IsSameAccount(CodexAccount existing, CodexAccount incoming)
    {
        return
            (!string.IsNullOrWhiteSpace(existing.id) &&
             existing.id.Equals(incoming.id, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(existing.account_id) &&
             existing.account_id.Equals(incoming.account_id, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(existing.email) &&
             existing.email.Equals(incoming.email, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(existing.access_token) &&
             existing.access_token == incoming.access_token);
    }

    private void RemoveDuplicateAccounts(CodexAccount canonical)
    {
        var duplicates = Accounts
            .Where(account => !ReferenceEquals(account, canonical) && IsSameAccount(account, canonical))
            .ToList();

        foreach (var duplicate in duplicates)
        {
            Accounts.Remove(duplicate);
            _ = _tokenStorage.RemoveTokensAsync(duplicate.id);
        }
    }

    /// <summary>
    /// Sorts OpenAI/Codex accounts by plan priority, usage percentage, and reset time.
    /// </summary>
    public void SortAccounts()
    {
        if (Accounts.Count <= 1) return;

        // 1. Get a sorted list
        var sorted = Accounts.OrderBy(a => GetTierPriority(a.plan_type))
                            .ThenByDescending(a => a.primaryUsedPercent)
                            .ThenBy(a => GetResetTimestamp(a))
                            .ToList();

        // 2. Synchronize ObservableCollection without clearing (to maintain scroll position if possible)
        for (int i = 0; i < sorted.Count; i++)
        {
            var oldIndex = Accounts.IndexOf(sorted[i]);
            if (oldIndex != i)
            {
                Accounts.Move(oldIndex, i);
            }
        }

        _ = SaveAccountsAsync();
    }

    private int GetTierPriority(string? planType)
    {
        if (string.IsNullOrEmpty(planType)) return 99;
        return planType.ToLower() switch
        {
            "plus" => 1,
            "pro" => 1,
            "team" => 1,
            "free" => 2,
            _ => 3
        };
    }

    private long GetResetTimestamp(CodexAccount acc)
    {
        // Use the stored timestamp. If it's 0 (no limit or not loaded), use a very far future date.
        if (acc.PrimaryResetAt <= 0) return DateTimeOffset.MaxValue.ToUnixTimeSeconds();
        return acc.PrimaryResetAt;
    }

    /// <summary>
    /// Removes a specific OpenAI/Codex account from local storage and deletes its secure tokens.
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
    /// Exports all OpenAI/Codex accounts to a JSON file at the specified path.
    /// </summary>
    public async Task ExportAccountsAsync(string targetPath)
    {
        try
        {
            var json = JsonSerializer.Serialize(Accounts.ToList());
            await File.WriteAllTextAsync(targetPath, json);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to export codex accounts", ex);
            throw;
        }
    }

    /// <summary>
    /// Imports OpenAI/Codex accounts from a JSON file at the specified path.
    /// </summary>
    public async Task ImportAccountsAsync(string sourcePath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(sourcePath);
            var list = JsonSerializer.Deserialize<List<CodexAccount>>(json);
            if (list != null)
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var acc in list)
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var element in doc.RootElement.EnumerateArray())
                        {
                            if (element.TryGetProperty("id", out var idProp) && idProp.GetString() == acc.id)
                            {
                                var oldAccess = element.TryGetProperty("access_token", out var aProp) ? aProp.GetString() : null;
                                var oldRefresh = element.TryGetProperty("refresh_token", out var rProp) ? rProp.GetString() : null;
                                
                                if (!string.IsNullOrEmpty(oldAccess)) acc.access_token = oldAccess;
                                if (!string.IsNullOrEmpty(oldRefresh)) acc.refresh_token = oldRefresh;
                                break;
                            }
                        }
                    }
                    AddOrUpdateAccount(acc);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to import codex accounts", ex);
            throw;
        }
    }
}

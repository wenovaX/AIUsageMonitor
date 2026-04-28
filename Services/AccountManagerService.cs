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
        _filePath = Path.Combine(FileSystem.AppDataDirectory, FileName);
        _tokenStorage = MauiProgram.Services.GetRequiredService<TokenStorageService>();
    }

    public async Task LoadAccountsAsync()
    {
        if (!File.Exists(_filePath)) return;

        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            var list = JsonSerializer.Deserialize<List<CloudAccount>>(json);
            if (list != null)
            {
                using var doc = JsonDocument.Parse(json);
                Accounts.Clear();
                // Deduplicate by email just in case the file is corrupted
                var uniqueList = list.GroupBy(a => a.email.ToLower()).Select(g => g.First());
                foreach (var acc in uniqueList)
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

                    // Migration logic: If tokens exist in JSON, move them to SecureStorage
                    var (secureAccess, secureRefresh, _) = await _tokenStorage.LoadTokensAsync(acc.id);
                    
                    System.Diagnostics.Debug.WriteLine($"[Migration:Google] Acc: {acc.email} | JSON(acc:{!string.IsNullOrEmpty(oldAccess)}, ref:{!string.IsNullOrEmpty(oldRefresh)}) | Secure(acc:{!string.IsNullOrEmpty(secureAccess)}, ref:{!string.IsNullOrEmpty(secureRefresh)})");

                    if (string.IsNullOrEmpty(secureAccess) && (!string.IsNullOrEmpty(oldAccess) || !string.IsNullOrEmpty(oldRefresh)))
                    {
                        System.Diagnostics.Debug.WriteLine($"[Migration:Google] -> MIGRATING old tokens to SecureStorage for {acc.email}");
                        await _tokenStorage.SaveTokensAsync(acc.id, oldAccess ?? "", oldRefresh ?? "");
                        acc.access_token = oldAccess ?? "";
                        acc.refresh_token = oldRefresh;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[Migration:Google] -> KEEPING SecureStorage tokens for {acc.email}");
                        acc.access_token = secureAccess ?? "";
                        acc.refresh_token = secureRefresh;
                    }

                    Accounts.Add(acc);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load accounts: {ex.Message}");
        }
    }

    public async Task SaveAccountsAsync()
    {
        await _saveSemaphore.WaitAsync();
        try
        {
            // Ensure all tokens are in SecureStorage before saving JSON
            foreach (var acc in Accounts)
            {
                await _tokenStorage.SaveTokensAsync(acc.id, acc.access_token, acc.refresh_token);
            }

            var json = JsonSerializer.Serialize(Accounts.ToList());
            await File.WriteAllTextAsync(_filePath, json);
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
        }
        else
        {
            if (string.IsNullOrEmpty(account.id)) account.id = Guid.NewGuid().ToString();
            account.last_updated = DateTime.Now;
            Accounts.Add(account);
        }
        _ = SaveAccountsAsync();
    }

    public void RemoveAccount(string accountId)
    {
        var acc = Accounts.FirstOrDefault(a => a.id == accountId);
        if (acc != null)
        {
            Accounts.Remove(acc);
            _tokenStorage.RemoveTokens(accountId);
            _ = SaveAccountsAsync();
        }
    }

    public async Task ExportAccountsAsync(string targetPath)
    {
        try
        {
            var json = JsonSerializer.Serialize(Accounts.ToList());
            await File.WriteAllTextAsync(targetPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to export accounts: {ex.Message}");
            throw;
        }
    }

    public async Task ImportAccountsAsync(string sourcePath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(sourcePath);
            var list = JsonSerializer.Deserialize<List<CloudAccount>>(json);
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
            System.Diagnostics.Debug.WriteLine($"Failed to import accounts: {ex.Message}");
            throw;
        }
    }
}

using System.Collections.ObjectModel;
using System.Text.Json;
using AIUsageMonitor.Models;

namespace AIUsageMonitor.Services;

public class AccountManagerService
{
    private const string FileName = "accounts.json";
    private readonly string _filePath;
    public ObservableCollection<CloudAccount> Accounts { get; } = new();

    public AccountManagerService()
    {
        _filePath = Path.Combine(FileSystem.AppDataDirectory, FileName);
        LoadAccounts();
    }

    public void LoadAccounts()
    {
        if (!File.Exists(_filePath)) return;

        try
        {
            var json = File.ReadAllText(_filePath);
            var list = JsonSerializer.Deserialize<List<CloudAccount>>(json);
            if (list != null)
            {
                Accounts.Clear();
                // Deduplicate by email just in case the file is corrupted
                var uniqueList = list.GroupBy(a => a.email.ToLower()).Select(g => g.First());
                foreach (var acc in uniqueList) Accounts.Add(acc);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load accounts: {ex.Message}");
        }
    }

    public void SaveAccounts()
    {
        try
        {
            var json = JsonSerializer.Serialize(Accounts.ToList());
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save accounts: {ex.Message}");
        }
    }

    public void AddOrUpdateAccount(CloudAccount account)
    {
        if (string.IsNullOrEmpty(account.email)) return;
        var existing = Accounts.FirstOrDefault(a => a.email.ToLower() == account.email.ToLower());
        if (existing != null)
        {
            existing.refresh_token = account.refresh_token;
            existing.name = account.name;
            existing.avatar_url = account.avatar_url;
            existing.credits = account.credits;
            existing.quotas = account.quotas;
            existing.last_updated = DateTime.Now;
        }
        else
        {
            account.id = Guid.NewGuid().ToString();
            account.last_updated = DateTime.Now;
            Accounts.Add(account);
        }
        SaveAccounts();
    }

    public void RemoveAccount(string accountId)
    {
        var acc = Accounts.FirstOrDefault(a => a.id == accountId);
        if (acc != null)
        {
            Accounts.Remove(acc);
            SaveAccounts();
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
                foreach (var acc in list)
                {
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

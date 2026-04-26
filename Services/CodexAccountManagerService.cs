using System.Collections.ObjectModel;
using System.Text.Json;
using AIUsageMonitor.Models;

namespace AIUsageMonitor.Services;

public class CodexAccountManagerService
{
    private const string FileName = "codex_accounts.json";
    private readonly string _filePath;
    public ObservableCollection<CodexAccount> Accounts { get; } = new();

    public CodexAccountManagerService()
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
            var list = JsonSerializer.Deserialize<List<CodexAccount>>(json);
            if (list != null)
            {
                Accounts.Clear();
                foreach (var acc in list) Accounts.Add(acc);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load codex accounts: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"Failed to save codex accounts: {ex.Message}");
        }
    }

    public void AddOrUpdateAccount(CodexAccount account)
    {
        var existing = Accounts.FirstOrDefault(a => a.id == account.id || (!string.IsNullOrEmpty(a.access_token) && a.access_token == account.access_token));
        if (existing != null)
        {
            existing.access_token = account.access_token;
            existing.name = account.name;
            existing.email = account.email;
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
            existing.secondaryResetDate = account.secondaryResetDate;
            if (!string.IsNullOrEmpty(account.refresh_token))
                existing.refresh_token = account.refresh_token;
            if (!string.IsNullOrEmpty(account.login_method))
                existing.login_method = account.login_method;
            existing.last_updated = DateTime.Now;
        }
        else
        {
            if (string.IsNullOrEmpty(account.id)) account.id = Guid.NewGuid().ToString();
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
            System.Diagnostics.Debug.WriteLine($"Failed to export codex accounts: {ex.Message}");
            throw;
        }
    }

    public async Task ImportAccountsAsync(string sourcePath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(sourcePath);
            var list = JsonSerializer.Deserialize<List<CodexAccount>>(json);
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
            System.Diagnostics.Debug.WriteLine($"Failed to import codex accounts: {ex.Message}");
            throw;
        }
    }
}

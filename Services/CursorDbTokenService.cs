using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using System.Diagnostics;

namespace AIUsageMonitor.Services;

public class CursorDbTokenService
{
    static CursorDbTokenService()
    {
        try
        {
            SQLitePCL.Batteries_V2.Init();
            Log.Info("SQLitePCL batteries initialized.");
        }
        catch (Exception ex)
        {
            Log.Info($"SQLite init warning: {ex.Message}");
        }
    }

    private static readonly string[] CandidateDbPaths =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor", "User", "globalStorage", "state.vscdb"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor Nightly", "User", "globalStorage", "state.vscdb")
    };

    public string? FindCursorDbPath()
    {
        var found = CandidateDbPaths.FirstOrDefault(File.Exists);
        Log.Info($"FindCursorDbPath => {(found ?? "NOT_FOUND")}");
        return found;
    }

    public async Task<(bool Success, string? SessionToken, string? Email, string Message, string? DbPath)> TryReadCurrentSessionAsync()
    {
        var dbPath = FindCursorDbPath();
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            Log.Info("DB path not found.");
            return (false, null, null, "Cursor DB not found.", null);
        }

        try
        {
            Log.Info($"Opening DB: {dbPath}");
            await using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
            await conn.OpenAsync();

            var accessToken = await TryReadAccessTokenAsync(conn);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                Log.Info("cursorAuth/accessToken missing.");
                return (false, null, null, "No cursorAuth/accessToken in DB.", dbPath);
            }

            var userId = TryExtractUserId(accessToken);
            if (string.IsNullOrWhiteSpace(userId))
            {
                Log.Info("userId extraction failed.");
                return (false, null, null, "Failed to decode Cursor access token.", dbPath);
            }

            var sessionToken = $"{userId}%3A%3A{accessToken}";
            var email = TryExtractEmail(accessToken);
            Log.Info($"Session extracted. userId={userId}, email={(email ?? "N/A")}, tokenLen={sessionToken.Length}");
            await DumpContextCandidatesForCurrentInstallationAsync();
            return (true, sessionToken, email, "OK", dbPath);
        }
        catch (Exception ex)
        {
            Log.Error("Exception during session extraction", ex);
            return (false, null, null, ex.Message, dbPath);
        }
    }

    public async Task DumpContextCandidatesForCurrentInstallationAsync()
    {
        var dbPath = FindCursorDbPath();
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            Log.Info("Context candidate dump skipped: DB path not found.");
            return;
        }

        await DumpContextCandidatesAsync(dbPath);
    }

    public async Task<(double Percent, string ComposerName)?> TryReadContextUsageAsync()
    {
        var dbPath = FindCursorDbPath();
        if (string.IsNullOrWhiteSpace(dbPath))
            return null;

        try
        {
            await using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM ItemTable WHERE key = 'composer.composerHeaders' LIMIT 1;";
            var raw = (await cmd.ExecuteScalarAsync())?.ToString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                Log.Info("composer.composerHeaders missing.");
                return null;
            }

            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("allComposers", out var allComposers) ||
                allComposers.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            double bestPercent = -1;
            string bestName = "";
            long bestTimestamp = 0;

            foreach (var composer in allComposers.EnumerateArray())
            {
                if (!composer.TryGetProperty("contextUsagePercent", out var percentEl) ||
                    percentEl.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                var percent = percentEl.GetDouble();
                var timestamp = TryGetInt64(composer, "lastUpdatedAt")
                    ?? TryGetInt64(composer, "conversationCheckpointLastUpdatedAt")
                    ?? TryGetInt64(composer, "createdAt")
                    ?? 0;

                var isBetter = timestamp > bestTimestamp || (timestamp == bestTimestamp && percent > bestPercent);
                if (!isBetter)
                    continue;

                bestPercent = percent;
                bestTimestamp = timestamp;
                bestName = TryGetString(composer, "name")
                    ?? TryGetString(composer, "subtitle")
                    ?? TryGetString(composer, "composerId")
                    ?? "";
            }

            if (bestPercent < 0)
                return null;

            Log.Info($"Context usage selected: percent={bestPercent:F1}, composer={bestName}, ts={bestTimestamp}");
            return (bestPercent, bestName);
        }
        catch (Exception ex)
        {
            Log.Error("Context usage read failed", ex);
            return null;
        }
    }

    private static async Task DumpContextCandidatesAsync(string globalDbPath)
    {
        try
        {
            Log.Info("Context candidate dump start.");
            await DumpMatchingKeysAsync(globalDbPath, "globalStorage");

            var cursorUserDir = Directory.GetParent(Directory.GetParent(globalDbPath)!.FullName)!.FullName;
            var workspaceRoot = Path.Combine(cursorUserDir, "workspaceStorage");
            if (!Directory.Exists(workspaceRoot))
            {
                Log.Info($"workspaceStorage not found: {workspaceRoot}");
                return;
            }

            foreach (var workspaceDb in Directory.EnumerateFiles(workspaceRoot, "state.vscdb", SearchOption.AllDirectories).Take(20))
            {
                await DumpMatchingKeysAsync(workspaceDb, $"workspace:{Path.GetFileName(Path.GetDirectoryName(workspaceDb))}");
            }

            Log.Info("Context candidate dump end.");
        }
        catch (Exception ex)
        {
            Log.Error("Context candidate dump failed", ex);
        }
    }

    private static async Task DumpMatchingKeysAsync(string dbPath, string label)
    {
        try
        {
            await using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT key, value
FROM ItemTable
WHERE lower(key) LIKE '%context%'
   OR lower(key) LIKE '%composer%'
   OR lower(key) LIKE '%chat%'
   OR lower(key) LIKE '%usage%'
   OR lower(value) LIKE '%context%'
   OR lower(value) LIKE '%composer%'
LIMIT 80;";

            await using var reader = await cmd.ExecuteReaderAsync();
            var count = 0;
            while (await reader.ReadAsync())
            {
                count++;
                var key = reader.IsDBNull(0) ? "" : reader.GetString(0);
                var value = reader.IsDBNull(1) ? "" : reader.GetString(1);
                Log.Info($"[{label}] candidate key={key}, value={Truncate(value, 1200)}");
            }

            Log.Info($"[{label}] candidate count={count}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[CursorDB][{label}] candidate scan failed: {ex.Message}");
        }
    }

    private static async Task<string?> TryReadAccessTokenAsync(SqliteConnection conn)
    {
        var keysToTry = new[]
        {
            "cursorAuth/accessToken",
            "cursorAuth/sessionToken",
            "cursorAuth/token"
        };

        foreach (var key in keysToTry)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM ItemTable WHERE key = $key LIMIT 1;";
            cmd.Parameters.AddWithValue("$key", key);
            var valueObj = await cmd.ExecuteScalarAsync();
            var value = valueObj?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                Trace.WriteLine($"[CursorDB] Token found by key: {key}");
                return ExtractJwtFromValue(value);
            }
        }

        // Fallback: inspect cursorAuth-related keys for schema changes
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT key, value FROM ItemTable WHERE key LIKE '%cursorAuth%' LIMIT 20;";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var key = reader.IsDBNull(0) ? "" : reader.GetString(0);
                var value = reader.IsDBNull(1) ? "" : reader.GetString(1);
                Trace.WriteLine($"[CursorDB] Fallback key: {key}");

                var extracted = ExtractJwtFromValue(value);
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    Trace.WriteLine($"[CursorDB] Token extracted from fallback key: {key}");
                    return extracted;
                }
            }
        }

        return null;
    }

    private static string? ExtractJwtFromValue(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // direct JWT
        if (raw.Count(c => c == '.') >= 2)
            return raw;

        // JSON container
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var candidateProps = new[] { "accessToken", "token", "jwt", "idToken" };
            foreach (var prop in candidateProps)
            {
                if (root.TryGetProperty(prop, out var el))
                {
                    var token = el.GetString();
                    if (!string.IsNullOrWhiteSpace(token) && token.Count(c => c == '.') >= 2)
                        return token;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? TryExtractUserId(string jwt)
    {
        var payload = TryExtractJwtPayload(jwt);
        if (payload is not JsonElement payloadValue)
            return null;

        if (!payloadValue.TryGetProperty("sub", out var subEl))
            return null;

        var sub = subEl.GetString();
        if (string.IsNullOrWhiteSpace(sub))
            return null;

        var parts = sub.Split('|');
        return parts.Length > 1 ? parts[1] : null;
    }

    private static string? TryExtractEmail(string jwt)
    {
        var payload = TryExtractJwtPayload(jwt);
        if (payload is not JsonElement payloadValue)
            return null;

        if (payloadValue.TryGetProperty("email", out var emailEl))
            return emailEl.GetString();

        return null;
    }

    private static JsonElement? TryExtractJwtPayload(string jwt)
    {
        try
        {
            var segments = jwt.Split('.');
            if (segments.Length < 2)
                return null;

            var payload = segments[1]
                .Replace('-', '+')
                .Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            var bytes = Convert.FromBase64String(payload);
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;
        return value[..maxLength] + "...(truncated)";
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static long? TryGetInt64(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : null;
    }
}

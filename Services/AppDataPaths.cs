using System.Diagnostics;

namespace AIUsageMonitor.Services;

public static class AppDataPaths
{
    public static string UserDataDirectory { get; } = EnsureDirectory(Path.Combine(ExecutableDirectory, "userdata"));
    public static string DataDirectory { get; } = EnsureDirectory(Path.Combine(UserDataDirectory, "data"));
    public static string WebViewDirectory { get; } = EnsureDirectory(Path.Combine(UserDataDirectory, "webview"));

    private static string ExecutableDirectory => GetExecutableDirectory();

    public static string GetDataFilePath(string fileName, string? legacyDirectory = null)
    {
        var path = Path.Combine(DataDirectory, fileName);

        foreach (var sourceDirectory in GetLegacyDataDirectories(legacyDirectory))
        {
            if (File.Exists(path))
                break;

            var sourcePath = Path.Combine(sourceDirectory, fileName);
            if (File.Exists(sourcePath) && !PathsEqual(sourcePath, path))
            {
                File.Copy(sourcePath, path, overwrite: false);
                TryDeleteFile(sourcePath);
                Debug.WriteLine($"[Migration] Moved legacy data file to userdata: {fileName}");
            }
        }

        return path;
    }

    private static string GetExecutableDirectory()
    {
        var candidates = new[]
        {
            TryGetMainModulePath(),
            Environment.GetCommandLineArgs().FirstOrDefault(),
            Environment.ProcessPath,
            AppContext.BaseDirectory
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            var fullPath = Path.GetFullPath(candidate);
            var directory = Directory.Exists(fullPath)
                ? fullPath
                : Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrWhiteSpace(directory) &&
                !directory.Contains(Path.Combine("Temp", ".net"), StringComparison.OrdinalIgnoreCase))
            {
                return directory;
            }
        }

        return AppContext.BaseDirectory;
    }

    private static string? TryGetMainModulePath()
    {
        try
        {
            return Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> GetLegacyDataDirectories(string? legacyDirectory)
    {
        if (!string.IsNullOrWhiteSpace(legacyDirectory))
            yield return legacyDirectory;

        var extractedDataDirectory = Path.Combine(AppContext.BaseDirectory, "userdata", "data");
        if (!PathsEqual(extractedDataDirectory, DataDirectory))
            yield return extractedDataDirectory;

        var tempNetDirectory = Path.Combine(Path.GetTempPath(), ".net", "AIUsageMonitor");
        if (!Directory.Exists(tempNetDirectory))
            yield break;

        foreach (var dataDirectory in EnumerateDataDirectories(tempNetDirectory))
        {
            if (!PathsEqual(dataDirectory, DataDirectory))
                yield return dataDirectory;
        }
    }

    private static IEnumerable<string> EnumerateDataDirectories(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root, "data", SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Migration] Failed to scan extracted userdata directories: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Migration] Failed to delete legacy data file '{path}': {ex.Message}");
        }
    }

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }
}

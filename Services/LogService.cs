using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace AIUsageMonitor.Services;

public static class Log
{
    /// <summary>
    /// 일반 정보 로그를 남깁니다. 태그는 호출한 파일명이 자동으로 사용됩니다.
    /// </summary>
    public static void Info(string message, [CallerFilePath] string filePath = "")
    {
        string tag = Path.GetFileNameWithoutExtension(filePath);
        Debug.WriteLine($"[{tag}] {message}");
    }

    /// <summary>
    /// 오류 로그를 남깁니다.
    /// </summary>
    public static void Error(string message, Exception? ex = null, [CallerFilePath] string filePath = "")
    {
        string tag = Path.GetFileNameWithoutExtension(filePath);
        string exMsg = ex != null ? $" | Exception: {ex.Message}" : "";
        Debug.WriteLine($"[{tag}][ERROR] {message}{exMsg}");
    }

    /// <summary>
    /// 특정 태그를 직접 지정하여 로그를 남깁니다.
    /// </summary>
    public static void Write(string tag, string message)
    {
        Debug.WriteLine($"[{tag}] {message}");
    }
}

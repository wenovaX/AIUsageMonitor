using AIUsageMonitor.Services;

namespace AIUsageMonitor.Controls;

public class LocalDataWebView : WebView
#if WINDOWS
    , Microsoft.Maui.IInitializationAwareWebView
#endif
{
#if WINDOWS
    void Microsoft.Maui.IInitializationAwareWebView.WebViewInitializationStarted(
        Microsoft.Maui.WebViewInitializationStartedEventArgs args)
    {
        args.UserDataFolder = AppDataPaths.WebViewDirectory;
    }

    void Microsoft.Maui.IInitializationAwareWebView.WebViewInitializationCompleted(
        Microsoft.Maui.WebViewInitializationCompletedEventArgs args)
    {
    }
#endif
}

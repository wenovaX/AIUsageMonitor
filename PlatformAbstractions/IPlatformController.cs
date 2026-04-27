using System.Windows.Input;

namespace AIUsageMonitor.PlatformAbstractions;

public interface IPlatformController
{
    bool SupportsTray { get; }
    bool IsWindowVisible { get; }

    event EventHandler? WindowVisibilityChanged;

    void Initialize(Window window);
    void ConfigureTrayIcon(object trayIcon, ICommand showCommand, ICommand exitCommand, ICommand activateCommand);
    void ShowMainWindow();
    void ExitApplication();

    bool GetMinimizeToTray();
    void SetMinimizeToTray(bool value);
    bool GetRememberCloseChoice();
    void SetRememberCloseChoice(bool value);
}

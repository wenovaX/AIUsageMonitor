using System.Windows.Input;

namespace AIUsageMonitor.PlatformAbstractions;

public abstract class BasePlatformController : IPlatformController
{
    protected Window? MainWindow { get; private set; }

    public virtual bool SupportsTray => false;
    public virtual bool IsWindowVisible => true;

    public event EventHandler? WindowVisibilityChanged;

    public virtual void Initialize(Window window)
    {
        MainWindow = window;
    }

    public virtual void ConfigureTrayIcon(object trayIcon, ICommand showCommand, ICommand exitCommand, ICommand activateCommand)
    {
    }

    public virtual void ShowMainWindow()
    {
    }

    public virtual void ExitApplication()
    {
        MainThread.BeginInvokeOnMainThread(() => Application.Current?.Quit());
    }

    public virtual bool GetMinimizeToTray() => true;

    public virtual void SetMinimizeToTray(bool value)
    {
    }

    public virtual bool GetRememberCloseChoice() => false;

    public virtual void SetRememberCloseChoice(bool value)
    {
    }

    protected void RaiseWindowVisibilityChanged()
    {
        WindowVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }
}

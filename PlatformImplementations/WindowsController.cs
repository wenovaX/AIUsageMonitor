using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using AIUsageMonitor.PlatformAbstractions;
using H.NotifyIcon;
using H.NotifyIcon.Core;
#if WINDOWS
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
#endif

namespace AIUsageMonitor.PlatformImplementations;

public class WindowsController : BasePlatformController
{
    private bool _isExiting;
    private bool _isTrayIconInitialized;
    private bool _isResizeMoveActive;

#if WINDOWS
    private TaskbarIcon? _trayIcon;
    private Microsoft.Maui.Controls.MenuFlyoutItem? _showAppMenuItem;
    private Microsoft.UI.Xaml.Window? _platformWindow;
    private AppWindow? _appWindow;
    private IntPtr _windowHandle;
    private SubclassProc? _subclassProc;
    private bool _isCloseDialogOpen;
#endif

    public override bool SupportsTray => true;
    public override bool IsWindowResizeInProgress => _isResizeMoveActive;

    public override bool IsWindowVisible
    {
        get
        {
#if WINDOWS
            if (_isExiting)
                return false;

            return _platformWindow?.AppWindow.IsVisible ?? true;
#else
            return true;
#endif
        }
    }

    public override void Initialize(Microsoft.Maui.Controls.Window window)
    {
        base.Initialize(window);

#if WINDOWS
        window.Width = Preferences.Default.Get("WindowWidth", 1130.0);
        window.Height = Preferences.Default.Get("WindowHeight", 900.0);
        window.MinimumWidth = 850;
        window.MinimumHeight = 700;
        window.Title = "AIUsageMonitor";

        window.Destroying += (_, _) =>
        {
            try
            {
                Preferences.Default.Set("WindowWidth", window.Width);
                Preferences.Default.Set("WindowHeight", window.Height);
                PrepareForShutdown();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Window destroy cleanup failed: {ex.Message}");
            }
        };

        window.HandlerChanged += OnWindowHandlerChanged;
#endif
    }

    public override void ConfigureTrayIcon(object trayIcon, ICommand showCommand, ICommand exitCommand, ICommand activateCommand)
    {
#if WINDOWS
        if (trayIcon is not TaskbarIcon taskbarIcon)
            return;

        _trayIcon = taskbarIcon;
        _trayIcon.DoubleClickCommand = activateCommand;
        _trayIcon.LeftClickCommand = activateCommand;
        _trayIcon.IconSource ??= ResolveTrayIconSource();

        _showAppMenuItem = new Microsoft.Maui.Controls.MenuFlyoutItem
        {
            Text = "Show App",
            Command = showCommand,
            IsEnabled = false
        };

        var menuFlyout = new Microsoft.Maui.Controls.MenuFlyout();
        menuFlyout.Add(_showAppMenuItem);
        menuFlyout.Add(new Microsoft.Maui.Controls.MenuFlyoutSeparator());
        menuFlyout.Add(new Microsoft.Maui.Controls.MenuFlyoutItem
        {
            Text = "Exit App",
            Command = exitCommand
        });
        FlyoutBase.SetContextFlyout(_trayIcon, menuFlyout);

        EnsureTrayIconInitialized();
#endif
    }

    public override void ShowMainWindow()
    {
#if WINDOWS
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_isExiting)
                return;

            _platformWindow?.AppWindow.Show();
            _platformWindow?.Activate();
            UpdateTrayMenuState(false);
            RaiseWindowVisibilityChanged();
        });
#endif
    }

    public override void ExitApplication()
    {
        if (_isExiting)
            return;

        _isExiting = true;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
#if WINDOWS
                PrepareForShutdown();
#endif
                Microsoft.Maui.Controls.Application.Current?.Quit();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Application quit failed: {ex.Message}");
            }
        });
    }

    public override bool GetMinimizeToTray() => Preferences.Default.Get("MinimizeToTray", true);

    public override void SetMinimizeToTray(bool value) => Preferences.Default.Set("MinimizeToTray", value);

    public override bool GetRememberCloseChoice() => Preferences.Default.Get("RememberCloseChoice", false);

    public override void SetRememberCloseChoice(bool value) => Preferences.Default.Set("RememberCloseChoice", value);

#if WINDOWS
    private void OnWindowHandlerChanged(object? sender, EventArgs e)
    {
        if (_isExiting)
            return;

        if (MainWindow?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window window)
            return;

        _platformWindow = window;

        _windowHandle = WindowNative.GetWindowHandle(window);
        EnsureResizeSubclass();

        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_windowHandle);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow = appWindow;

        appWindow.Closing -= OnAppWindowClosing;
        appWindow.Closing += OnAppWindowClosing;

        EnsureTrayIconInitialized();
    }

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        try
        {
            var rememberChoice = GetRememberCloseChoice();
            var minimizeToTray = GetMinimizeToTray();

            if (_isExiting)
            {
                return;
            }

            if (rememberChoice)
            {
                if (minimizeToTray)
                {
                    args.Cancel = true;
                    sender.Hide();
                    UpdateTrayMenuState(true);
                    RaiseWindowVisibilityChanged();
                }

                return;
            }

            args.Cancel = true;

            if (_isCloseDialogOpen || _platformWindow?.Content?.XamlRoot is null)
                return;

            _isCloseDialogOpen = true;
            try
            {
                var rememberChoiceCheckBox = new Microsoft.UI.Xaml.Controls.CheckBox
                {
                    Content = "Remember this choice"
                };

                var panel = new StackPanel { Spacing = 10 };
                panel.Children.Add(new TextBlock
                {
                    Text = "Send the app to the tray and keep it running in the background?",
                    TextWrapping = TextWrapping.Wrap
                });
                panel.Children.Add(rememberChoiceCheckBox);

                var dialog = new ContentDialog
                {
                    Title = "Close app",
                    Content = panel,
                    PrimaryButtonText = "Send to tray",
                    SecondaryButtonText = "Exit app",
                    CloseButtonText = "Cancel",
                    XamlRoot = _platformWindow.Content.XamlRoot
                };

                var result = await dialog.ShowAsync();
                bool saveChoice = rememberChoiceCheckBox.IsChecked == true;

                if (result == ContentDialogResult.Primary)
                {
                    if (saveChoice)
                    {
                        SetRememberCloseChoice(true);
                        SetMinimizeToTray(true);
                    }

                    ShowBackgroundNotification();
                    sender.Hide();
                    UpdateTrayMenuState(true);
                    RaiseWindowVisibilityChanged();
                }
                else if (result == ContentDialogResult.Secondary)
                {
                    if (saveChoice)
                    {
                        SetRememberCloseChoice(true);
                        SetMinimizeToTray(false);
                    }

                    ExitApplication();
                }
            }
            finally
            {
                _isCloseDialogOpen = false;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Window closing failed: {ex.Message}");
            if (!_isExiting)
            {
                args.Cancel = false;
            }
        }
    }

    private void PrepareForShutdown()
    {
        _isExiting = true;
        _isResizeMoveActive = false;
        _isCloseDialogOpen = false;

        if (_appWindow is not null)
        {
            try
            {
                _appWindow.Closing -= OnAppWindowClosing;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AppWindow closing cleanup failed: {ex.Message}");
            }
            _appWindow = null;
        }

        if (_subclassProc is not null && _windowHandle != IntPtr.Zero)
        {
            try
            {
                RemoveWindowSubclass(_windowHandle, _subclassProc, 1);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Window subclass cleanup failed: {ex.Message}");
            }
            _subclassProc = null;
            _windowHandle = IntPtr.Zero;
        }

        if (_trayIcon is not null)
        {
            try
            {
                _trayIcon.LeftClickCommand = null;
                _trayIcon.DoubleClickCommand = null;
                FlyoutBase.SetContextFlyout(_trayIcon, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Tray cleanup failed: {ex.Message}");
            }
        }

        _platformWindow = null;
        _showAppMenuItem = null;
        _isTrayIconInitialized = false;
    }

    private void EnsureTrayIconInitialized()
    {
        if (_isExiting || _trayIcon is null || _isTrayIconInitialized)
            return;

        try
        {
            _trayIcon.IconSource ??= ResolveTrayIconSource();
            _trayIcon.ForceCreate(false);
            _isTrayIconInitialized = _trayIcon.IsCreated;

            if (!_isTrayIconInitialized)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_isExiting || _trayIcon is null)
                        return;

                    _trayIcon.IconSource ??= ResolveTrayIconSource();
                    _trayIcon.ForceCreate(false);
                    _isTrayIconInitialized = _trayIcon.IsCreated;
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Tray icon initialization failed: {ex.Message}");
        }
    }

    private ImageSource ResolveTrayIconSource()
    {
        const string trayIconFileName = "trayicon.ico";
        var outputFilePath = Path.Combine(AppContext.BaseDirectory, trayIconFileName);

        if (File.Exists(outputFilePath))
        {
            return ImageSource.FromFile(trayIconFileName);
        }

        return new GeneratedIconSource
        {
            Text = "A",
            Background = new SolidColorBrush(Color.FromArgb("#10b981")),
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 72
        };
    }

    private void UpdateTrayMenuState(bool isMinimizedToTray)
    {
        if (_isExiting)
            return;

        if (_showAppMenuItem is not null)
        {
            _showAppMenuItem.IsEnabled = isMinimizedToTray;
        }
    }

    private void ShowBackgroundNotification()
    {
        if (_isExiting)
            return;

        try
        {
            _trayIcon?.ShowNotification(
                "AIUsageMonitor",
                "App is still running in the system tray.",
                NotificationIcon.Info,
                customIconHandle: null,
                largeIcon: false,
                respectQuietTime: true,
                realtime: false,
                sound: false,
                timeout: TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
    }

    private void EnsureResizeSubclass()
    {
        if (_isExiting || _windowHandle == IntPtr.Zero || _subclassProc is not null)
            return;

        _subclassProc = WindowSubclassProc;
        SetWindowSubclass(_windowHandle, _subclassProc, 1, IntPtr.Zero);
    }

    private IntPtr WindowSubclassProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        switch (message)
        {
            case WmEnterSizeMove:
                _isResizeMoveActive = true;
                break;

            case WmExitSizeMove:
                _isResizeMoveActive = false;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (!_isExiting)
                    {
                        RaiseWindowResizeCompleted();
                    }
                });
                break;

            case WmNcDestroy:
                if (_subclassProc is not null)
                {
                    RemoveWindowSubclass(hWnd, _subclassProc, 1);
                }
                _subclassProc = null;
                _windowHandle = IntPtr.Zero;
                _isResizeMoveActive = false;
                break;
        }

        return DefSubclassProc(hWnd, message, wParam, lParam);
    }

    private const uint WmEnterSizeMove = 0x0231;
    private const uint WmExitSizeMove = 0x0232;
    private const uint WmNcDestroy = 0x0082;

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, uint uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, uint uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);
#endif
}

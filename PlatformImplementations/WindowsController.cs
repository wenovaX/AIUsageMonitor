using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using AIUsageMonitor.PlatformAbstractions;
using H.NotifyIcon;
using H.NotifyIcon.Core;
#if WINDOWS
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

#if WINDOWS
    private TaskbarIcon? _trayIcon;
    private Microsoft.Maui.Controls.MenuFlyoutItem? _showAppMenuItem;
    private Microsoft.UI.Xaml.Window? _platformWindow;
#endif

    public override bool SupportsTray => true;

    public override bool IsWindowVisible
    {
        get
        {
#if WINDOWS
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
            Preferences.Default.Set("WindowWidth", window.Width);
            Preferences.Default.Set("WindowHeight", window.Height);
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
            _platformWindow?.AppWindow.Show();
            _platformWindow?.Activate();
            UpdateTrayMenuState(false);
            RaiseWindowVisibilityChanged();
        });
#endif
    }

    public override void ExitApplication()
    {
        _isExiting = true;
        MainThread.BeginInvokeOnMainThread(() => Microsoft.Maui.Controls.Application.Current?.Quit());
    }

    public override bool GetMinimizeToTray() => Preferences.Default.Get("MinimizeToTray", true);

    public override void SetMinimizeToTray(bool value) => Preferences.Default.Set("MinimizeToTray", value);

    public override bool GetRememberCloseChoice() => Preferences.Default.Get("RememberCloseChoice", false);

    public override void SetRememberCloseChoice(bool value) => Preferences.Default.Set("RememberCloseChoice", value);

#if WINDOWS
    private void OnWindowHandlerChanged(object? sender, EventArgs e)
    {
        if (MainWindow?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window window)
            return;

        _platformWindow = window;

        var handle = WindowNative.GetWindowHandle(window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        appWindow.Closing -= OnAppWindowClosing;
        appWindow.Closing += OnAppWindowClosing;

        EnsureTrayIconInitialized();
    }

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
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

        if (_platformWindow is null)
            return;

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

    private void EnsureTrayIconInitialized()
    {
        if (_trayIcon is null || _isTrayIconInitialized)
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
                    if (_trayIcon is null)
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
        if (_showAppMenuItem is not null)
        {
            _showAppMenuItem.IsEnabled = isMinimizedToTray;
        }
    }

    private void ShowBackgroundNotification()
    {
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
#endif
}

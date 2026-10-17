using AIUsageMonitor.Models;
using AIUsageMonitor.Localization;
using AIUsageMonitor.PlatformAbstractions;
using AIUsageMonitor.Services;
using AIUsageMonitor.Views;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows.Input;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#if WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using VirtualKey = Windows.System.VirtualKey;
#endif
namespace AIUsageMonitor;

public partial class MainPage : ContentPage
{
    private enum RefreshQueueLevel
    {
        Immediate,
        Background
    }

    private sealed record RefreshOperation(
        Func<bool> IsRefreshing,
        Func<bool> IsQueued,
        Action<bool> SetQueued,
        Action<bool> SetRefreshing,
        Action ClearError,
        Action<Exception> SetError,
        Func<Task> RefreshAsync,
        int RetryCount,
        int TimeoutSeconds,
        bool DelayForAnimation,
        Func<Exception, Task<bool>>? RecoverAsync = null);

    private readonly ModelCatalogService _modelCatalogService;
    private readonly GoogleRadarService _googleRadar;
    private readonly AccountManagerService _accountManager = new();
    private readonly CodexAccountManagerService _codexAccountManager = new();
    private readonly CursorAccountManagerService _cursorAccountManager = new();
    private readonly CursorDbTokenService _cursorDbTokenService = new();
    private readonly OpenAIAuthService _openAIAuth = new();
    private readonly CodexApiService _codexApi = new();
    private readonly CursorApiService _cursorApi = new();
    private readonly PlatformManager _platformManager;
    private readonly TokenStorageService _tokenStorage;
    private readonly NotificationService _notificationService;
    private readonly Dictionary<string, MainPageTabView> _tabContentCache = new(StringComparer.Ordinal);
    private ObservableCollection<ModelCatalogSection>? _modelCatalogSections;
    private bool _isRemovingAccount;
    public IReadOnlyList<DiscoveredModelEntry> GeminiCatalogModels =>
        _modelCatalogService.Models.Where(model => model.IsGemini).ToList();

    public IReadOnlyList<DiscoveredModelEntry> OtherCatalogModels =>
        _modelCatalogService.Models.Where(model => !model.IsGemini).ToList();

    public ObservableCollection<ModelCatalogSection> ModelCatalogSections => _modelCatalogSections ??= new()
    {
        new("GEMINI", GeminiCatalogModels),
        new(LocalizationManager.Text("dashboard.others"), OtherCatalogModels)
    };

    public bool CanCleanMissingModels => _modelCatalogService.HasAvailabilitySnapshot && _modelCatalogService.MissingModelCount > 0;

    public int AccountCardsPerRow => CalculateAccountCardsPerRow();

    // Tab Navigation Logic
    private string _currentTab = "Google";
    public string CurrentTab
    {
        get => _currentTab;
        set 
        { 
            if (_currentTab == value)
                return;

            var previousTab = _currentTab;
            _currentTab = value; 

            if (previousTab == "Settings" && value != "Settings")
            {
                _modelCatalogService.ResumeCatalogChangedNotifications();
                if (_hasPendingModelCatalogUiRefresh)
                {
                    _hasPendingModelCatalogUiRefresh = false;
                    RefreshModelCatalogUi();
                }
            }

            if (value == "Settings")
            {
                _modelCatalogService.SuspendCatalogChangedNotifications();
            }

            OnPropertyChanged(); 
            OnPropertyChanged(nameof(IsGoogleTab));
            OnPropertyChanged(nameof(IsCursorTab));
            OnPropertyChanged(nameof(IsCodexTab));
            OnPropertyChanged(nameof(IsSettingsTab));
            OnPropertyChanged(nameof(IsNotificationsTab));
            OnPropertyChanged(nameof(IsAboutTab));
            OnPropertyChanged(nameof(CanAddAccount));
            OnPropertyChanged(nameof(AddAccountButtonText));

            ShowTabContent(value);

            if (value == "Cursor")
            {
                RefreshCursorDbState();
            }
            NotifyStatsChanged();
        }
    }

    public bool IsGoogleTab => CurrentTab == "Google";
    public bool IsCursorTab => CurrentTab == "Cursor";
    public bool IsCodexTab => CurrentTab == "Codex";
    public bool IsSettingsTab => CurrentTab == "Settings";
    public bool IsNotificationsTab => CurrentTab == "Notifications";
    public bool IsAboutTab => CurrentTab == "About";
    public bool HasGoogleAccounts => Accounts.Count > 0;
    public bool HasNoGoogleAccounts => !HasGoogleAccounts;
    public bool HasCodexAccounts => CodexAccounts.Count > 0;
    public bool HasNoCodexAccounts => !HasCodexAccounts;

    private bool _isAnonymousMode;
    public bool IsAnonymousMode
    {
        get => _isAnonymousMode;
        set 
        { 
            _isAnonymousMode = value; 
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(ToggleKnobHorizontalOptions));
            OnPropertyChanged(nameof(ToggleColor));
            
            // Sync with all accounts
            foreach (var acc in Accounts) acc.IsAnonymous = _isAnonymousMode;
            foreach (var acc in CodexAccounts) acc.IsAnonymous = _isAnonymousMode;
        }
    }

    public LayoutOptions ToggleKnobHorizontalOptions => IsAnonymousMode ? LayoutOptions.End : LayoutOptions.Start;
    public Color ToggleColor => IsAnonymousMode ? Color.FromArgb("#10b981") : Color.FromArgb("#64748b");

    private bool _isBusy;
    public new bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }


    public bool MinimizeToTray
    {
        get => _platformManager.Current.GetMinimizeToTray();
        set { _platformManager.Current.SetMinimizeToTray(value); OnPropertyChanged(); }
    }

    public bool RememberCloseChoice
    {
        get => _platformManager.Current.GetRememberCloseChoice();
        set { _platformManager.Current.SetRememberCloseChoice(value); OnPropertyChanged(); }
    }

    private readonly RefreshConcurrencyLimiter _refreshConcurrencyLimiter;
#if WINDOWS
    private Microsoft.UI.Xaml.Window? _platformWindow;
#endif
    private bool _hasPendingModelCatalogUiRefresh;
    private int _lastAccountCardsPerRow;

    private bool _isLoginWaiting;
    public bool IsLoginWaiting
    {
        get => _isLoginWaiting;
        set { _isLoginWaiting = value; OnPropertyChanged(); }
    }

    private bool _isCodexLoginOverlayVisible;
    public bool IsCodexLoginOverlayVisible
    {
        get => _isCodexLoginOverlayVisible;
        set { _isCodexLoginOverlayVisible = value; OnPropertyChanged(); }
    }

    public const string AppVersion = "1.1.0";
    public string DisplayVersion => $"v{AppVersion}";
    public string AboutVersionLabel => LocalizationManager.Format("about.version", DisplayVersion);
    public IReadOnlyList<string> LanguageOptions { get; } =
    [
        LocalizationManager.Text("settings.language_system"),
        "English",
        "한국어"
    ];
    public int LanguageSelectionIndex => LocalizationManager.NormalizePreference(NotificationSettings.LanguageCode) switch
    {
        "en" => 1,
        "ko" => 2,
        _ => 0
    };

    private CancellationTokenSource? _loginCts;
    private CancellationTokenSource? _backgroundRefreshCts;
    private bool _isProcessingCodexLogin;
    private readonly object _refreshSchedulersLock = new();
    private bool _isCursorCredentialLoginWaiting;
    private bool _isShuttingDown;

    public bool IsCursorCredentialLoginWaiting
    {
        get => _isCursorCredentialLoginWaiting;
        set { _isCursorCredentialLoginWaiting = value; OnPropertyChanged(); }
    }

    public string CursorStoragePath => _cursorAccountManager.StorageDirectory;
    public string CursorDbPath => _cursorDbPath;
    public bool IsCursorDbAvailable => _isCursorDbAvailable;
    public bool IsCursorDbMissing => IsCursorTab && !_isCursorDbAvailable;
    public string CursorDbStatusMessage => _cursorDbStatusMessage;
    public bool CanAddAccount => !IsCursorTab || IsCursorDbAvailable;
    public string AddAccountButtonText => LocalizationManager.Text(IsCursorTab ? "app.add_current_account" : "app.add_account");
    public NotificationSettings NotificationSettings => _notificationService.Settings;
    private bool _isCursorDbAvailable;
    private string _cursorDbPath = LocalizationManager.Text("cursor.path_not_found");
    private string _cursorDbStatusMessage = LocalizationManager.Text("cursor.install_required");

    // Shared Settings Panel Logic
    private CloudAccount? _selectedAccount;
    public CloudAccount? SelectedAccount
    {
        get => _selectedAccount;
        set { _selectedAccount = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsSettingsPanelOpen)); }
    }

    public bool IsSettingsPanelOpen => SelectedAccount != null;

    // Tab-aware stats
    public int GlobalActions => CurrentTab == "Codex"
        ? CodexAccounts.Count
        : CurrentTab == "Cursor"
        ? CursorAccounts.Count
        : GetVisibleGoogleQuotasSnapshot().Count;
    public int GlobalActive => CurrentTab == "Codex"
        ? CodexAccounts.Count(a => !a.IsRateLimited)
        : CurrentTab == "Cursor"
        ? CursorAccounts.Count(a => a.context_usage_percent < 99.5)
        : GetVisibleGoogleQuotasSnapshot().Count(q => q.percentage > 0);
    public int GlobalLimited => CurrentTab == "Codex"
        ? CodexAccounts.Count(a => a.IsRateLimited)
        : CurrentTab == "Cursor"
        ? CursorAccounts.Count(a => a.context_bottom_hit || a.context_usage_percent >= 99.5)
        : GetVisibleGoogleQuotasSnapshot().Count(q => q.percentage == 0);
    public int GlobalQuota => CurrentTab == "Codex"
        ? (CodexAccounts.Count == 0 ? 0 : (int)CodexAccounts.Average(a => a.PrimaryRemainingPercent))
        : CurrentTab == "Cursor"
        ? (CursorAccounts.Count == 0 ? 0 : (int)CursorAccounts.Average(a => Math.Max(0, 100 - a.context_usage_percent)))
        : GetVisibleGoogleQuotaAverage();

    public ObservableCollection<CloudAccount> Accounts => _accountManager.Accounts;
    public ObservableCollection<CodexAccount> CodexAccounts => _codexAccountManager.Accounts;
    public ObservableCollection<CursorAccount> CursorAccounts => _cursorAccountManager.Accounts;

    private bool _isMinimizedToTray = false;

    public MainPage()
    {
        _platformManager = MauiProgram.Services.GetRequiredService<PlatformManager>();
        _modelCatalogService = MauiProgram.Services.GetRequiredService<ModelCatalogService>();
        _tokenStorage = MauiProgram.Services.GetRequiredService<TokenStorageService>();
        _notificationService = MauiProgram.Services.GetRequiredService<NotificationService>();
        _refreshConcurrencyLimiter = new RefreshConcurrencyLimiter(NotificationSettings.MaxConcurrentRefreshes);
        NotificationSettings.PropertyChanged += OnNotificationSettingsPropertyChanged;
        Debug.WriteLine($"Refresh concurrency initialized. Max={NotificationSettings.MaxConcurrentRefreshes}");

        _googleRadar = new GoogleRadarService();
        InitializeComponent();
        BindingContext = this;
        ShowTabContent(CurrentTab);
        HandlerChanged += OnHandlerChanged;
        Unloaded += OnUnloaded;
        SizeChanged += OnPageSizeChanged;
        
        ShowAppCommand = new Command(() => OnShowAppFromTray(null, EventArgs.Empty));
        ExitAppCommand = new Command(() => OnExitAppFromTray(null, EventArgs.Empty));
        DoubleClickTrayIconCommand = new Command(() => OnShowAppFromTray(null, EventArgs.Empty));
        Accounts.CollectionChanged += OnAccountsCollectionChanged;
        CodexAccounts.CollectionChanged += OnCodexAccountsCollectionChanged;
        CursorAccounts.CollectionChanged += OnCursorAccountsCollectionChanged;
        _modelCatalogService.CatalogChanged += OnModelCatalogChanged;
        UpdateDisplayIndices();
        InitializeAsync();
    }

    private void ShowTabContent(string tab)
    {
        if (TabContentHost is null)
            return;

        if (!_tabContentCache.TryGetValue(tab, out var content))
        {
            content = tab switch
            {
                "Google" => new GoogleTabView(),
                "Codex" => new CodexTabView(),
                "Cursor" => new CursorTabView(),
                "Notifications" => new NotificationsTabView(),
                "Settings" => new SettingsTabView(),
                "About" => new AboutTabView(),
                _ => new GoogleTabView()
            };
            content.BindingContext = this;
            content.AccountCardsPerRow = AccountCardsPerRow;
            _tabContentCache[tab] = content;
        }

        TabContentHost.Content = content;
    }

    private async void InitializeAsync()
    {
        if (_isShuttingDown)
            return;

        await _accountManager.LoadAccountsAsync();
        await _codexAccountManager.LoadAccountsAsync();
        await _cursorAccountManager.LoadAccountsAsync();
        if (_isShuttingDown)
            return;

        RefreshCursorDbState();

        UpdateDisplayIndices();
        UpdateCodexDisplayIndices();

        _ = RefreshAllAccounts(RefreshQueueLevel.Background);
        _ = RefreshAllCursorAccounts(RefreshQueueLevel.Background);
        _ = RefreshAllCodexAccounts(RefreshQueueLevel.Background);

        StartBackgroundRefreshLoop();
    }

    public ICommand ShowAppCommand { get; }
    public ICommand ExitAppCommand { get; }
    public ICommand DoubleClickTrayIconCommand { get; }

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        ConfigurePlatformUi();
#if WINDOWS
        AttachRefreshKeyboardShortcut();
#endif
    }

    private void OnNotificationSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotificationSettings.MaxConcurrentRefreshes))
            _refreshConcurrencyLimiter.SetMaxConcurrency(NotificationSettings.MaxConcurrentRefreshes);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ConfigurePlatformUi();
#if WINDOWS
        AttachRefreshKeyboardShortcut();
#endif
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        _isShuttingDown = true;
        _loginCts?.Cancel();
        _loginCts?.Dispose();
        _loginCts = null;
        _backgroundRefreshCts?.Cancel();
        _backgroundRefreshCts?.Dispose();
        _backgroundRefreshCts = null;
        NotificationSettings.PropertyChanged -= OnNotificationSettingsPropertyChanged;
        SizeChanged -= OnPageSizeChanged;
        Accounts.CollectionChanged -= OnAccountsCollectionChanged;
        CodexAccounts.CollectionChanged -= OnCodexAccountsCollectionChanged;
        CursorAccounts.CollectionChanged -= OnCursorAccountsCollectionChanged;
        _modelCatalogService.CatalogChanged -= OnModelCatalogChanged;
        _platformManager.Current.WindowVisibilityChanged -= OnPlatformWindowVisibilityChanged;
        _platformManager.Current.WindowResizeCompleted -= OnPlatformWindowResizeCompleted;
#if WINDOWS
        if (_platformWindow?.Content is UIElement rootElement)
        {
            rootElement.KeyDown -= OnWindowKeyDown;
        }
        _platformWindow = null;
#endif
    }

    private void ConfigurePlatformUi()
    {
        if (_isShuttingDown)
            return;

        _platformManager.Current.WindowVisibilityChanged -= OnPlatformWindowVisibilityChanged;
        _platformManager.Current.WindowVisibilityChanged += OnPlatformWindowVisibilityChanged;
        _platformManager.Current.WindowResizeCompleted -= OnPlatformWindowResizeCompleted;
        _platformManager.Current.WindowResizeCompleted += OnPlatformWindowResizeCompleted;
        _platformManager.Current.ConfigureTrayIcon(TrayIcon, ShowAppCommand, ExitAppCommand, DoubleClickTrayIconCommand);
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        if (_isShuttingDown)
            return;

        var cardsPerRow = CalculateAccountCardsPerRow();
        if (_lastAccountCardsPerRow == cardsPerRow)
            return;

        _lastAccountCardsPerRow = cardsPerRow;
        OnPropertyChanged(nameof(AccountCardsPerRow));
        foreach (var tabView in _tabContentCache.Values)
            tabView.AccountCardsPerRow = cardsPerRow;
    }

    private void OnAccountsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isShuttingDown)
            return;

        OnPropertyChanged(nameof(HasGoogleAccounts));
        OnPropertyChanged(nameof(HasNoGoogleAccounts));
    }

    private void OnPlatformWindowResizeCompleted(object? sender, EventArgs e)
    {
        if (_isShuttingDown)
            return;

        OnPageSizeChanged(sender, e);
    }

    private void OnCodexAccountsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isShuttingDown)
            return;

        OnPropertyChanged(nameof(HasCodexAccounts));
        OnPropertyChanged(nameof(HasNoCodexAccounts));
    }

    private void OnCursorAccountsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isShuttingDown)
            return;

        NotifyStatsChanged();
    }

    private void OnModelCatalogChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_isShuttingDown)
                return;

            SyncModelCatalogSections();
            OnPropertyChanged(nameof(CanCleanMissingModels));

            if (IsSettingsTab)
            {
                _hasPendingModelCatalogUiRefresh = true;
                return;
            }

            RefreshModelCatalogUi();
        });
    }

    private void RefreshModelCatalogUi()
    {
        SyncModelCatalogSections();

        foreach (var account in Accounts)
            account.NotifyQuotaChanges();
        NotifyStatsChanged();
    }

    internal async void OnUpdateModelListClicked(object? sender, EventArgs e)
    {
        var hasRegisteredAntigravityAccount = Accounts.Any(account =>
            !string.IsNullOrWhiteSpace(account.refresh_token) ||
            !string.IsNullOrWhiteSpace(account.access_token));

        if (!hasRegisteredAntigravityAccount)
        {
            await DisplayAlertAsync(
                LocalizationManager.Text("errors.antigravity_models_title"),
                LocalizationManager.Text("errors.antigravity_account_required"),
                LocalizationManager.Text("common.ok"));
            return;
        }

        var discoveredModels = Accounts
            .SelectMany(account => account.quotas)
            .Select(quota => quota.display_name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (discoveredModels.Count == 0)
        {
            await DisplayAlertAsync(
                LocalizationManager.Text("errors.antigravity_models_title"),
                LocalizationManager.Text("errors.antigravity_data_unavailable"),
                LocalizationManager.Text("common.ok"));
            return;
        }

        _modelCatalogService.UpdateAvailableModels(discoveredModels);
    }

    internal void OnSetModelListToDefaultClicked(object? sender, EventArgs e)
    {
        _modelCatalogService.ResetToDefaults();
        OnPropertyChanged(nameof(GeminiCatalogModels));
        OnPropertyChanged(nameof(OtherCatalogModels));
    }

    internal async void OnCleanMissingModelsClicked(object? sender, EventArgs e)
    {
        var missingCount = _modelCatalogService.MissingModelCount;
        if (missingCount == 0)
            return;

        var confirmed = await DisplayAlertAsync(
            LocalizationManager.Text("settings.clean_missing_models_title"),
            LocalizationManager.Format("settings.clean_missing_models_message", missingCount),
            LocalizationManager.Text("common.yes"),
            LocalizationManager.Text("common.no"));

        if (!confirmed)
            return;

        _modelCatalogService.RemoveMissingModels();
        OnPropertyChanged(nameof(CanCleanMissingModels));
    }

    internal void OnDecreaseRefreshConcurrencyClicked(object? sender, EventArgs e) =>
        NotificationSettings.MaxConcurrentRefreshes--;

    internal void OnIncreaseRefreshConcurrencyClicked(object? sender, EventArgs e) =>
        NotificationSettings.MaxConcurrentRefreshes++;

    private void SyncModelCatalogSections()
    {
        var sections = ModelCatalogSections;
        SyncModelCatalogSection(sections[0], GeminiCatalogModels);
        SyncModelCatalogSection(sections[1], OtherCatalogModels);
    }

    private static void SyncModelCatalogSection(ModelCatalogSection section, IReadOnlyList<DiscoveredModelEntry> entries)
    {
        if (section.SequenceEqual(entries))
            return;

        section.Clear();
        foreach (var entry in entries)
            section.Add(entry);
    }

    private int CalculateAccountCardsPerRow()
    {
        const double minimumCardWidth = 240;
        const double itemSpacing = 5;
        const double sidebarWidth = 80;
        const double collectionPadding = 40;
        var availableWidth = Width <= 0
            ? minimumCardWidth
            : Math.Max(minimumCardWidth, Width - sidebarWidth - collectionPadding);

        return Math.Max(1, (int)Math.Floor((availableWidth + itemSpacing) / (minimumCardWidth + itemSpacing)));
    }

    private List<ModelQuotaInfo> GetVisibleGoogleQuotasSnapshot()
    {
        return Accounts
            .ToList()
            .SelectMany(account =>
            {
                var hiddenModels = account.HiddenModels.ToHashSet(StringComparer.OrdinalIgnoreCase);
                return account.quotas
                    .ToList()
                    .Where(quota =>
                        !hiddenModels.Contains(quota.display_name) &&
                        _modelCatalogService.IsEnabled(quota.display_name));
            })
            .ToList();
    }

    private int GetVisibleGoogleQuotaAverage()
    {
        var quotas = GetVisibleGoogleQuotasSnapshot();
        return quotas.Count == 0 ? 0 : (int)quotas.Average(quota => quota.percentage);
    }

    private void OnShowAppFromTray(object? sender, EventArgs e)
    {
        _platformManager.Current.ShowMainWindow();
    }

    private void OnExitAppFromTray(object? sender, EventArgs e)
    {
        _platformManager.Current.ExitApplication();
    }

    private void OnPlatformWindowVisibilityChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_isShuttingDown)
                return;

            _isMinimizedToTray = !_platformManager.Current.IsWindowVisible;

            if (!_isMinimizedToTray)
            {
                _ = RefreshAllAccounts(RefreshQueueLevel.Background);
                _ = RefreshAllCursorAccounts(RefreshQueueLevel.Background);
                _ = RefreshAllCodexAccounts(RefreshQueueLevel.Background);
            }
        });
    }

    private void OnTabClicked(object? sender, EventArgs e)
    {
        if (sender is Border border)
        {
            var id = border.AutomationId;
            if (!string.IsNullOrEmpty(id))
            {
                CurrentTab = id;
                OnPropertyChanged(nameof(IsNotificationsTab));
            }
        }
    }


    private void OnAnonymousToggleClicked(object? sender, EventArgs e)
    {
        IsAnonymousMode = !IsAnonymousMode;
    }

    internal void OnToggleSettingsTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is CloudAccount account)
        {
            SelectedAccount = account;
        }
    }

    private void OnCloseSettingsClicked(object? sender, EventArgs e)
    {
        SelectedAccount = null;
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        if (CurrentTab == "Cursor")
        {
            if (!CanAddAccount)
            {
                await DisplayAlertAsync(
                    LocalizationManager.Text("errors.cursor_not_ready_title"),
                    LocalizationManager.Text("errors.cursor_not_ready"),
                    LocalizationManager.Text("common.ok"));
                return;
            }
            await PromptAddCursorAccount();
            return;
        }

        if (CurrentTab == "Codex")
        {
            await PromptAddCodexAccount();
            return;
        }

        await LoginWithGoogleAsync();
    }

    private async Task LoginWithGoogleAsync()
    {
        _loginCts?.Cancel();
        _loginCts = new CancellationTokenSource();

        try
        {
            IsLoginWaiting = true;
            var authUrl = _googleRadar.GetAuthUrl();
            var code = await _googleRadar.ReceiveCodeAsync(authUrl, _loginCts.Token);
            if (string.IsNullOrEmpty(code)) return;
            
            var tokens = await _googleRadar.ExchangeCodeAsync(code);
            var userInfo = await _googleRadar.GetUserInfoAsync(tokens.access_token);
            
            var account = new CloudAccount
            {
                email = userInfo.email,
                name = userInfo.name,
                avatar_url = userInfo.picture,
                refresh_token = tokens.refresh_token ?? "",
                access_token = tokens.access_token,
                provider = "google",
                IsAnonymous = IsAnonymousMode
            };

            await _googleRadar.UpdateAccountDataAsync(account);
            _accountManager.AddOrUpdateAccount(account);
            UpdateDisplayIndices();
            NotifyStatsChanged();
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("Google login cancelled by user.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Login Error: {ex.Message}");
        }
        finally
        {
            IsLoginWaiting = false;
        }
    }

    private async Task PromptAddCodexAccount()
    {
        string oauthOption = LocalizationManager.Text("login.openai_oauth");
        string webSessionOption = LocalizationManager.Text("login.openai_web_session");
        var action = await DisplayActionSheetAsync(
            LocalizationManager.Text("login.add_codex_title"),
            LocalizationManager.Text("common.cancel"),
            null,
            oauthOption,
            webSessionOption);

        if (action == oauthOption)
        {
            await LoginViaOpenAIFlow();
        }
        else if (action == webSessionOption)
        {
            StartCodexWebViewLogin(clearSession: true);
        }
    }

    private async Task PromptAddCursorAccount()
    {
        try
        {
            Trace.WriteLine("[Cursor] Add Current Account clicked.");
            RefreshCursorDbState();
            if (!IsCursorDbAvailable)
            {
                Trace.WriteLine("[Cursor] Add aborted: DB unavailable.");
                await DisplayAlertAsync(
                    LocalizationManager.Text("errors.cursor_not_detected_title"),
                    LocalizationManager.Text("errors.cursor_not_ready"),
                    LocalizationManager.Text("common.ok"));
                return;
            }

            IsCursorCredentialLoginWaiting = true;
            var session = await _cursorDbTokenService.TryReadCurrentSessionAsync();
            Trace.WriteLine($"[Cursor] Session read result: success={session.Success}, db={session.DbPath}, msg={session.Message}");
            if (!session.Success || string.IsNullOrWhiteSpace(session.SessionToken))
                throw new Exception(LocalizationManager.Format("errors.cursor_session_unavailable", session.Message));

            var account = new CursorAccount
            {
                name = GetNextCursorDefaultName(),
                email = string.Empty,
                session_token = session.SessionToken
            };

            await UpdateCursorAccountData(account);
            _cursorAccountManager.AddOrUpdateAccount(account);
            NotifyStatsChanged();
            Trace.WriteLine($"[Cursor] Account added/updated. email={account.email}, id={account.id}");
            await DisplayAlertAsync(
                LocalizationManager.Text("messages.cursor_account_added_title"),
                LocalizationManager.Text("messages.cursor_account_added"),
                LocalizationManager.Text("common.ok"));
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Cursor] Add failed: {ex}");
            await DisplayAlertAsync(
                LocalizationManager.Text("errors.cursor_login_failed_title"),
                LocalizationManager.Format("errors.cursor_login_failed_detail", ex.Message),
                LocalizationManager.Text("common.ok"));
        }
        finally
        {
            IsCursorCredentialLoginWaiting = false;
        }
    }

    private void RefreshCursorDbState()
    {
        var path = _cursorDbTokenService.FindCursorDbPath();
        _isCursorDbAvailable = !string.IsNullOrWhiteSpace(path);
        _cursorDbPath = path ?? LocalizationManager.Text("cursor.path_not_found");
        _cursorDbStatusMessage = LocalizationManager.Text(_isCursorDbAvailable
            ? "cursor.detected"
            : "cursor.check_installation");

        OnPropertyChanged(nameof(IsCursorDbAvailable));
        OnPropertyChanged(nameof(IsCursorDbMissing));
        OnPropertyChanged(nameof(CursorDbPath));
        OnPropertyChanged(nameof(CursorDbStatusMessage));
        OnPropertyChanged(nameof(CanAddAccount));
        OnPropertyChanged(nameof(AddAccountButtonText));
        Trace.WriteLine($"[Cursor] DB state refreshed. available={_isCursorDbAvailable}, path={_cursorDbPath}");
    }

    internal async void OnLanguageSelectionChanged(object? sender, EventArgs e)
    {
        if (sender is not Picker picker || picker.SelectedIndex is < 0 or > 2)
            return;

        string languageCode = picker.SelectedIndex switch
        {
            1 => "en",
            2 => "ko",
            _ => "system"
        };

        if (LocalizationManager.NormalizePreference(NotificationSettings.LanguageCode) == languageCode)
            return;

        NotificationSettings.LanguageCode = languageCode;
        OnPropertyChanged(nameof(LanguageSelectionIndex));
        await DisplayAlertAsync(
            LocalizationManager.Text("settings.language_restart_title"),
            LocalizationManager.Text("settings.language_restart_detail"),
            LocalizationManager.Text("common.ok"));
    }

    internal async void OnOpenCursorStorageFolderClicked(object? sender, EventArgs e)
    {
        try
        {
#if WINDOWS
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = _cursorAccountManager.StorageDirectory,
                UseShellExecute = true
            });
#else
            await Launcher.Default.OpenAsync(new OpenFileRequest
            {
                File = new ReadOnlyFile(_cursorAccountManager.StorageDirectory)
            });
#endif
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(
                LocalizationManager.Text("errors.open_folder_failed_title"),
                ex.Message,
                LocalizationManager.Text("common.ok"));
        }
    }

    internal async void OnOpenCursorHomeClicked(object? sender, EventArgs e)
    {
        await Launcher.Default.OpenAsync("https://cursor.com/");
    }

    private string GetNextCursorDefaultName()
    {
        var index = 1;
        var existing = CursorAccounts
            .Select(a => a.name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        while (existing.Contains($"user-{index}"))
            index++;

        return $"user-{index}";
    }

    private async void StartCodexWebViewLogin(bool clearSession = true)
    {
        IsCodexLoginOverlayVisible = true;
        _isProcessingCodexLogin = false;

#if WINDOWS
        if (clearSession)
        {
            await ClearCodexWebViewSessionAsync();
        }
#endif

        CodexLoginWebView.Source = "https://chatgpt.com/auth/login";
    }

#if WINDOWS
    private async Task ClearCodexWebViewSessionAsync()
    {
        try
        {
            CodexLoginWebView.Source = "about:blank";

            Microsoft.UI.Xaml.Controls.WebView2? webView2 = null;
            for (var attempt = 0; attempt < 10; attempt++)
            {
                webView2 = CodexLoginWebView.Handler?.PlatformView as Microsoft.UI.Xaml.Controls.WebView2;
                if (webView2 != null)
                    break;

                await Task.Delay(100);
            }

            if (webView2 == null)
            {
                Debug.WriteLine("[WebView2] Cannot clear cookies/cache because the WebView handler is not ready.");
                return;
            }

            if (webView2.CoreWebView2 == null)
            {
                await webView2.EnsureCoreWebView2Async();
            }

            var coreWebView = webView2.CoreWebView2;
            if (coreWebView == null)
            {
                Debug.WriteLine("[WebView2] Cannot clear cookies/cache because CoreWebView2 is not initialized.");
                return;
            }

            coreWebView.CookieManager.DeleteAllCookies();
            await coreWebView.Profile.ClearBrowsingDataAsync();
            Debug.WriteLine("[WebView2] Cleared Codex login cookies/cache before web session login.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebView2] Failed to clear cookies/cache: {ex.Message}");
        }
    }
#endif

    private void OnCancelCodexLoginClicked(object? sender, EventArgs e)
    {
        Debug.WriteLine("[CodexWebView] User clicked Cancel button.");
        _isProcessingCodexLogin = false;
        IsCodexLoginOverlayVisible = false;
        CodexLoginWebView.Source = "about:blank";
    }

    private async void OnCodexLoginWebViewNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (!IsCodexLoginOverlayVisible || _isProcessingCodexLogin)
            return;

        Debug.WriteLine($"[CodexWebView] Navigated to URL: {e.Url}");

        if (e.Url.StartsWith("https://chatgpt.com", StringComparison.OrdinalIgnoreCase))
        {
            _isProcessingCodexLogin = true;
            try
            {
                Debug.WriteLine("[CodexWebView] chatgpt.com detected, waiting 1500ms for auth state to settle...");
                // Give the page a moment to load
                await Task.Delay(1500);

                if (sender is Microsoft.Maui.Controls.WebView webView)
                {
                    Debug.WriteLine("[CodexWebView] Executing JS to fetch /api/auth/session...");
                    string js = "(() => { try { var xhr = new XMLHttpRequest(); xhr.withCredentials = true; xhr.open('GET', 'https://chatgpt.com/api/auth/session', false); xhr.send(); return xhr.responseText; } catch(e) { return 'ERROR:' + e.message; } })()";
                    string result = await webView.EvaluateJavaScriptAsync(js);
                    
                    Debug.WriteLine($"[CodexWebView] JS Execution Result Length: {result?.Length ?? 0}");
                    if (!string.IsNullOrEmpty(result) && result.Length < 200)
                    {
                        Debug.WriteLine($"[CodexWebView] JS Result snippet: {result}");
                    }

                    if (!string.IsNullOrEmpty(result) && result != "null" && result.Length > 10 && !result.StartsWith("ERROR:"))
                    {
                        var unescaped = System.Text.RegularExpressions.Regex.Unescape(result.Trim('"'));
                        var jsonDoc = JsonDocument.Parse(unescaped);
                        if (jsonDoc.RootElement.TryGetProperty("accessToken", out var tokenEl))
                        {
                            var token = tokenEl.GetString();
                            if (!string.IsNullOrEmpty(token))
                            {
                                Debug.WriteLine("[CodexWebView] SUCCESS! Access token captured.");
                                IsCodexLoginOverlayVisible = false;
                                webView.Source = "about:blank"; // Reset
                                
                                string? userName = null;
                                string? userEmail = null;
                                
                                if (jsonDoc.RootElement.TryGetProperty("user", out var userEl))
                                {
                                    if (userEl.TryGetProperty("name", out var nameEl)) userName = nameEl.GetString();
                                    if (userEl.TryGetProperty("email", out var emailEl)) userEmail = emailEl.GetString();
                                }
                                
                                await ProcessCodexTokenAsync(token, userName, userEmail);
                            }
                            else
                            {
                                Debug.WriteLine("[CodexWebView] accessToken property found, but value is empty.");
                            }
                        }
                        else
                        {
                            Debug.WriteLine("[CodexWebView] accessToken property not found in JSON.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CodexWebView] Token Extraction Error: {ex.Message}");
            }
            finally
            {
                if (IsCodexLoginOverlayVisible)
                {
                    _isProcessingCodexLogin = false;
                }
            }
        }
    }

    private static string GetCodexTokenStorageKey(CodexAccount account) =>
        string.IsNullOrWhiteSpace(account.account_id) ? account.id : account.account_id;


    private async Task LoginViaOpenAIFlow()
    {
        _loginCts?.Cancel();
        _loginCts = new CancellationTokenSource();

        try
        {
            IsLoginWaiting = true;
            var authUrl = _openAIAuth.GetAuthUrl();
            var code = await _openAIAuth.ReceiveCodeAsync(authUrl, _loginCts.Token);
            if (string.IsNullOrEmpty(code))
                return;

            var tokens = await _openAIAuth.ExchangeCodeForTokensAsync(code);
            var codexAccount = new CodexAccount
            {
                name = "OpenAI Account",
                access_token = tokens.access_token,
                refresh_token = tokens.refresh_token,
                login_method = "openai"
            };

            try
            {
                var jwtInfo = CodexApiService.ParseIdToken(tokens.id_token);
                if (jwtInfo != null)
                {
                    if (!string.IsNullOrWhiteSpace(jwtInfo.Name))
                        codexAccount.name = jwtInfo.Name;
                    if (!string.IsNullOrWhiteSpace(jwtInfo.Email))
                        codexAccount.email = jwtInfo.Email;
                }
            }
            catch
            {
            }

            if (codexAccount.name == "OpenAI Account" || string.IsNullOrWhiteSpace(codexAccount.email))
            {
                try
                {
                    var userInfo = await _codexApi.FetchUserInfoAsync(tokens.access_token);
                    if (userInfo != null)
                    {
                        if (!string.IsNullOrWhiteSpace(userInfo.Name))
                            codexAccount.name = userInfo.Name;
                        if (!string.IsNullOrWhiteSpace(userInfo.Email))
                            codexAccount.email = userInfo.Email;
                    }
                }
                catch
                {
                }
            }

            AdoptExistingCodexAccountIdentity(codexAccount);
            var accountKey = GetCodexTokenStorageKey(codexAccount);
            await _tokenStorage.SaveTokensAsync(accountKey, tokens.access_token, tokens.refresh_token, tokens.expires_in);
            await ScheduleTokenRefreshAsync(codexAccount);
            await UpdateCodexAccountData(codexAccount);

            _codexAccountManager.AddOrUpdateAccount(codexAccount);
            UpdateCodexDisplayIndices();
            await DisplayAlertAsync(
                LocalizationManager.Text("messages.openai_account_added_title"),
                LocalizationManager.Text("messages.openai_account_added"),
                LocalizationManager.Text("common.ok"));
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("OpenAI login cancelled by user.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OpenAI Login Error: {ex.Message}");
            await DisplayAlertAsync(
                LocalizationManager.Text("errors.login_failed_title"),
                ex.Message,
                LocalizationManager.Text("common.ok"));
        }
        finally
        {
            IsLoginWaiting = false;
        }
    }

    // Refresh token if needed (called from scheduler or after 401)
    private async Task RefreshTokenIfNeededAsync(CodexAccount account, bool silent = false)
    {
        var accountKey = GetCodexTokenStorageKey(account);
        var (_, refresh, expiresAt) = await _tokenStorage.LoadTokensAsync(accountKey);
        if (string.IsNullOrEmpty(refresh))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                account.HasError = true;
                account.LastErrorMessage = "Authentication Expired (No Refresh Token). Please sign in again.";
            });
            return;
        }

        var now = DateTime.UtcNow;
        // Refresh if expiration is missing, already passed, or within 5 min window
        if (!expiresAt.HasValue || expiresAt.Value <= now.AddMinutes(5))
        {
            try
            {
                var refreshed = await _openAIAuth.RefreshTokenAsync(refresh);
                // Save new tokens + new expiration
                await _tokenStorage.SaveTokensAsync(accountKey,
                    refreshed.access_token,
                    refreshed.refresh_token,
                    refreshed.expires_in);
                account.access_token = refreshed.access_token;
                account.refresh_token = refreshed.refresh_token;
                // Reschedule next refresh based on new expiration
                await ScheduleTokenRefreshAsync(account);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TokenRefresh] Failed for {accountKey}: {ex.Message}");
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    account.HasError = true;
                    account.LastErrorMessage = $"Authentication Expired. Refresh failed: {ex.Message}";
                });
            }
        }
    }

    // Schedule a token‑refresh task for the given account (5 min before expiry)
    private async Task ScheduleTokenRefreshAsync(CodexAccount account)
    {
        var accountKey = GetCodexTokenStorageKey(account);
        var (_, _, expiresAt) = await _tokenStorage.LoadTokensAsync(accountKey);
        if (!expiresAt.HasValue) return; // nothing to schedule

        var now = DateTime.UtcNow;
        var runAt = expiresAt.Value.AddMinutes(-5);
        var delay = runAt > now ? runAt - now : TimeSpan.Zero;

        // Cancel previous schedule if any
        CancellationTokenSource? oldCts = null;
        lock (_refreshSchedulersLock)
        {
            if (_refreshSchedulers.TryGetValue(accountKey, out var existingCts))
            {
                oldCts = existingCts;
            }

            var cts = new CancellationTokenSource();
            _refreshSchedulers[accountKey] = cts;
            oldCts?.Cancel();
            oldCts?.Dispose();

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay, cts.Token);
                    if (!cts.IsCancellationRequested)
                        await RefreshTokenIfNeededAsync(account, silent: true);
                }
                catch (TaskCanceledException)
                {
                }
            });
        }
    }

    private async Task ProcessCodexTokenAsync(string token, string? userName, string? userEmail)
    {
        try
        {
            var codexAccount = new CodexAccount
            {
                access_token = token,
                refresh_token = string.Empty,
                login_method = "openai",
                name = string.IsNullOrWhiteSpace(userName) ? "OpenAI Account" : userName,
                email = userEmail ?? string.Empty,
                account_id = string.Empty
            };

            if (codexAccount.name == "OpenAI Account" || string.IsNullOrWhiteSpace(codexAccount.email))
            {
                try
                {
                    var apiUserInfo = await _codexApi.FetchUserInfoAsync(token);
                    if (apiUserInfo != null)
                    {
                        if (!string.IsNullOrWhiteSpace(apiUserInfo.Name))
                            codexAccount.name = apiUserInfo.Name;
                        if (!string.IsNullOrWhiteSpace(apiUserInfo.Email))
                            codexAccount.email = apiUserInfo.Email;
                    }
                }
                catch
                {
                }
            }

            if (codexAccount.name == "OpenAI Account" && !string.IsNullOrWhiteSpace(codexAccount.email))
            {
                var prefix = codexAccount.email.Split('@')[0];
                if (prefix.Length > 0)
                {
                    codexAccount.name = char.ToUpper(prefix[0]) + prefix.Substring(1);
                }
            }

            AdoptExistingCodexAccountIdentity(codexAccount);
            await _tokenStorage.SaveTokensAsync(codexAccount.id, token, null);

            try
            {
                await UpdateCodexAccountData(codexAccount);
            }
            catch (Exception ex) when (ex.Message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
            {
                Log.Info("Access token likely expired, attempting refresh...");
                var (_, savedRefresh, _) = await _tokenStorage.LoadTokensAsync(codexAccount.id);
                if (!string.IsNullOrEmpty(savedRefresh))
                {
                    var refreshed = await _openAIAuth.RefreshTokenAsync(savedRefresh);
                    codexAccount.access_token = refreshed.access_token;
                    codexAccount.refresh_token = refreshed.refresh_token;
                    await _tokenStorage.SaveTokensAsync(codexAccount.id, refreshed.access_token, refreshed.refresh_token, refreshed.expires_in);
                    await UpdateCodexAccountData(codexAccount);
                }
                else
                {
                    Log.Info("No refresh token available, prompting re-login.");
                    await DisplayAlertAsync(
                        LocalizationManager.Text("errors.session_expired_title"),
                        LocalizationManager.Text("errors.session_expired_detail"),
                        LocalizationManager.Text("common.ok"));
                    StartCodexWebViewLogin(clearSession: true);
                    return;
                }
            }

            _codexAccountManager.AddOrUpdateAccount(codexAccount);
            UpdateCodexDisplayIndices();
            await DisplayAlertAsync(
                LocalizationManager.Text("messages.openai_account_added_title"),
                LocalizationManager.Text("messages.openai_account_added"),
                LocalizationManager.Text("common.ok"));
        }
        catch (Exception ex)
        {
            _isProcessingCodexLogin = false;
            Log.Error("Codex Token Process Error", ex);
            await DisplayAlertAsync(
                LocalizationManager.Text("errors.login_failed_title"),
                LocalizationManager.Format("errors.codex_token_failed", ex.Message),
                LocalizationManager.Text("common.ok"));
        }
    }

    private void OnCancelLoginClicked(object? sender, EventArgs e)
    {
        _loginCts?.Cancel();
        IsLoginWaiting = false;
    }

    private void AdoptExistingCodexAccountIdentity(CodexAccount account)
    {
        var existing = _codexAccountManager.FindMatchingAccount(account);
        if (existing is null)
            return;

        account.id = existing.id;

        if (string.IsNullOrWhiteSpace(account.account_id))
            account.account_id = existing.account_id;
        if (string.IsNullOrWhiteSpace(account.refresh_token))
            account.refresh_token = existing.refresh_token;
    }

    private async void OnRefreshAllClicked(object? sender, EventArgs e)
    {
        await RefreshCurrentTabAsync();
    }

    private async Task RefreshCurrentTabAsync()
    {
        if (CurrentTab == "Codex")
            await RefreshAllCodexAccounts(RefreshQueueLevel.Background);
        else if (CurrentTab == "Cursor")
            await RefreshAllCursorAccounts(RefreshQueueLevel.Background);
        else
            await RefreshAllAccounts(RefreshQueueLevel.Background);
    }

#if WINDOWS
    private void AttachRefreshKeyboardShortcut()
    {
        var platformWindow = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (platformWindow is null || ReferenceEquals(_platformWindow, platformWindow))
            return;

        if (_platformWindow?.Content is UIElement previousRoot)
        {
            previousRoot.KeyDown -= OnWindowKeyDown;
        }

        _platformWindow = platformWindow;

        if (_platformWindow.Content is UIElement rootElement)
        {
            rootElement.KeyDown -= OnWindowKeyDown;
            rootElement.KeyDown += OnWindowKeyDown;
        }
    }

    private async void OnWindowKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.F5)
            return;

        e.Handled = true;
        await RefreshCurrentTabAsync();
    }
#endif

    // NEW: Token refresh semaphore (single token refresh at a time)
    private readonly SemaphoreSlim _tokenRefreshSemaphore = new SemaphoreSlim(1, 1);
    // Scheduler Cts per account to allow cancellation/rescheduling
    private readonly Dictionary<string, CancellationTokenSource> _refreshSchedulers = new();

    private RefreshOperation? CreateRefreshOperation(object account, RefreshQueueLevel queueLevel)
    {
        return account switch
        {
            CloudAccount google => new RefreshOperation(
                () => google.IsRefreshing,
                () => google.IsRefreshQueued,
                value => google.IsRefreshQueued = value,
                value => google.IsRefreshing = value,
                () => google.HasError = false,
                error =>
                {
                    google.HasError = true;
                    google.LastErrorMessage = error.Message;
                },
                () => _googleRadar.UpdateAccountDataAsync(google),
                RetryCount: 3,
                TimeoutSeconds: 15,
                DelayForAnimation: true),
            CodexAccount codex => new RefreshOperation(
                () => codex.IsRefreshing,
                () => codex.IsRefreshQueued,
                value => codex.IsRefreshQueued = value,
                value => codex.IsRefreshing = value,
                () => codex.HasError = false,
                error =>
                {
                    codex.HasError = true;
                    codex.LastErrorMessage = error.Message;
                },
                () => UpdateCodexAccountData(codex),
                RetryCount: 3,
                TimeoutSeconds: 15,
                DelayForAnimation: true,
                RecoverAsync: async error =>
                {
                    if (!error.Message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
                        return false;

                    await _tokenRefreshSemaphore.WaitAsync();
                    try
                    {
                        await RefreshTokenIfNeededAsync(codex, silent: queueLevel == RefreshQueueLevel.Background);
                    }
                    finally
                    {
                        _tokenRefreshSemaphore.Release();
                    }

                    return true;
                }),
            CursorAccount cursor => new RefreshOperation(
                () => cursor.IsRefreshing,
                () => cursor.IsRefreshQueued,
                value => cursor.IsRefreshQueued = value,
                value => cursor.IsRefreshing = value,
                () => cursor.HasError = false,
                error =>
                {
                    cursor.HasError = true;
                    cursor.LastErrorMessage = error.Message;
                },
                () => UpdateCursorAccountData(cursor),
                RetryCount: 2,
                TimeoutSeconds: 10,
                DelayForAnimation: false),
            _ => null
        };
    }

    private async Task EnqueueRefreshAsync(object account, RefreshQueueLevel queueLevel)
    {
        var operation = CreateRefreshOperation(account, queueLevel);
        if (operation is null || operation.IsRefreshing() || operation.IsQueued())
            return;

        operation.SetQueued(true);
        operation.ClearError();

        if (queueLevel == RefreshQueueLevel.Background)
        {
            await Task.Delay(100);
        }

        using var refreshLease = await _refreshConcurrencyLimiter.AcquireAsync();
        try
        {
            if (_isMinimizedToTray)
                return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                operation.SetQueued(false);
                operation.SetRefreshing(true);
            });

            try
            {
                if (operation.DelayForAnimation)
                    await Task.Delay(500);

                await RetryAsync(operation.RefreshAsync, operation.RetryCount, operation.TimeoutSeconds);
            }
            catch (Exception ex)
            {
                var recovered = operation.RecoverAsync is not null && await operation.RecoverAsync(ex);
                if (!recovered)
                    MainThread.BeginInvokeOnMainThread(() => operation.SetError(ex));
            }
            finally
            {
                MainThread.BeginInvokeOnMainThread(() => operation.SetRefreshing(false));
            }
        }
        finally
        {
            MainThread.BeginInvokeOnMainThread(() => operation.SetQueued(false));
        }
    }

    private async Task RefreshAllAccounts(RefreshQueueLevel queueLevel = RefreshQueueLevel.Background)
    {
        if (Accounts.Count == 0) return;
        try
        {
            var uniqueAccounts = Accounts.GroupBy(a => a.email.ToLower()).Select(g => g.First()).ToList();
            if (uniqueAccounts.Count != Accounts.Count)
            {
                Accounts.Clear();
                foreach (var acc in uniqueAccounts) Accounts.Add(acc);
            }

            var tasks = Accounts.Select(acc => EnqueueRefreshAsync(acc, queueLevel)).ToList();
            await Task.WhenAll(tasks);
            _accountManager.SortAccounts();
            _ = _accountManager.SaveAccountsAsync();
            UpdateDisplayIndices();
            NotifyStatsChanged();
        }
        catch (Exception ex)
        {
            Log.Error("Refresh error", ex);
        }
    }

    private async Task RefreshAllCodexAccounts(RefreshQueueLevel queueLevel = RefreshQueueLevel.Background)
    {
        if (CodexAccounts.Count == 0) return;
        try
        {
            var tasks = CodexAccounts.Select(acc => EnqueueRefreshAsync(acc, queueLevel)).ToList();
            await Task.WhenAll(tasks);

            _ = _codexAccountManager.SaveAccountsAsync();
            UpdateCodexDisplayIndices();
        }
        catch (Exception ex)
        {
            Log.Error("Codex refresh error", ex);
        }
    }

    private async Task RefreshAllCursorAccounts(RefreshQueueLevel queueLevel = RefreshQueueLevel.Background)
    {
        if (CursorAccounts.Count == 0) return;
        try
        {
            var tasks = CursorAccounts.Select(acc => EnqueueRefreshAsync(acc, queueLevel)).ToList();
            await Task.WhenAll(tasks);

            _ = _cursorAccountManager.SaveAccountsAsync();
            NotifyStatsChanged();
        }
        catch (Exception ex)
        {
            Log.Error("Cursor refresh error", ex);
        }
    }

    private void StartBackgroundRefreshLoop()
    {
        _backgroundRefreshCts?.Cancel();
        _backgroundRefreshCts = new CancellationTokenSource();
        var token = _backgroundRefreshCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var interval = NotificationSettings.RefreshIntervalMinutes;
                    if (interval < 1) interval = 1;

                    await Task.Delay(TimeSpan.FromMinutes(interval), token);

                    if (!_isShuttingDown && NotificationSettings.EnableBackgroundRefresh && !_isMinimizedToTray)
                    {
                        Log.Info($"[BackgroundRefresh] Starting loose refresh cycle...");
                        await PerformBackgroundRefreshAsync(token);
                        Log.Info($"[BackgroundRefresh] Loose refresh cycle completed.");
                    }
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error("Background refresh loop error", ex);
                    await Task.Delay(TimeSpan.FromMinutes(1), token); // Wait before retry
                }
            }
        }, token);
    }

    private async Task PerformBackgroundRefreshAsync(CancellationToken token)
    {
        if (_isShuttingDown)
            return;

        // Collect accounts that are healthy and not rate limited
        var googleToRefresh = Accounts.Where(a => !a.HasError && a.quotas.Any(q => q.percentage > 0)).ToList();
        var codexToRefresh = CodexAccounts.Where(a => !a.HasError && !a.IsRateLimited).ToList();
        var cursorToRefresh = CursorAccounts.Where(a => !a.HasError && a.context_usage_percent < 99.5).ToList();

        var allToRefresh = new List<object>();
        allToRefresh.AddRange(googleToRefresh);
        allToRefresh.AddRange(codexToRefresh);
        allToRefresh.AddRange(cursorToRefresh);

        // Randomize order a bit to spread the load
        var rnd = new Random();
        allToRefresh = allToRefresh.OrderBy(_ => rnd.Next()).ToList();

        var batchSize = NotificationSettings.MaxConcurrentRefreshes;
        foreach (var batch in allToRefresh.Chunk(batchSize))
        {
            if (token.IsCancellationRequested) break;
            if (_isShuttingDown) break;

            await Task.WhenAll(batch.Select(account => EnqueueRefreshAsync(account, RefreshQueueLevel.Background)));
            
            // Pause between batches to spread background requests over time.
            await Task.Delay(TimeSpan.FromSeconds(rnd.Next(5, 11)), token);
        }

        // Save after a full cycle
        if (!_isShuttingDown)
        {
            _ = _accountManager.SaveAccountsAsync();
            _ = _codexAccountManager.SaveAccountsAsync();
            _ = _cursorAccountManager.SaveAccountsAsync();
        }
    }

    private async Task RetryAsync(Func<Task> action, int maxRetries, int timeoutSeconds)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource();
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                var task = action();
                var completedTask = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, timeoutCts.Token));

                if (completedTask == task)
                {
                    timeoutCts.Cancel(); // Clean up the Delay task
                    await task; // Propagate exceptions
                    return;     // Success
                }

                throw new TimeoutException($"Attempt {attempt}/{maxRetries} timed out.");
            }
            catch (Exception ex)
            {
                Log.Info($"[Retry] Attempt {attempt}/{maxRetries} failed: {ex.Message}");
                if (attempt == maxRetries) throw;
                await Task.Delay(1500); // 1.5-second pause before retry
            }
        }
    }



    private async Task UpdateCodexAccountData(CodexAccount account)
    {
        try
        {
            var usage = await _codexApi.FetchUsageAsync(account.access_token, 
                string.IsNullOrEmpty(account.account_id) ? null : account.account_id);

            if (usage != null)
            {
                // Global validation check: Ensure the response isn't completely empty
                if (usage.RateLimit?.PrimaryWindow == null && usage.RateLimit?.SecondaryWindow == null && usage.Credits == null)
                {
                    throw new Exception("Received suspiciously invalid usage data (all elements are null).");
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (!string.IsNullOrEmpty(usage.PlanType))
                        account.plan_type = usage.PlanType;
                    if (!string.IsNullOrWhiteSpace(usage.AccountId))
                        account.account_id = usage.AccountId;

                    // Normalize windows: API may swap primary/secondary depending on plan
                    // session = 300 min (5h), weekly = 10080 min (7d)
                    var rawPrimary = usage.RateLimit?.PrimaryWindow;
                    var rawSecondary = usage.RateLimit?.SecondaryWindow;
                    var (sessionWindow, weeklyWindow) = NormalizeWindows(rawPrimary, rawSecondary);

                    // Session window → main usage gauge (or weekly if no session)
                    if (sessionWindow != null)
                    {
                        account.primaryUsedPercent = sessionWindow.UsedPercent;
                        account.primaryWindowLabel = CodexApiService.FormatWindowName(sessionWindow.LimitWindowSeconds);
                        account.primaryResetDescription = CodexApiService.FormatResetTime(sessionWindow.ResetAt);
                        account.PrimaryResetAt = sessionWindow.ResetAt;
                    }
                    else if (weeklyWindow != null)
                    {
                        // Free accounts: only weekly window exists → show as primary gauge
                        account.primaryUsedPercent = weeklyWindow.UsedPercent;
                        account.primaryWindowLabel = CodexApiService.FormatWindowName(weeklyWindow.LimitWindowSeconds);
                        account.primaryResetDescription = CodexApiService.FormatResetTime(weeklyWindow.ResetAt);
                        account.PrimaryResetAt = weeklyWindow.ResetAt;
                        weeklyWindow = null; // Already shown as primary
                    }

                    // Weekly window → reset date
                    if (weeklyWindow != null)
                    {
                        account.secondaryUsedPercent = weeklyWindow.UsedPercent;
                        account.secondaryResetDescription = CodexApiService.FormatResetTime(weeklyWindow.ResetAt);

                        var secondaryText = CodexApiService.FormatWindowName(weeklyWindow.LimitWindowSeconds);
                        account.secondaryWindowLabel = secondaryText;
                    }
                    else
                    {
                        account.secondaryWindowLabel = string.Empty;
                        account.secondaryResetDescription = string.Empty;
                    }

                    if (usage.Credits != null)
                    {
                        account.has_credits = usage.Credits.HasCredits;
                        account.unlimited_credits = usage.Credits.Unlimited;
                        var balance = usage.Credits.GetBalance();
                        if (balance.HasValue) account.credits = balance.Value;
                    }

                    if (usage.Promo != null && !string.IsNullOrWhiteSpace(usage.Promo.Message))
                    {
                        account.PromoMessage = usage.Promo.Message;
                    }
                    else
                    {
                        account.PromoMessage = string.Empty;
                    }

                    // 3. Handle Trial Expiration (Detect if access is revoked via Promo Message)
                    account.PromoMessage = usage.Promo?.Message ?? "";
                    if (!string.IsNullOrEmpty(account.PromoMessage) && 
                        (account.PromoMessage.Contains("To continue using Codex", StringComparison.OrdinalIgnoreCase) ||
                         account.PromoMessage.Contains("start a free trial of Plus", StringComparison.OrdinalIgnoreCase)))
                    {
                        account.IsTrialExpired = true;
                        // Force usage to 100% if trial is expired
                        account.primaryUsedPercent = 100;
                    }
                    else
                    {
                        account.IsTrialExpired = false;
                    }

                    account.last_updated = DateTime.Now;
                    var resetDate = (usage.RateLimit?.PrimaryWindow?.ResetAt ?? 0);
                    var resetDateStr = resetDate > 0 ? CodexApiService.FormatResetDate(resetDate) : "—";
                    Log.Info($"Update Completed: {account.name} | Usage: {account.primaryUsedPercent}% ({account.primaryWindowLabel}) | Reset: {account.primaryResetDescription} ({resetDateStr}) | [Current Local Time: {DateTime.Now:MM/dd HH:mm:ss}]");
                    
                    _codexAccountManager.SortAccounts();
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Codex update error for {account.name}", ex);
            throw;
        }
    }

    private async Task UpdateCursorAccountData(CursorAccount account)
    {
        Log.Info($"[Cursor] Update start. name={account.name}, email={account.email}, hasSession={!string.IsNullOrWhiteSpace(account.session_token)}, hasLoginId={!string.IsNullOrWhiteSpace(account.login_email)}");
        (string? Email, int Used, int Limit, DateTime? ResetDate) result;
        (double Percent, string ComposerName)? contextUsage = null;

        if (!string.IsNullOrWhiteSpace(account.login_email) &&
            !string.IsNullOrWhiteSpace(account.login_password))
        {
            var credentialResult = await _cursorApi.FetchMonthlyUsageWithCredentialsAsync(account.login_email, account.login_password);
            result = (credentialResult.Email, credentialResult.Used, credentialResult.Limit, credentialResult.ResetDate);
            if (!string.IsNullOrWhiteSpace(credentialResult.SessionToken))
                account.session_token = credentialResult.SessionToken!;
        }
        else
        {
            if (account.session_token.Contains("%3A%3A", StringComparison.OrdinalIgnoreCase))
            {
                contextUsage = await _cursorDbTokenService.TryReadContextUsageAsync();
            }

            result = await _cursorApi.FetchMonthlyUsageAsync(account.session_token);
        }

        Log.Info($"[Cursor] Update fetched. apiEmail={(result.Email ?? "N/A")}, used={result.Used}, limit={result.Limit}");

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!string.IsNullOrWhiteSpace(result.Email))
            {
                account.email = result.Email!;
            }
            else if (string.IsNullOrWhiteSpace(account.email))
            {
                var userId = ExtractCursorUserId(account.session_token);
                if (!string.IsNullOrWhiteSpace(userId))
                {
                    account.email = userId;
                }
            }
            account.monthly_used = result.Used;
            account.monthly_limit = result.Limit <= 0 ? 500 : result.Limit;
            ApplyCursorContextUsage(account, contextUsage, result.ResetDate);
            account.last_updated = DateTime.Now;
            _cursorAccountManager.SortAccounts();
        });
    }

    private static void ApplyCursorContextUsage(
        CursorAccount account,
        (double Percent, string ComposerName)? contextUsage,
        DateTime? resetDate)
    {
        var now = DateTime.UtcNow;
        var existingResetUtc = account.context_reset_date?.ToUniversalTime();
        var incomingResetUtc = resetDate?.ToUniversalTime();

        if (existingResetUtc.HasValue && now >= existingResetUtc.Value)
        {
            account.context_usage_percent = 0;
            account.context_composer_name = string.Empty;
            account.context_bottom_hit = false;
        }

        if (incomingResetUtc.HasValue)
        {
            account.context_reset_date = incomingResetUtc.Value;
        }

        if (contextUsage is null)
            return;

        var sameResetWindow =
            existingResetUtc.HasValue &&
            incomingResetUtc.HasValue &&
            Math.Abs((existingResetUtc.Value - incomingResetUtc.Value).TotalMinutes) < 1;

        var incomingPercent = Math.Clamp(contextUsage.Value.Percent, 0, 100);
        if (!sameResetWindow || incomingPercent >= account.context_usage_percent)
        {
            account.context_usage_percent = incomingPercent;
            account.context_composer_name = contextUsage.Value.ComposerName;
        }

        if (account.context_usage_percent >= 99.5)
        {
            account.context_bottom_hit = true;
        }
    }

    private static string? ExtractCursorUserId(string sessionToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
            return null;
        var sep = sessionToken.IndexOf("%3A%3A", StringComparison.OrdinalIgnoreCase);
        if (sep <= 0)
            return null;
        return sessionToken[..sep];
    }

    // Normalize API windows: session (300min/5h) should be primary, weekly (10080min/7d) should be secondary
    // The API may return them swapped depending on plan type (free vs plus)
    private static (CodexApiService.WindowSnapshot? session, CodexApiService.WindowSnapshot? weekly) NormalizeWindows(
        CodexApiService.WindowSnapshot? primary, CodexApiService.WindowSnapshot? secondary)
    {
        const int SessionMinutes = 300;    // 5 hours in minutes (18000 seconds)
        const int WeeklyMinutes = 10080;   // 7 days in minutes (604800 seconds)

        static bool IsSession(CodexApiService.WindowSnapshot w) => w.LimitWindowSeconds / 60 <= SessionMinutes;
        static bool IsWeekly(CodexApiService.WindowSnapshot w) => w.LimitWindowSeconds / 60 >= WeeklyMinutes;

        if (primary != null && secondary != null)
        {
            // Both present: ensure session comes first
            if (IsWeekly(primary) && IsSession(secondary))
                return (secondary, primary); // Swap
            return (primary, secondary);     // Already correct
        }

        if (primary != null)
        {
            // Only one window: classify it
            if (IsWeekly(primary)) return (null, primary);
            return (primary, null);
        }

        if (secondary != null)
        {
            if (IsSession(secondary)) return (secondary, null);
            return (null, secondary);
        }

        return (null, null);
    }

    internal async void OnRefreshCodexAccountClicked(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is CodexAccount account)
        {
            Log.Info($"Refresh Clicked: {account.name} ({account.email})");
            IsBusy = true;
            await EnqueueRefreshAsync(account, RefreshQueueLevel.Immediate);
            _ = _codexAccountManager.SaveAccountsAsync();
            IsBusy = false;
        }
    }

    internal async void OnErrorIconTapped(object? sender, TappedEventArgs e)
    {
        var account = e.Parameter;
        var provider = account switch
        {
            CloudAccount => "Google",
            CodexAccount => "Codex",
            CursorAccount => "Cursor",
            _ => string.Empty
        };
        var msg = account switch
        {
            CloudAccount cloud => cloud.LastErrorMessage,
            CodexAccount codex => codex.LastErrorMessage,
            CursorAccount cursor => cursor.LastErrorMessage,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(msg) || string.IsNullOrEmpty(provider))
            return;

        var title = LocalizationManager.Text("errors.update_failed_title");
        var description = msg;
        var msgLower = msg.ToLowerInvariant();
        var staleCache = msgLower.Contains("invalid or zeroed quota data") || msgLower.Contains("all elements are null");
        var authenticationExpired = !staleCache && (
            msgLower.Contains("invalid_grant") ||
            msgLower.Contains("authorization") ||
            msgLower.Contains("unauthorized") ||
            msgLower.Contains("authentication expired") ||
            msgLower.Contains("token expired") ||
            msgLower.Contains("session expired") ||
            msgLower.Contains("expired") ||
            msgLower.Contains("check session token") ||
            msgLower.Contains("no refresh token") ||
            msgLower.Contains("re-login"));

        if (staleCache)
        {
            title = LocalizationManager.Text("errors.stale_cache_title");
            description = LocalizationManager.Format("errors.stale_cache_detail", msg);
        }
        else if (authenticationExpired)
        {
            title = LocalizationManager.Text("errors.authentication_expired_title");
            description = LocalizationManager.Format("errors.authentication_expired_detail", provider, msg);
        }
        else if (msgLower.Contains("api error"))
        {
            title = LocalizationManager.Text("errors.api_server_title");
            description = LocalizationManager.Format("errors.api_server_detail", msg);
        }

        if (authenticationExpired)
        {
            var shouldReauthenticate = await DisplayAlertAsync(
                title,
                $"{description}\n\n{LocalizationManager.Text("errors.reauthenticate_prompt")}",
                LocalizationManager.Text("common.reauthenticate"),
                LocalizationManager.Text("common.close"));

            if (shouldReauthenticate && account is not null)
                await ReauthenticateAccountAsync(account);
            return;
        }

        await DisplayAlertAsync(title, description, LocalizationManager.Text("common.ok"));
    }

    private async Task ReauthenticateAccountAsync(object account)
    {
        switch (account)
        {
            case CloudAccount:
                await LoginWithGoogleAsync();
                break;
            case CodexAccount:
                await PromptAddCodexAccount();
                break;
            case CursorAccount:
                await PromptAddCursorAccount();
                break;
        }
    }

    internal async void OnRefreshAccountClicked(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is CloudAccount account)
        {
            IsBusy = true;
            await EnqueueRefreshAsync(account, RefreshQueueLevel.Immediate);
            _accountManager.SortAccounts();
            _ = _accountManager.SaveAccountsAsync();
            NotifyStatsChanged();
            IsBusy = false;
        }
        else if (e.Parameter is CursorAccount cursorAccount)
        {
            IsBusy = true;
            await EnqueueRefreshAsync(cursorAccount, RefreshQueueLevel.Immediate);
            _cursorAccountManager.SortAccounts();
            _ = _cursorAccountManager.SaveAccountsAsync();
            NotifyStatsChanged();
            IsBusy = false;
        }
        else if (e.Parameter is CodexAccount codexAccount)
        {
            IsBusy = true;
            await EnqueueRefreshAsync(codexAccount, RefreshQueueLevel.Immediate);
            _ = _codexAccountManager.SaveAccountsAsync();
            NotifyStatsChanged();
            IsBusy = false;
        }
    }

    // Export/Import Logic
    private async void OnExportClicked(object? sender, EventArgs e)
    {
        try
        {
            var fileName = CurrentTab == "Google"
                ? "AIUsageMonitor_accounts.json"
                : CurrentTab == "Cursor"
                ? "AIUsageMonitor_cursor_accounts.json"
                : "AIUsageMonitor_codex_accounts.json";
            string json = "";
            if (CurrentTab == "Google")
                json = JsonFileUtility.Serialize(Accounts.ToList());
            else if (CurrentTab == "Cursor")
                json = JsonFileUtility.Serialize(CursorAccounts.ToList());
            else
                json = JsonFileUtility.Serialize(CodexAccounts.ToList());

            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            var fileSaverResult = await CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync(fileName, stream);
            
            if (fileSaverResult.IsSuccessful)
            {
                await DisplayAlertAsync(
                    LocalizationManager.Text("messages.export_success_title"),
                    LocalizationManager.Format("messages.export_saved", fileSaverResult.FilePath),
                    LocalizationManager.Text("common.ok"));
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(
                LocalizationManager.Text("errors.export_failed_title"),
                ex.Message,
                LocalizationManager.Text("common.ok"));
        }
    }

    private async void OnImportClicked(object? sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = LocalizationManager.Text("import.picker_title"),
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".json" } }
                })
            });

            if (result != null)
            {
                if (CurrentTab == "Google")
                {
                    await _accountManager.ImportAccountsAsync(result.FullPath);
                    UpdateDisplayIndices();
                    NotifyStatsChanged();
                }
                else if (CurrentTab == "Cursor")
                {
                    var list = await JsonFileUtility.ReadAsync<List<CursorAccount>>(result.FullPath);
                    if (list != null)
                    {
                        foreach (var acc in list)
                        {
                            _cursorAccountManager.AddOrUpdateAccount(acc);
                        }
                    }
                    NotifyStatsChanged();
                }
                else
                {
                    await _codexAccountManager.ImportAccountsAsync(result.FullPath);
                }
                await DisplayAlertAsync(
                    LocalizationManager.Text("messages.import_success_title"),
                    LocalizationManager.Text("messages.accounts_imported"),
                    LocalizationManager.Text("common.ok"));
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(
                LocalizationManager.Text("errors.import_failed_title"),
                ex.Message,
                LocalizationManager.Text("common.ok"));
        }
    }

    private CancellationTokenSource? _digestDebounceCts;

    private void NotifyStatsChanged()
    {
        OnPropertyChanged(nameof(GlobalActions));
        OnPropertyChanged(nameof(GlobalActive));
        OnPropertyChanged(nameof(GlobalLimited));
        OnPropertyChanged(nameof(GlobalQuota));

        // Debounced Slack digest — wait 10s after last update to batch
        _digestDebounceCts?.Cancel();
        _digestDebounceCts = new CancellationTokenSource();
        var token = _digestDebounceCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(10_000, token);
                await CollectAndSendDigestAsync();
            }
            catch (TaskCanceledException) { }
        }, token);
    }

    private async Task CollectAndSendDigestAsync()
    {
        var entries = new List<AccountDigestEntry>();

        // Codex accounts
        foreach (var acc in CodexAccounts)
        {
            entries.Add(new AccountDigestEntry(
                Provider: "Codex",
                AccountName: acc.email ?? "Unknown",
                UsagePercent: 100 - acc.PrimaryRemainingPercent,
                IsAvailable: !acc.IsRateLimited && !acc.IsTrialExpired,
                ResetAt: acc.PrimaryResetAt
            ));
        }

        // Cursor accounts
        foreach (var acc in CursorAccounts)
        {
            long cursorResetAt = acc.context_reset_date.HasValue
                ? new DateTimeOffset(acc.context_reset_date.Value).ToUnixTimeSeconds()
                : 0;
            entries.Add(new AccountDigestEntry(
                Provider: "Cursor",
                AccountName: acc.email ?? "Unknown",
                UsagePercent: acc.MonthlyUsedPercent,
                IsAvailable: acc.context_usage_percent < 99.5,
                ResetAt: cursorResetAt
            ));
        }

        // Google accounts
        foreach (var quota in GetVisibleGoogleQuotasSnapshot())
        {
            entries.Add(new AccountDigestEntry(
                Provider: "Google",
                AccountName: quota.display_name ?? "Unknown",
                UsagePercent: 100 - quota.percentage,
                IsAvailable: quota.percentage > 0,
                ResetAt: 0
            ));
        }

        await _notificationService.SendAccountDigestAsync(entries);
        await _notificationService.ScheduleResetAlertsAsync(entries);
    }

    private void UpdateDisplayIndices()
    {
        for (int i = 0; i < Accounts.Count; i++)
        {
            Accounts[i].DisplayIndex = i;
            Accounts[i].IsAnonymous = IsAnonymousMode;
        }
    }

    private void UpdateCodexDisplayIndices()
    {
        for (int i = 0; i < CodexAccounts.Count; i++)
        {
            CodexAccounts[i].DisplayIndex = i;
            CodexAccounts[i].IsAnonymous = IsAnonymousMode;
        }
    }

    // Removed UpdateAccountData as it is now in GoogleRadarService

    private async Task RemoveAccountWithConfirmationAsync<TAccount>(
        string accountId,
        IEnumerable<TAccount> accounts,
        Func<TAccount, string> getId,
        Func<TAccount, string> getDisplayName,
        Func<TAccount, string> getDisplayEmail,
        string provider,
        Func<string, Task> removeAccount,
        Action? afterRemove = null)
    {
        if (_isRemovingAccount)
            return;

        _isRemovingAccount = true;
        try
        {
            var account = accounts.FirstOrDefault(item => getId(item) == accountId);
            if (account is null)
                return;

            var displayName = getDisplayName(account);
            var displayEmail = getDisplayEmail(account);
            var accountLabel = string.IsNullOrWhiteSpace(displayEmail)
                ? displayName
                : $"{displayName}\n{displayEmail}";

            if (!await DisplayAlertAsync(
                    LocalizationManager.Text("account.remove.confirm_title"),
                    LocalizationManager.Format("account.remove.confirm_message", provider, accountLabel),
                    LocalizationManager.Text("common.yes"),
                    LocalizationManager.Text("common.no")))
                return;

            await removeAccount(accountId);
            afterRemove?.Invoke();
            NotifyStatsChanged();
        }
        finally
        {
            _isRemovingAccount = false;
        }
    }

    internal async void OnDeleteAccountClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string id)
        {
            await RemoveAccountWithConfirmationAsync(
                id,
                Accounts,
                account => account.id,
                account => account.DisplayName,
                account => account.DisplayEmail,
                "Google",
                _accountManager.RemoveAccountAsync,
                () =>
                {
                    UpdateDisplayIndices();
                });
        }
    }

    internal async void OnDeleteCodexAccountClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string id)
        {
            await RemoveAccountWithConfirmationAsync(
                id,
                CodexAccounts,
                account => account.id,
                account => account.DisplayName,
                account => account.DisplayEmail,
                "Codex",
                _codexAccountManager.RemoveAccountAsync,
                UpdateCodexDisplayIndices);
        }
    }

    internal async void OnDeleteCursorAccountClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string id)
        {
            await RemoveAccountWithConfirmationAsync(
                id,
                CursorAccounts,
                account => account.id,
                account => account.DisplayName,
                account => account.DisplayEmail,
                "Cursor",
                _cursorAccountManager.RemoveAccountAsync);
        }
    }

    internal async void OnRenameCursorAccountClicked(object? sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not string id)
            return;

        var account = CursorAccounts.FirstOrDefault(a => a.id == id);
        if (account is null)
            return;

        var newName = await DisplayPromptAsync(
            LocalizationManager.Text("account.rename.title"),
            LocalizationManager.Text("account.rename.prompt"),
            LocalizationManager.Text("account.rename.save"),
            LocalizationManager.Text("common.cancel"),
            account.DisplayName,
            maxLength: 80);

        if (string.IsNullOrWhiteSpace(newName))
            return;

        account.name = newName.Trim();
        _cursorAccountManager.AddOrUpdateAccount(account);
        NotifyStatsChanged();
    }

    internal async void OnOpenCodexSiteClicked(object? sender, EventArgs e)
    {
        await Microsoft.Maui.ApplicationModel.Launcher.OpenAsync("https://openai.com/codex/");
    }

    internal async void OnOpenGitHubSourceClicked(object? sender, EventArgs e)
    {
        await Microsoft.Maui.ApplicationModel.Launcher.OpenAsync("https://github.com/wenovaX/AIUsageMonitor");
    }

    internal async void OnOpenGitHubReleasesClicked(object? sender, EventArgs e)
    {
        await Microsoft.Maui.ApplicationModel.Launcher.OpenAsync("https://github.com/wenovaX/AIUsageMonitor/releases");
    }
    internal async void OnSlackTestClicked(object? sender, EventArgs e)
    {
        await _notificationService.SendTestAsync();
    }

    internal async void OnOpenSlackApiClicked(object? sender, EventArgs e)
    {
        try
        {
            await Launcher.Default.OpenAsync(new Uri("https://api.slack.com/apps"));
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to open Slack API page: {ex.Message}");
        }
    }

    internal async void OnScheduledTestClicked(object? sender, EventArgs e)
    {
        await _notificationService.SendScheduledTestAsync();
    }
}

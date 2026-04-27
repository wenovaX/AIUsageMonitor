using AIUsageMonitor.Models;
using AIUsageMonitor.PlatformAbstractions;
using AIUsageMonitor.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Text.Json;
using System.Windows.Input;
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

    private readonly ModelCatalogService _modelCatalogService;
    private readonly GoogleRadarService _googleRadar;
    private readonly AccountManagerService _accountManager = new();
    private readonly CodexAccountManagerService _codexAccountManager = new();
    private readonly CodexOAuthService _codexOAuth = new();
    private readonly OpenAIAuthService _openAIAuth = new();
    private readonly CodexApiService _codexApi = new();
    private readonly PlatformManager _platformManager;
    private const double GoogleCardWidth = 240;
    private const double GoogleCardSpacing = 5;
    private const double GoogleLayoutHorizontalPadding = 80;
    private const int GoogleLayoutDebounceMilliseconds = 120;

    public IReadOnlyList<DiscoveredModelEntry> GeminiCatalogModels =>
        _modelCatalogService.Models.Where(model => model.IsGemini).ToList();

    public IReadOnlyList<DiscoveredModelEntry> OtherCatalogModels =>
        _modelCatalogService.Models.Where(model => !model.IsGemini).ToList();

    public ObservableCollection<GoogleAccountRow> GoogleAccountRows { get; } = new();

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
            OnPropertyChanged(nameof(IsCodexTab));
            OnPropertyChanged(nameof(IsSettingsTab));
            OnPropertyChanged(nameof(IsAboutTab));
            NotifyStatsChanged();
        }
    }

    public bool IsGoogleTab => CurrentTab == "Google";
    public bool IsCodexTab => CurrentTab == "Codex";
    public bool IsSettingsTab => CurrentTab == "Settings";
    public bool IsAboutTab => CurrentTab == "About";

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

    private readonly SemaphoreSlim _globalRefreshSemaphore;
    private readonly SemaphoreSlim _googleRefreshSemaphore;
    private readonly SemaphoreSlim _codexRefreshSemaphore;
    private readonly int _maxGlobalRefreshConcurrency;
    private readonly int _maxGoogleRefreshConcurrency;
    private readonly int _maxCodexRefreshConcurrency;
#if WINDOWS
    private Microsoft.UI.Xaml.Window? _platformWindow;
#endif
    private CancellationTokenSource? _googleLayoutCts;
    private int _lastGoogleCardsPerRow;
    private string _lastGoogleLayoutSignature = string.Empty;
    private readonly Dictionary<string, int> _googleAccountRowIndexById = new();
    private readonly Dictionary<int, CancellationTokenSource> _googleRowRefreshCts = new();
    private bool _isBulkUpdatingGoogleLayout;
    private bool _hasPendingModelCatalogUiRefresh;

    private bool _isLoginWaiting;
    public bool IsLoginWaiting
    {
        get => _isLoginWaiting;
        set { _isLoginWaiting = value; OnPropertyChanged(); }
    }

    public const string AppVersion = "1.0.4";
    public string DisplayVersion => $"v{AppVersion}";

    private CancellationTokenSource? _loginCts;

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
        : GetVisibleGoogleQuotasSnapshot().Count;
    public int GlobalActive => CurrentTab == "Codex"
        ? CodexAccounts.Count(a => !a.IsRateLimited)
        : GetVisibleGoogleQuotasSnapshot().Count(q => q.percentage > 0);
    public int GlobalLimited => CurrentTab == "Codex"
        ? CodexAccounts.Count(a => a.IsRateLimited)
        : GetVisibleGoogleQuotasSnapshot().Count(q => q.percentage == 0);
    public int GlobalQuota => CurrentTab == "Codex"
        ? (CodexAccounts.Count == 0 ? 0 : (int)CodexAccounts.Average(a => a.PrimaryRemainingPercent))
        : GetVisibleGoogleQuotaAverage();

    public ObservableCollection<CloudAccount> Accounts => _accountManager.Accounts;
    public ObservableCollection<CodexAccount> CodexAccounts => _codexAccountManager.Accounts;

    private bool _isMinimizedToTray = false;

    public MainPage()
    {
        _platformManager = MauiProgram.Services.GetRequiredService<PlatformManager>();
        _modelCatalogService = MauiProgram.Services.GetRequiredService<ModelCatalogService>();
        (_maxGlobalRefreshConcurrency, _maxGoogleRefreshConcurrency, _maxCodexRefreshConcurrency) = CalculateRefreshConcurrency();
        _globalRefreshSemaphore = new SemaphoreSlim(_maxGlobalRefreshConcurrency, _maxGlobalRefreshConcurrency);
        _googleRefreshSemaphore = new SemaphoreSlim(_maxGoogleRefreshConcurrency, _maxGoogleRefreshConcurrency);
        _codexRefreshSemaphore = new SemaphoreSlim(_maxCodexRefreshConcurrency, _maxCodexRefreshConcurrency);
        Debug.WriteLine($"Refresh queue initialized. Global={_maxGlobalRefreshConcurrency}, Google={_maxGoogleRefreshConcurrency}, Codex={_maxCodexRefreshConcurrency}");

        _googleRadar = new GoogleRadarService();
        InitializeComponent();
        BindingContext = this;
        HandlerChanged += OnHandlerChanged;
        Unloaded += OnUnloaded;
        SizeChanged += OnPageSizeChanged;
        
        ShowAppCommand = new Command(() => OnShowAppFromTray(null, EventArgs.Empty));
        ExitAppCommand = new Command(() => OnExitAppFromTray(null, EventArgs.Empty));
        DoubleClickTrayIconCommand = new Command(() => OnShowAppFromTray(null, EventArgs.Empty));

        Accounts.CollectionChanged += OnAccountsCollectionChanged;
        _modelCatalogService.CatalogChanged += OnModelCatalogChanged;
        UpdateDisplayIndices();
        foreach (var account in Accounts)
        {
            account.PropertyChanged += OnGoogleAccountPropertyChanged;
        }
        RebuildGoogleAccountRows();

        _ = RefreshAllAccounts(RefreshQueueLevel.Background);
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

    private static (int Global, int Google, int Codex) CalculateRefreshConcurrency()
    {
        var processorCount = Environment.ProcessorCount;
        var global = processorCount >= 8 ? 3 : 2;
        var google = Math.Min(2, global);
        var codex = global;

        return (global, google, codex);
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
        SizeChanged -= OnPageSizeChanged;
        _googleLayoutCts?.Cancel();
        _googleLayoutCts?.Dispose();
        _googleLayoutCts = null;
        foreach (var cts in _googleRowRefreshCts.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _googleRowRefreshCts.Clear();
        Accounts.CollectionChanged -= OnAccountsCollectionChanged;
        _modelCatalogService.CatalogChanged -= OnModelCatalogChanged;
        foreach (var account in Accounts)
        {
            account.PropertyChanged -= OnGoogleAccountPropertyChanged;
        }
        _platformManager.Current.WindowVisibilityChanged -= OnPlatformWindowVisibilityChanged;
        _platformManager.Current.WindowResizeCompleted -= OnPlatformWindowResizeCompleted;
        _googleAccountRowIndexById.Clear();
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
        _platformManager.Current.WindowVisibilityChanged -= OnPlatformWindowVisibilityChanged;
        _platformManager.Current.WindowVisibilityChanged += OnPlatformWindowVisibilityChanged;
        _platformManager.Current.WindowResizeCompleted -= OnPlatformWindowResizeCompleted;
        _platformManager.Current.WindowResizeCompleted += OnPlatformWindowResizeCompleted;
        _platformManager.Current.ConfigureTrayIcon(TrayIcon, ShowAppCommand, ExitAppCommand, DoubleClickTrayIconCommand);
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        if (_platformManager.Current.IsWindowResizeInProgress)
            return;

        ScheduleGoogleAccountRowRefresh();
    }

    private void OnAccountsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (CloudAccount account in e.OldItems)
            {
                account.PropertyChanged -= OnGoogleAccountPropertyChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (CloudAccount account in e.NewItems)
            {
                account.PropertyChanged -= OnGoogleAccountPropertyChanged;
                account.PropertyChanged += OnGoogleAccountPropertyChanged;
            }
        }

        RebuildGoogleAccountRows();
    }

    private void ScheduleGoogleAccountRowRefresh(bool force = false)
    {
        var signature = BuildGoogleLayoutSignature();
        if (!force && signature == _lastGoogleLayoutSignature)
            return;

        _googleLayoutCts?.Cancel();
        _googleLayoutCts?.Dispose();
        _googleLayoutCts = new CancellationTokenSource();
        var token = _googleLayoutCts.Token;

        _ = MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                await Task.Delay(GoogleLayoutDebounceMilliseconds, token);
                if (token.IsCancellationRequested)
                    return;

                var refreshedSignature = BuildGoogleLayoutSignature();
                if (!force && refreshedSignature == _lastGoogleLayoutSignature)
                    return;

                RebuildGoogleAccountRows();
            }
            catch (TaskCanceledException)
            {
            }
        });
    }

    private void OnGoogleAccountPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isBulkUpdatingGoogleLayout)
            return;

        if (sender is not CloudAccount account)
            return;

        if (e.PropertyName is not nameof(CloudAccount.quotas)
            and not nameof(CloudAccount.HiddenModels)
            and not nameof(CloudAccount.GeminiQuotas)
            and not nameof(CloudAccount.OtherQuotas)
            and not nameof(CloudAccount.HasGeminiQuotas)
            and not nameof(CloudAccount.HasOtherQuotas))
        {
            return;
        }

        RefreshGoogleAccountRow(account);
    }

    private void RebuildGoogleAccountRows()
    {
        foreach (var cts in _googleRowRefreshCts.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _googleRowRefreshCts.Clear();

        GoogleAccountRows.Clear();
        _googleAccountRowIndexById.Clear();

        _lastGoogleCardsPerRow = CalculateGoogleCardsPerRow();
        _lastGoogleLayoutSignature = BuildGoogleLayoutSignature();

        if (Accounts.Count == 0)
            return;

        var cardsPerRow = _lastGoogleCardsPerRow;

        for (int i = 0; i < Accounts.Count; i += cardsPerRow)
        {
            var rowAccounts = Accounts.Skip(i).Take(cardsPerRow).ToList();
            var rowHeight = rowAccounts.Max(EstimateGoogleCardHeight);
            var row = new GoogleAccountRow();
            var rowIndex = GoogleAccountRows.Count;

            foreach (var account in rowAccounts)
            {
                account.CardHeightHint = rowHeight;
                row.Accounts.Add(account);
                _googleAccountRowIndexById[account.id] = rowIndex;
            }

            GoogleAccountRows.Add(row);
        }
    }

    private void RefreshGoogleAccountRow(CloudAccount account)
    {
        if (!_googleAccountRowIndexById.TryGetValue(account.id, out var rowIndex)
            || rowIndex < 0
            || rowIndex >= GoogleAccountRows.Count)
        {
            RebuildGoogleAccountRows();
            return;
        }

        ScheduleGoogleAccountRowHeightRefresh(rowIndex);
    }

    private void ScheduleGoogleAccountRowHeightRefresh(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= GoogleAccountRows.Count)
            return;

        if (_googleRowRefreshCts.TryGetValue(rowIndex, out var existingCts))
        {
            existingCts.Cancel();
            existingCts.Dispose();
        }

        var cts = new CancellationTokenSource();
        _googleRowRefreshCts[rowIndex] = cts;
        var token = cts.Token;

        _ = MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                await Task.Delay(GoogleLayoutDebounceMilliseconds, token);
                if (token.IsCancellationRequested)
                    return;

                RefreshGoogleAccountRowCore(rowIndex);
            }
            catch (TaskCanceledException)
            {
            }
            finally
            {
                if (_googleRowRefreshCts.TryGetValue(rowIndex, out var current) && current == cts)
                {
                    _googleRowRefreshCts.Remove(rowIndex);
                }
                cts.Dispose();
            }
        });
    }

    private void RefreshGoogleAccountRowCore(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= GoogleAccountRows.Count)
            return;

        var row = GoogleAccountRows[rowIndex];
        if (row.Accounts.Count == 0)
            return;

        var newRowHeight = row.Accounts.Max(EstimateGoogleCardHeight);
        var currentHeight = row.Accounts[0].CardHeightHint;

        if (Math.Abs(currentHeight - newRowHeight) < 0.5)
            return;

        foreach (var rowAccount in row.Accounts)
        {
            rowAccount.CardHeightHint = newRowHeight;
        }

        _lastGoogleLayoutSignature = BuildGoogleLayoutSignature();
    }

    private void OnPlatformWindowResizeCompleted(object? sender, EventArgs e)
    {
        RebuildGoogleAccountRows();
    }

    private void OnModelCatalogChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (IsSettingsTab)
            {
                _hasPendingModelCatalogUiRefresh = true;
                OnPropertyChanged(nameof(GeminiCatalogModels));
                OnPropertyChanged(nameof(OtherCatalogModels));
                return;
            }

            RefreshModelCatalogUi();
        });
    }

    private void RefreshModelCatalogUi()
    {
        OnPropertyChanged(nameof(GeminiCatalogModels));
        OnPropertyChanged(nameof(OtherCatalogModels));

        _isBulkUpdatingGoogleLayout = true;
        try
        {
            foreach (var account in Accounts)
            {
                account.NotifyQuotaChanges();
            }
        }
        finally
        {
            _isBulkUpdatingGoogleLayout = false;
        }

        RebuildGoogleAccountRows();
        NotifyStatsChanged();
    }

    private async void OnUpdateModelListClicked(object? sender, EventArgs e)
    {
        var hasRegisteredAntigravityAccount = Accounts.Any(account =>
            !string.IsNullOrWhiteSpace(account.refresh_token) ||
            !string.IsNullOrWhiteSpace(account.access_token));

        if (!hasRegisteredAntigravityAccount)
        {
            await DisplayAlertAsync("Antigravity Models", "유효한 Antigravity 계정을 하나 이상 등록하고 다시 눌러주세요.", "OK");
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
            await DisplayAlertAsync("Antigravity Models", "Antigravity 정보를 아직 불러오지 못했습니다. 계정을 하나 이상 등록하고 새로고침한 뒤 다시 눌러주세요.", "OK");
            return;
        }

        var updated = _modelCatalogService.UpdateAvailableModels(discoveredModels);
        if (!updated)
        {
            OnPropertyChanged(nameof(GeminiCatalogModels));
            OnPropertyChanged(nameof(OtherCatalogModels));
        }
    }

    private void OnSetModelListToDefaultClicked(object? sender, EventArgs e)
    {
        _modelCatalogService.ResetToDefaults();
        OnPropertyChanged(nameof(GeminiCatalogModels));
        OnPropertyChanged(nameof(OtherCatalogModels));
    }

    private string BuildGoogleLayoutSignature()
    {
        var cardsPerRow = CalculateGoogleCardsPerRow();
        var heightSignature = string.Join(",", Accounts.Select(a => EstimateGoogleCardHeight(a).ToString("F0")));
        return $"{cardsPerRow}|{heightSignature}";
    }

    private int CalculateGoogleCardsPerRow()
    {
        var availableWidth = Width <= 0
            ? GoogleCardWidth
            : Math.Max(GoogleCardWidth, Width - GoogleLayoutHorizontalPadding);

        return Math.Max(1, (int)Math.Floor((availableWidth + GoogleCardSpacing) / (GoogleCardWidth + GoogleCardSpacing)));
    }

    private static double EstimateGoogleCardHeight(CloudAccount account)
    {
        var height = 136d;

        if (account.HasGeminiQuotas)
        {
            height += 16;
            height += account.GeminiQuotas.Count() * 28;
        }

        if (account.HasOtherQuotas)
        {
            height += 16;
            height += account.OtherQuotas.Count() * 28;
        }

        return Math.Max(210, height);
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
            _isMinimizedToTray = !_platformManager.Current.IsWindowVisible;

            if (!_isMinimizedToTray)
            {
                _ = RefreshAllAccounts(RefreshQueueLevel.Background);
                _ = RefreshAllCodexAccounts(RefreshQueueLevel.Background);
            }
        });
    }

    private void OnTabClicked(object? sender, EventArgs e)
    {
        if (sender is Border border)
        {
            var tab = border.AutomationId;
            if (!string.IsNullOrEmpty(tab)) CurrentTab = tab;
        }
    }


    private void OnAnonymousToggleClicked(object? sender, EventArgs e)
    {
        IsAnonymousMode = !IsAnonymousMode;
    }

    private void OnToggleSettingsTapped(object? sender, TappedEventArgs e)
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
        if (CurrentTab == "Codex")
        {
            await PromptAddCodexAccount();
            return;
        }

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
            RebuildGoogleAccountRows();
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
        var action = await DisplayActionSheetAsync("Add Codex Account", "Cancel", null,
            "Login with OpenAI",
            "Login with GitHub (Copilot)",
            "Enter Token Manually");

        if (action == "Enter Token Manually")
        {
            string token = await DisplayPromptAsync("Add Codex Account", "Enter your Codex Token (JWT or OAuth):", "Add", "Cancel");
            if (!string.IsNullOrWhiteSpace(token))
            {
                var codexAccount = new CodexAccount
                {
                    name = "Manual Token Account",
                    access_token = token.Trim(),
                    login_method = "manual"
                };
                _codexAccountManager.AddOrUpdateAccount(codexAccount);
            }
        }
        else if (action == "Login with OpenAI")
        {
            await LoginViaOpenAIFlow();
        }
        else if (action == "Login with GitHub (Copilot)")
        {
            await LoginViaGitHubFlow();
        }
    }

    private async Task LoginViaOpenAIFlow()
    {
        _loginCts?.Cancel();
        _loginCts = new CancellationTokenSource();

        try
        {
            IsLoginWaiting = true;
            var authUrl = _openAIAuth.GetAuthUrl();
            var code = await _openAIAuth.ReceiveCodeAsync(authUrl, _loginCts.Token);

            if (string.IsNullOrEmpty(code)) return;

            var tokens = await _openAIAuth.ExchangeCodeForTokensAsync(code);

            var codexAccount = new CodexAccount
            {
                name = "OpenAI Account",
                access_token = tokens.access_token,
                refresh_token = tokens.refresh_token,
                login_method = "openai"
            };

            // Extract user info from JWT id_token (most reliable)
            try
            {
                var jwtInfo = CodexApiService.ParseIdToken(tokens.id_token);
                if (jwtInfo != null)
                {
                    if (!string.IsNullOrEmpty(jwtInfo.Name)) codexAccount.name = jwtInfo.Name;
                    if (!string.IsNullOrEmpty(jwtInfo.Email)) codexAccount.email = jwtInfo.Email;
                }
            }
            catch { /* Non-critical */ }

            // Fallback: try /me API if JWT didn't give us a name
            if (codexAccount.name == "OpenAI Account")
            {
                try
                {
                    var userInfo = await _codexApi.FetchUserInfoAsync(tokens.access_token);
                    if (userInfo != null)
                    {
                        if (!string.IsNullOrEmpty(userInfo.Name)) codexAccount.name = userInfo.Name;
                        if (!string.IsNullOrEmpty(userInfo.Email)) codexAccount.email = userInfo.Email;
                    }
                }
                catch { /* Non-critical */ }
            }

            // Try to fetch initial usage
            await UpdateCodexAccountData(codexAccount);

            _codexAccountManager.AddOrUpdateAccount(codexAccount);
            UpdateCodexDisplayIndices();
            await DisplayAlertAsync("Success", "OpenAI account successfully authorized and added.", "OK");
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("OpenAI login cancelled by user.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OpenAI Login Error: {ex.Message}");
            await DisplayAlertAsync("Login Failed", ex.Message, "OK");
        }
        finally
        {
            IsLoginWaiting = false;
        }
    }

    private async Task LoginViaGitHubFlow()
    {
        _loginCts?.Cancel();
        _loginCts = new CancellationTokenSource();

        try
        {
            IsLoginWaiting = true;
            var deviceCodeResponse = await _codexOAuth.RequestDeviceCodeAsync();
            if (deviceCodeResponse == null) throw new Exception("Failed to get device code.");

            // Copy to clipboard
            await Clipboard.Default.SetTextAsync(deviceCodeResponse.UserCode);

            // Notify user
            await DisplayAlertAsync("GitHub Login", 
                $"Your login code is: {deviceCodeResponse.UserCode}\n\nIt has been copied to your clipboard. The browser will now open. Please paste the code to authorize.", 
                "Open Browser");

            // Open browser
            await Microsoft.Maui.ApplicationModel.Launcher.OpenAsync(deviceCodeResponse.VerificationUri);

            // Start polling
            using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(_loginCts.Token);
            pollCts.CancelAfter(TimeSpan.FromMinutes(10));
            var token = await _codexOAuth.PollForTokenAsync(deviceCodeResponse.DeviceCode, deviceCodeResponse.Interval, pollCts.Token);

            if (!string.IsNullOrEmpty(token))
            {
                var codexAccount = new CodexAccount
                {
                    name = "GitHub Copilot",
                    access_token = token,
                    login_method = "github"
                };

                // Fetch GitHub profile
                try
                {
                    var ghUser = await _codexApi.FetchGitHubUserInfoAsync(token);
                    if (ghUser != null)
                    {
                        if (!string.IsNullOrEmpty(ghUser.Name)) codexAccount.name = ghUser.Name;
                        if (!string.IsNullOrEmpty(ghUser.Email)) codexAccount.email = ghUser.Email;
                    }
                }
                catch { /* Non-critical */ }

                _codexAccountManager.AddOrUpdateAccount(codexAccount);
                UpdateCodexDisplayIndices();
                await DisplayAlertAsync("Success", "GitHub Copilot account successfully authorized and added.", "OK");
            }
        }
        catch (OperationCanceledException)
        {
            await DisplayAlertAsync("Cancelled", "Login timed out or was cancelled.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Login Failed", ex.Message, "OK");
        }
        finally
        {
            IsLoginWaiting = false;
        }
    }

    private void OnCancelLoginClicked(object? sender, EventArgs e)
    {
        _loginCts?.Cancel();
        IsLoginWaiting = false;
    }

    private async void OnRefreshAllClicked(object? sender, EventArgs e)
    {
        await RefreshCurrentTabAsync();
    }

    private async Task RefreshCurrentTabAsync()
    {
        if (CurrentTab == "Codex")
            await RefreshAllCodexAccounts(RefreshQueueLevel.Background);
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

    private async Task EnqueueRefreshAsync(object account, RefreshQueueLevel queueLevel = RefreshQueueLevel.Background)
    {
        if (account is CloudAccount g)
        {
            if (g.IsRefreshing || g.IsRefreshQueued) return;
            g.IsRefreshQueued = true;
            g.HasError = false;
        }
        else if (account is CodexAccount c)
        {
            if (c.IsRefreshing || c.IsRefreshQueued) return;
            c.IsRefreshQueued = true;
            c.HasError = false;
        }

        var providerSemaphore = account is CloudAccount
            ? _googleRefreshSemaphore
            : _codexRefreshSemaphore;

        if (queueLevel == RefreshQueueLevel.Background)
        {
            await Task.Delay(100);
        }

        await _globalRefreshSemaphore.WaitAsync();
        await providerSemaphore.WaitAsync();
        try
        {
            // Abort if minimized, but clear the queued state
            if (_isMinimizedToTray) return;

            if (account is CloudAccount googleAcc)
            {
                googleAcc.IsRefreshQueued = false;
                googleAcc.IsRefreshing = true;
                try
                {
                    await RetryAsync(async () => await _googleRadar.UpdateAccountDataAsync(googleAcc), 3, 15);
                }
                catch { googleAcc.HasError = true; }
                finally { googleAcc.IsRefreshing = false; }
            }
            else if (account is CodexAccount codexAcc)
            {
                codexAcc.IsRefreshQueued = false;
                codexAcc.IsRefreshing = true;
                try
                {
                    await RetryAsync(async () => await UpdateCodexAccountData(codexAcc), 3, 15);
                }
                catch { codexAcc.HasError = true; }
                finally { codexAcc.IsRefreshing = false; }
            }
        }
        finally
        {
            // Always ensure queued state is cleared
            if (account is CloudAccount g2) g2.IsRefreshQueued = false;
            if (account is CodexAccount c2) c2.IsRefreshQueued = false;
            providerSemaphore.Release();
            _globalRefreshSemaphore.Release();
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

            _accountManager.SaveAccounts();
            UpdateDisplayIndices();
            RebuildGoogleAccountRows();
            NotifyStatsChanged();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Refresh error: {ex.Message}");
        }
    }

    private async Task RefreshAllCodexAccounts(RefreshQueueLevel queueLevel = RefreshQueueLevel.Background)
    {
        if (CodexAccounts.Count == 0) return;
        try
        {
            var tasks = CodexAccounts.Select(acc => EnqueueRefreshAsync(acc, queueLevel)).ToList();
            await Task.WhenAll(tasks);

            _codexAccountManager.SaveAccounts();
            UpdateCodexDisplayIndices();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Codex refresh error: {ex.Message}");
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
                Debug.WriteLine($"[Retry] Attempt {attempt}/{maxRetries} failed: {ex.Message}");
                if (attempt == maxRetries) throw;
                await Task.Delay(1000); // Brief pause before retry
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
                if (!string.IsNullOrEmpty(usage.PlanType))
                    account.plan_type = usage.PlanType;

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
                }
                else if (weeklyWindow != null)
                {
                    // Free accounts: only weekly window exists → show as primary gauge
                    account.primaryUsedPercent = weeklyWindow.UsedPercent;
                    account.primaryWindowLabel = CodexApiService.FormatWindowName(weeklyWindow.LimitWindowSeconds);
                    account.primaryResetDescription = CodexApiService.FormatResetTime(weeklyWindow.ResetAt);
                    weeklyWindow = null; // Already shown as primary
                }

                // Weekly window → reset date
                if (weeklyWindow != null)
                {
                    account.secondaryUsedPercent = weeklyWindow.UsedPercent;
                    account.secondaryResetDate = CodexApiService.FormatResetDate(weeklyWindow.ResetAt);
                    account.secondaryResetDescription = CodexApiService.FormatResetTime(weeklyWindow.ResetAt);

                    var secondaryText = CodexApiService.FormatWindowName(weeklyWindow.LimitWindowSeconds);
                    account.secondaryWindowLabel = secondaryText;
                }
                else
                {
                    account.secondaryWindowLabel = string.Empty;
                    account.secondaryResetDate = string.Empty;
                    account.secondaryResetDescription = string.Empty;
                }

                if (usage.Credits != null)
                {
                    account.has_credits = usage.Credits.HasCredits;
                    account.unlimited_credits = usage.Credits.Unlimited;
                    var balance = usage.Credits.GetBalance();
                    if (balance.HasValue) account.credits = balance.Value;
                }

                account.last_updated = DateTime.Now;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Codex update error for {account.name}: {ex.Message}");
        }
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

    private async void OnRefreshCodexAccountClicked(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is CodexAccount account)
        {
            IsBusy = true;
            await EnqueueRefreshAsync(account, RefreshQueueLevel.Immediate);
            _codexAccountManager.SaveAccounts();
            IsBusy = false;
        }
    }

    private async void OnRefreshAccountClicked(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is CloudAccount account)
        {
            IsBusy = true;
            await EnqueueRefreshAsync(account, RefreshQueueLevel.Immediate);
            _accountManager.SaveAccounts();
            NotifyStatsChanged();
            IsBusy = false;
        }
    }

    private void OnHideModelClicked(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is ModelQuotaInfo modelInfo)
        {
            var account = Accounts.FirstOrDefault(a => a.quotas.Contains(modelInfo));
            if (account != null)
            {
                if (!account.HiddenModels.Contains(modelInfo.display_name))
                {
                    account.HiddenModels.Add(modelInfo.display_name);
                    account.NotifyQuotaChanges();
                    RefreshGoogleAccountRow(account);
                    _accountManager.SaveAccounts();
                    NotifyStatsChanged();
                }
            }
        }
    }

    // Export/Import Logic
    private async void OnExportClicked(object? sender, EventArgs e)
    {
        try
        {
            var fileName = CurrentTab == "Google" ? "AIUsageMonitor_accounts.json" : "AIUsageMonitor_codex_accounts.json";
            string json = "";
            if (CurrentTab == "Google")
                json = JsonSerializer.Serialize(Accounts.ToList());
            else
                json = JsonSerializer.Serialize(CodexAccounts.ToList());

            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            var fileSaverResult = await CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync(fileName, stream);
            
            if (fileSaverResult.IsSuccessful)
            {
                await DisplayAlertAsync("Export Success", $"File saved to: {fileSaverResult.FilePath}", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Export Failed", ex.Message, "OK");
        }
    }

    private async void OnImportClicked(object? sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select Accounts JSON",
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
                    RebuildGoogleAccountRows();
                    NotifyStatsChanged();
                }
                else
                {
                    await _codexAccountManager.ImportAccountsAsync(result.FullPath);
                }
                await DisplayAlertAsync("Import Success", "Accounts imported.", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Import Failed", ex.Message, "OK");
        }
    }

    private void NotifyStatsChanged()
    {
        OnPropertyChanged(nameof(GlobalActions));
        OnPropertyChanged(nameof(GlobalActive));
        OnPropertyChanged(nameof(GlobalLimited));
        OnPropertyChanged(nameof(GlobalQuota));
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

    private void OnDeleteAccountClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string id)
        {
            _accountManager.RemoveAccount(id);
            UpdateDisplayIndices();
            RebuildGoogleAccountRows();
            NotifyStatsChanged();
        }
    }

    private void OnDeleteCodexAccountClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string id)
        {
            _codexAccountManager.RemoveAccount(id);
        }
    }

    private async void OnOpenCodexSiteClicked(object? sender, EventArgs e)
    {
        await Microsoft.Maui.ApplicationModel.Launcher.OpenAsync("https://openai.com/codex/");
    }
}

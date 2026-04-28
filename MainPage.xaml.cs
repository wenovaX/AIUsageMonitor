using AIUsageMonitor.Models;
using AIUsageMonitor.PlatformAbstractions;
using AIUsageMonitor.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
    private const double GoogleCardWidth = 240;
    private const double GoogleCardSpacing = 5;
    private const double GoogleLayoutHorizontalPadding = 80;
    private const int GoogleLayoutDebounceMilliseconds = 120;

    public IReadOnlyList<DiscoveredModelEntry> GeminiCatalogModels =>
        _modelCatalogService.Models.Where(model => model.IsGemini).ToList();

    public IReadOnlyList<DiscoveredModelEntry> OtherCatalogModels =>
        _modelCatalogService.Models.Where(model => !model.IsGemini).ToList();

    public ObservableCollection<GoogleAccountRow> GoogleAccountRows { get; } = new();
    public ObservableCollection<CodexAccountRow> CodexAccountRows { get; } = new();

    private const double CodexCardWidth = 240;
    private const double CodexCardSpacing = 5;
    private const double CodexLayoutHorizontalPadding = 50; // Fine-tuned wrapping offset

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
            OnPropertyChanged(nameof(IsAboutTab));
            OnPropertyChanged(nameof(CanAddAccount));
            OnPropertyChanged(nameof(AddAccountButtonText));

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
    private readonly SemaphoreSlim _cursorRefreshSemaphore;
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
    private int _lastCodexCardsPerRow;

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

    public const string AppVersion = "1.0.6";
    public string DisplayVersion => $"v{AppVersion}";

    private CancellationTokenSource? _loginCts;
    private bool _isProcessingCodexLogin;
    private readonly object _refreshSchedulersLock = new();
    private bool _isCursorCredentialLoginWaiting;

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
    public string AddAccountButtonText => IsCursorTab ? "+ Add Current Account" : "+ Add Account";
    private bool _isCursorDbAvailable;
    private string _cursorDbPath = "Not found";
    private string _cursorDbStatusMessage = "Cursor 설치가 필요합니다.";

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
        (_maxGlobalRefreshConcurrency, _maxGoogleRefreshConcurrency, _maxCodexRefreshConcurrency) = CalculateRefreshConcurrency();
        _globalRefreshSemaphore = new SemaphoreSlim(_maxGlobalRefreshConcurrency, _maxGlobalRefreshConcurrency);
        _googleRefreshSemaphore = new SemaphoreSlim(_maxGoogleRefreshConcurrency, _maxGoogleRefreshConcurrency);
        _cursorRefreshSemaphore = new SemaphoreSlim(_maxCodexRefreshConcurrency, _maxCodexRefreshConcurrency);
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
        CodexAccounts.CollectionChanged += OnCodexAccountsCollectionChanged;
        CursorAccounts.CollectionChanged += OnCursorAccountsCollectionChanged;
        _modelCatalogService.CatalogChanged += OnModelCatalogChanged;
        UpdateDisplayIndices();
        foreach (var account in Accounts)
        {
            account.PropertyChanged += OnGoogleAccountPropertyChanged;
        }
        RebuildGoogleAccountRows();
        RebuildCodexAccountRows();

        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        await _accountManager.LoadAccountsAsync();
        await _codexAccountManager.LoadAccountsAsync();
        await _cursorAccountManager.LoadAccountsAsync();
        RefreshCursorDbState();

        UpdateDisplayIndices();
        UpdateCodexDisplayIndices();

        foreach (var account in Accounts)
        {
            account.PropertyChanged -= OnGoogleAccountPropertyChanged;
            account.PropertyChanged += OnGoogleAccountPropertyChanged;
        }
        RebuildGoogleAccountRows();
        RebuildCodexAccountRows();

        _ = RefreshAllAccounts(RefreshQueueLevel.Background);
        _ = RefreshAllCursorAccounts(RefreshQueueLevel.Background);
        _ = RefreshAllCodexAccounts(RefreshQueueLevel.Background);
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
        CursorAccounts.CollectionChanged -= OnCursorAccountsCollectionChanged;
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
        RebuildCodexAccountRows();
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
        RebuildCodexAccountRows();
    }

    private void OnCodexAccountsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildCodexAccountRows();
    }

    private void OnCursorAccountsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        NotifyStatsChanged();
    }

    private void RebuildCodexAccountRows()
    {
        CodexAccountRows.Clear();
        _lastCodexCardsPerRow = CalculateCodexCardsPerRow();

        if (CodexAccounts.Count == 0)
            return;

        var cardsPerRow = _lastCodexCardsPerRow;

        for (int i = 0; i < CodexAccounts.Count; i += cardsPerRow)
        {
            var rowAccounts = CodexAccounts.Skip(i).Take(cardsPerRow).ToList();
            var row = new CodexAccountRow();

            foreach (var account in rowAccounts)
            {
                row.Accounts.Add(account);
            }

            CodexAccountRows.Add(row);
        }
    }

    private int CalculateCodexCardsPerRow()
    {
        var availableWidth = Width <= 0
            ? CodexCardWidth
            : Math.Max(CodexCardWidth, Width - CodexLayoutHorizontalPadding);

        return Math.Max(1, (int)Math.Floor((availableWidth + CodexCardSpacing) / (CodexCardWidth + CodexCardSpacing)));
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
                _ = RefreshAllCursorAccounts(RefreshQueueLevel.Background);
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
        if (CurrentTab == "Cursor")
        {
            if (!CanAddAccount)
            {
                await DisplayAlertAsync("Cursor Not Ready", "Cursor 설치/로그인 후 다시 시도해 주세요.", "OK");
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
            "Login with OpenAI (OAuth)",
            "Login with OpenAI (Web Session)");

        if (action == "Login with OpenAI (OAuth)")
        {
            await LoginViaOpenAIFlow();
        }
        else if (action == "Login with OpenAI (Web Session)")
        {
            StartCodexWebViewLogin();
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
                await DisplayAlertAsync("Cursor Not Detected", "Cursor 설치/로그인 후 다시 시도해 주세요.", "OK");
                return;
            }

            IsCursorCredentialLoginWaiting = true;
            var session = await _cursorDbTokenService.TryReadCurrentSessionAsync();
            Trace.WriteLine($"[Cursor] Session read result: success={session.Success}, db={session.DbPath}, msg={session.Message}");
            if (!session.Success || string.IsNullOrWhiteSpace(session.SessionToken))
                throw new Exception($"Cursor 세션을 읽지 못했습니다: {session.Message}");

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
            await DisplayAlertAsync("Success", "현재 Cursor 계정을 추가했습니다.", "OK");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Cursor] Add failed: {ex}");
            await DisplayAlertAsync("Cursor Login Failed", ex.Message, "OK");
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
        _cursorDbPath = path ?? "Not found";
        _cursorDbStatusMessage = _isCursorDbAvailable ?
            "Cursor DB detected. Click 'Add Current Account' above to add your currently logged-in account."
            : "Please check your Cursor installation or login status. It will be detected automatically after installation and login.";

        OnPropertyChanged(nameof(IsCursorDbAvailable));
        OnPropertyChanged(nameof(IsCursorDbMissing));
        OnPropertyChanged(nameof(CursorDbPath));
        OnPropertyChanged(nameof(CursorDbStatusMessage));
        OnPropertyChanged(nameof(CanAddAccount));
        OnPropertyChanged(nameof(AddAccountButtonText));
        Trace.WriteLine($"[Cursor] DB state refreshed. available={_isCursorDbAvailable}, path={_cursorDbPath}");
    }

    private async void OnOpenCursorStorageFolderClicked(object? sender, EventArgs e)
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
            await DisplayAlertAsync("Open Folder Failed", ex.Message, "OK");
        }
    }

    private async void OnOpenCursorHomeClicked(object? sender, EventArgs e)
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

    private async void StartCodexWebViewLogin(bool clearSession = false)
    {
        IsCodexLoginOverlayVisible = true;
        _isProcessingCodexLogin = false;

#if WINDOWS
        if (clearSession)
        {
            try
            {
                if (CodexLoginWebView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 webView2)
                {
                    await webView2.EnsureCoreWebView2Async();
                    webView2.CoreWebView2.CookieManager.DeleteAllCookies();
                    // Force a clean session when token refresh or auth recovery fails.
                    await webView2.CoreWebView2.Profile.ClearBrowsingDataAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebView2] Failed to clear cookies/cache: {ex.Message}");
            }
        }
#endif

        CodexLoginWebView.Source = "https://chatgpt.com/auth/login";
    }

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

    private Task PromptCodexReLoginAsync(string title, string message)
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await DisplayAlertAsync(title, message, "OK");
            StartCodexWebViewLogin(clearSession: true);
        });
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

            var accountKey = GetCodexTokenStorageKey(codexAccount);
            await _tokenStorage.SaveTokensAsync(accountKey, tokens.access_token, tokens.refresh_token, tokens.expires_in);
            await ScheduleTokenRefreshAsync(codexAccount);
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

    // Refresh token if needed (called from scheduler or after 401)
    private async Task RefreshTokenIfNeededAsync(CodexAccount account)
    {
        var accountKey = GetCodexTokenStorageKey(account);
        var (_, refresh, expiresAt) = await _tokenStorage.LoadTokensAsync(accountKey);
        if (string.IsNullOrEmpty(refresh))
        {
            // No refresh token – ask user to re‑login
            await PromptCodexReLoginAsync("Session Expired", "Automatic refresh is unavailable for this login. Please sign in again.");
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
                await PromptCodexReLoginAsync("Authentication Expired", "Refresh failed. A fresh web session login will open now.");
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
                        await RefreshTokenIfNeededAsync(account);
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

            await _tokenStorage.SaveTokensAsync(codexAccount.id, token, null);

            try
            {
                await UpdateCodexAccountData(codexAccount);
            }
            catch (Exception ex) when (ex.Message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine("[CodexWebView] Access token likely expired, attempting refresh...");
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
                    Debug.WriteLine("[CodexWebView] No refresh token available, prompting re-login.");
                    await DisplayAlertAsync("Session Expired", "Please log in again.", "OK");
                    StartCodexWebViewLogin(clearSession: true);
                    return;
                }
            }

            _codexAccountManager.AddOrUpdateAccount(codexAccount);
            UpdateCodexDisplayIndices();
            await DisplayAlertAsync("Success", "OpenAI account successfully added.", "OK");
        }
        catch (Exception ex)
        {
            _isProcessingCodexLogin = false;
            Debug.WriteLine($"Codex Token Process Error: {ex.Message}");
            await DisplayAlertAsync("Login Failed", $"Failed to process Codex token:\n{ex.Message}", "OK");
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
    private async Task EnqueueRefreshAsync(object account, RefreshQueueLevel queueLevel)
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
        else if (account is CursorAccount r)
        {
            if (r.IsRefreshing || r.IsRefreshQueued) return;
            r.IsRefreshQueued = true;
            r.HasError = false;
        }

        var providerSemaphore = account switch
        {
            CloudAccount => _googleRefreshSemaphore,
            CursorAccount => _cursorRefreshSemaphore,
            _ => _codexRefreshSemaphore
        };

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
                MainThread.BeginInvokeOnMainThread(() => {
                    googleAcc.IsRefreshQueued = false;
                    googleAcc.IsRefreshing = true;
                });
                try
                {
                    await Task.Delay(500); // FORCE DELAY TO SEE ANIMATION
                    await RetryAsync(async () => await _googleRadar.UpdateAccountDataAsync(googleAcc), 3, 15);
                }
                catch (Exception ex) { 
                    MainThread.BeginInvokeOnMainThread(() => {
                        googleAcc.HasError = true; 
                        googleAcc.LastErrorMessage = ex.Message; 
                    });
                }
                finally { 
                    MainThread.BeginInvokeOnMainThread(() => googleAcc.IsRefreshing = false); 
                }
            }
            else if (account is CodexAccount codexAcc)
            {
                // Use dedicated token refresh semaphore for token-only refreshes.
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    codexAcc.IsRefreshQueued = false;
                    codexAcc.IsRefreshing = true;
                });
                try
                {
                    await Task.Delay(500); // FORCE DELAY TO SEE ANIMATION
                    await RetryAsync(async () => await UpdateCodexAccountData(codexAcc), 3, 15);
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
                    {
                        await _tokenRefreshSemaphore.WaitAsync();
                        try
                        {
                            await RefreshTokenIfNeededAsync(codexAcc);
                        }
                        finally
                        {
                            _tokenRefreshSemaphore.Release();
                        }
                    }
                    else
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            codexAcc.HasError = true;
                            codexAcc.LastErrorMessage = ex.Message;
                        });
                    }
                }
                finally
                {
                    MainThread.BeginInvokeOnMainThread(() => codexAcc.IsRefreshing = false);
                }
            }
            else if (account is CursorAccount cursorAcc)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    cursorAcc.IsRefreshQueued = false;
                    cursorAcc.IsRefreshing = true;
                });
                try
                {
                    await RetryAsync(async () => await UpdateCursorAccountData(cursorAcc), 2, 10);
                }
                catch (Exception ex)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        cursorAcc.HasError = true;
                        cursorAcc.LastErrorMessage = ex.Message;
                    });
                }
                finally
                {
                    MainThread.BeginInvokeOnMainThread(() => cursorAcc.IsRefreshing = false);
                }
            }
        }
        finally
        {
            // Always ensure queued state is cleared
            if (account is CloudAccount g2) g2.IsRefreshQueued = false;
            if (account is CodexAccount c2) c2.IsRefreshQueued = false;
            if (account is CursorAccount r2) r2.IsRefreshQueued = false;
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

            _ = _accountManager.SaveAccountsAsync();
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

            _ = _codexAccountManager.SaveAccountsAsync();
            UpdateCodexDisplayIndices();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Codex refresh error: {ex.Message}");
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
            Debug.WriteLine($"Cursor refresh error: {ex.Message}");
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

                    if (usage.Promo != null && !string.IsNullOrWhiteSpace(usage.Promo.Message))
                    {
                        account.PromoMessage = usage.Promo.Message;
                    }
                    else
                    {
                        account.PromoMessage = string.Empty;
                    }

                    account.last_updated = DateTime.Now;
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Codex update error for {account.name}: {ex.Message}");
            throw;
        }
    }

    private async Task UpdateCursorAccountData(CursorAccount account)
    {
        Trace.WriteLine($"[Cursor] Update start. name={account.name}, email={account.email}, hasSession={!string.IsNullOrWhiteSpace(account.session_token)}, hasLoginId={!string.IsNullOrWhiteSpace(account.login_email)}");
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

        Trace.WriteLine($"[Cursor] Update fetched. apiEmail={(result.Email ?? "N/A")}, used={result.Used}, limit={result.Limit}");

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

    private async void OnRefreshCodexAccountClicked(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is CodexAccount account)
        {
            IsBusy = true;
            await EnqueueRefreshAsync(account, RefreshQueueLevel.Immediate);
            _ = _codexAccountManager.SaveAccountsAsync();
            IsBusy = false;
        }
    }

    private async void OnErrorIconTapped(object? sender, TappedEventArgs e)
    {
        string? msg = null;
        if (e.Parameter is CloudAccount cloud)
            msg = cloud.LastErrorMessage;
        else if (e.Parameter is CodexAccount codex)
            msg = codex.LastErrorMessage;

        if (!string.IsNullOrEmpty(msg))
        {
            string title = "Update Failed";
            string description = msg;
            string msgLower = msg.ToLowerInvariant();

            if (msgLower.Contains("invalid or zeroed quota data") || msgLower.Contains("all elements are null"))
            {
                title = "Server Cache Delay (Stale Data)";
                description = "The API server temporarily returned expired cache data (all zeros). To protect your existing display data, the update has been paused.\n\nPlease try again by clicking the refresh (↻) button in a few moments.";
            }
            else if (msgLower.Contains("invalid_grant") || msgLower.Contains("failed to fetch") || msgLower.Contains("authorization") || msgLower.Contains("unauthorized") || msgLower.Contains("expired"))
            {
                title = "Authentication Expired";
                description = "The account login session has expired or lacks proper authorization.\n\nPlease remove this account using the 'REMOVE ACCOUNT' button, then log in again using the '+ Add Account' button at the top right.";
            }
            else if (msgLower.Contains("api error"))
            {
                title = "API Server Error";
                description = "The API server responded with an error. This is usually a temporary backend issue.\n\nPlease try again later.";
            }

            await DisplayAlertAsync(title, description, "OK");
        }
    }

    private async void OnRefreshAccountClicked(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is CloudAccount account)
        {
            IsBusy = true;
            await EnqueueRefreshAsync(account, RefreshQueueLevel.Immediate);
            _ = _accountManager.SaveAccountsAsync();
            NotifyStatsChanged();
            IsBusy = false;
        }
        else if (e.Parameter is CursorAccount cursorAccount)
        {
            IsBusy = true;
            await EnqueueRefreshAsync(cursorAccount, RefreshQueueLevel.Immediate);
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
                    _ = _accountManager.SaveAccountsAsync();
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
            var fileName = CurrentTab == "Google"
                ? "AIUsageMonitor_accounts.json"
                : CurrentTab == "Cursor"
                ? "AIUsageMonitor_cursor_accounts.json"
                : "AIUsageMonitor_codex_accounts.json";
            string json = "";
            if (CurrentTab == "Google")
                json = JsonSerializer.Serialize(Accounts.ToList());
            else if (CurrentTab == "Cursor")
                json = JsonSerializer.Serialize(CursorAccounts.ToList());
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
                else if (CurrentTab == "Cursor")
                {
                    var json = await File.ReadAllTextAsync(result.FullPath);
                    var list = JsonSerializer.Deserialize<List<CursorAccount>>(json);
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

    private void OnDeleteCursorAccountClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string id)
        {
            _cursorAccountManager.RemoveAccount(id);
            NotifyStatsChanged();
        }
    }

    private async void OnRenameCursorAccountClicked(object? sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not string id)
            return;

        var account = CursorAccounts.FirstOrDefault(a => a.id == id);
        if (account is null)
            return;

        var newName = await DisplayPromptAsync(
            "Cursor Account",
            "카드에 표시할 이름을 입력하세요.",
            "Save",
            "Cancel",
            account.DisplayName,
            maxLength: 80);

        if (string.IsNullOrWhiteSpace(newName))
            return;

        account.name = newName.Trim();
        _cursorAccountManager.AddOrUpdateAccount(account);
        NotifyStatsChanged();
    }

    private async void OnOpenCodexSiteClicked(object? sender, EventArgs e)
    {
        await Microsoft.Maui.ApplicationModel.Launcher.OpenAsync("https://openai.com/codex/");
    }

    private async void OnOpenGitHubSourceClicked(object? sender, EventArgs e)
    {
        await Microsoft.Maui.ApplicationModel.Launcher.OpenAsync("https://github.com/wenovaX/AIUsageMonitor");
    }

    private async void OnOpenGitHubReleasesClicked(object? sender, EventArgs e)
    {
        await Microsoft.Maui.ApplicationModel.Launcher.OpenAsync("https://github.com/wenovaX/AIUsageMonitor/releases");
    }
}

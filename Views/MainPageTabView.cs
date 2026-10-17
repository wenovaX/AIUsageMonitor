using Microsoft.Maui.Controls;

namespace AIUsageMonitor.Views;

public abstract class MainPageTabView : ContentView
{
    public static readonly BindableProperty AccountCardsPerRowProperty = BindableProperty.Create(nameof(AccountCardsPerRow), typeof(int), typeof(MainPageTabView), 1);

    private MainPage Owner => (MainPage)BindingContext;

    public int AccountCardsPerRow { get => (int)GetValue(AccountCardsPerRowProperty); set => SetValue(AccountCardsPerRowProperty, value); }

    protected void OnErrorIconTapped(object? sender, TappedEventArgs e) => Owner.OnErrorIconTapped(sender, e);
    protected void OnToggleSettingsTapped(object? sender, TappedEventArgs e) => Owner.OnToggleSettingsTapped(sender, e);
    protected void OnRefreshAccountClicked(object? sender, TappedEventArgs e) => Owner.OnRefreshAccountClicked(sender, e);
    protected void OnDeleteAccountClicked(object? sender, EventArgs e) => Owner.OnDeleteAccountClicked(sender, e);
    protected void OnOpenCursorHomeClicked(object? sender, EventArgs e) => Owner.OnOpenCursorHomeClicked(sender, e);
    protected void OnRenameCursorAccountClicked(object? sender, EventArgs e) => Owner.OnRenameCursorAccountClicked(sender, e);
    protected void OnDeleteCursorAccountClicked(object? sender, EventArgs e) => Owner.OnDeleteCursorAccountClicked(sender, e);
    protected void OnRefreshCodexAccountClicked(object? sender, TappedEventArgs e) => Owner.OnRefreshCodexAccountClicked(sender, e);
    protected void OnDeleteCodexAccountClicked(object? sender, EventArgs e) => Owner.OnDeleteCodexAccountClicked(sender, e);
    protected void OnOpenSlackApiClicked(object? sender, EventArgs e) => Owner.OnOpenSlackApiClicked(sender, e);
    protected void OnSlackTestClicked(object? sender, EventArgs e) => Owner.OnSlackTestClicked(sender, e);
    protected void OnScheduledTestClicked(object? sender, EventArgs e) => Owner.OnScheduledTestClicked(sender, e);
    protected void OnUpdateModelListClicked(object? sender, EventArgs e) => Owner.OnUpdateModelListClicked(sender, e);
    protected void OnSetModelListToDefaultClicked(object? sender, EventArgs e) => Owner.OnSetModelListToDefaultClicked(sender, e);
    protected void OnCleanMissingModelsClicked(object? sender, EventArgs e) => Owner.OnCleanMissingModelsClicked(sender, e);
    protected void OnDecreaseRefreshConcurrencyClicked(object? sender, EventArgs e) => Owner.OnDecreaseRefreshConcurrencyClicked(sender, e);
    protected void OnIncreaseRefreshConcurrencyClicked(object? sender, EventArgs e) => Owner.OnIncreaseRefreshConcurrencyClicked(sender, e);
    protected void OnLanguageSelectionChanged(object? sender, EventArgs e) => Owner.OnLanguageSelectionChanged(sender, e);
    protected void OnOpenCursorStorageFolderClicked(object? sender, EventArgs e) => Owner.OnOpenCursorStorageFolderClicked(sender, e);
    protected void OnOpenCodexSiteClicked(object? sender, TappedEventArgs e) => Owner.OnOpenCodexSiteClicked(sender, e);
    protected void OnOpenGitHubSourceClicked(object? sender, EventArgs e) => Owner.OnOpenGitHubSourceClicked(sender, e);
    protected void OnOpenGitHubReleasesClicked(object? sender, EventArgs e) => Owner.OnOpenGitHubReleasesClicked(sender, e);
}

namespace AIUsageMonitor.Controls;

public partial class AccountCardHeaderView : ContentView
{
    public static readonly BindableProperty AccountProperty = BindableProperty.Create(nameof(Account), typeof(object), typeof(AccountCardHeaderView));
    public static readonly BindableProperty DisplayNameProperty = BindableProperty.Create(nameof(DisplayName), typeof(string), typeof(AccountCardHeaderView), string.Empty);
    public static readonly BindableProperty DisplayEmailProperty = BindableProperty.Create(nameof(DisplayEmail), typeof(string), typeof(AccountCardHeaderView), string.Empty);
    public static readonly BindableProperty AvatarTextProperty = BindableProperty.Create(nameof(AvatarText), typeof(string), typeof(AccountCardHeaderView), string.Empty);
    public static readonly BindableProperty AvatarImageSourceProperty = BindableProperty.Create(nameof(AvatarImageSource), typeof(ImageSource), typeof(AccountCardHeaderView));
    public static readonly BindableProperty IsEmailVisibleProperty = BindableProperty.Create(nameof(IsEmailVisible), typeof(bool), typeof(AccountCardHeaderView), true);
    public static readonly BindableProperty IsAnonymousProperty = BindableProperty.Create(nameof(IsAnonymous), typeof(bool), typeof(AccountCardHeaderView), false, propertyChanged: OnAvatarStateChanged);
    public static readonly BindableProperty HasAvatarImageProperty = BindableProperty.Create(nameof(HasAvatarImage), typeof(bool), typeof(AccountCardHeaderView), false, propertyChanged: OnAvatarStateChanged);
    public static readonly BindableProperty IsRefreshingProperty = BindableProperty.Create(nameof(IsRefreshing), typeof(bool), typeof(AccountCardHeaderView), false);
    public static readonly BindableProperty IsRefreshQueuedProperty = BindableProperty.Create(nameof(IsRefreshQueued), typeof(bool), typeof(AccountCardHeaderView), false);
    public static readonly BindableProperty HasErrorProperty = BindableProperty.Create(nameof(HasError), typeof(bool), typeof(AccountCardHeaderView), false);

    public object? Account { get => GetValue(AccountProperty); set => SetValue(AccountProperty, value); }
    public string DisplayName { get => (string)GetValue(DisplayNameProperty); set => SetValue(DisplayNameProperty, value); }
    public string DisplayEmail { get => (string)GetValue(DisplayEmailProperty); set => SetValue(DisplayEmailProperty, value); }
    public string AvatarText { get => (string)GetValue(AvatarTextProperty); set => SetValue(AvatarTextProperty, value); }
    public ImageSource? AvatarImageSource { get => (ImageSource?)GetValue(AvatarImageSourceProperty); set => SetValue(AvatarImageSourceProperty, value); }
    public bool IsEmailVisible { get => (bool)GetValue(IsEmailVisibleProperty); set => SetValue(IsEmailVisibleProperty, value); }
    public bool IsAnonymous { get => (bool)GetValue(IsAnonymousProperty); set => SetValue(IsAnonymousProperty, value); }
    public bool HasAvatarImage { get => (bool)GetValue(HasAvatarImageProperty); set => SetValue(HasAvatarImageProperty, value); }
    public bool IsRefreshing { get => (bool)GetValue(IsRefreshingProperty); set => SetValue(IsRefreshingProperty, value); }
    public bool IsRefreshQueued { get => (bool)GetValue(IsRefreshQueuedProperty); set => SetValue(IsRefreshQueuedProperty, value); }
    public bool HasError { get => (bool)GetValue(HasErrorProperty); set => SetValue(HasErrorProperty, value); }

    public bool ShouldShowAvatarImage => HasAvatarImage && !IsAnonymous;
    public bool ShouldShowAvatarFallback => !ShouldShowAvatarImage;

    public event EventHandler<TappedEventArgs>? ErrorTapped;

    public AccountCardHeaderView() => InitializeComponent();

    private static void OnAvatarStateChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (AccountCardHeaderView)bindable;
        view.OnPropertyChanged(nameof(ShouldShowAvatarImage));
        view.OnPropertyChanged(nameof(ShouldShowAvatarFallback));
    }

    private void OnErrorIconTapped(object? sender, TappedEventArgs e) => ErrorTapped?.Invoke(sender, e);
}

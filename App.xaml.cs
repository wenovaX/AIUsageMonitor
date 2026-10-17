using AIUsageMonitor.PlatformAbstractions;
using AIUsageMonitor.Localization;
using AIUsageMonitor.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AIUsageMonitor
{
    public partial class App : Application
    {
        private readonly PlatformManager _platformManager;

        public App()
        {
            _platformManager = MauiProgram.Services.GetRequiredService<PlatformManager>();
            var notificationSettings = MauiProgram.Services.GetRequiredService<NotificationService>().Settings;
            string languageCode = LocalizationManager.NormalizePreference(notificationSettings.LanguageCode);
            if (notificationSettings.LanguageCode != languageCode)
                notificationSettings.LanguageCode = languageCode;
            LocalizationManager.Initialize(languageCode);
            InitializeComponent();
        }


        protected override Window CreateWindow(IActivationState? activationState)
        {
            var mainWindow = new Window(new AppShell());
            _platformManager.Current.Initialize(mainWindow);
            return mainWindow;
        }
    }
}

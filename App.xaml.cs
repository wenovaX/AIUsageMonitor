using AIUsageMonitor.PlatformAbstractions;
using Microsoft.Extensions.DependencyInjection;

namespace AIUsageMonitor
{
    public partial class App : Application
    {
        private readonly PlatformManager _platformManager;

        public App()
        {
            _platformManager = MauiProgram.Services.GetRequiredService<PlatformManager>();
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

using AIUsageMonitor.PlatformAbstractions;
using Microsoft.Extensions.DependencyInjection;

namespace AIUsageMonitor
{
    public partial class App : Application
    {
        private Window? _mainWindow;
        private readonly PlatformManager _platformManager;

        public App()
        {
            _platformManager = MauiProgram.Services.GetRequiredService<PlatformManager>();
            InitializeComponent();
        }


        protected override Window CreateWindow(IActivationState? activationState)
        {
            _mainWindow = new Window(new AppShell());
            _platformManager.Current.Initialize(_mainWindow);

            return _mainWindow;
        }

        private void OnShowAppClicked(object sender, EventArgs e)
        {
            _platformManager.Current.ShowMainWindow();
        }

        private void OnExitClicked(object sender, EventArgs e)
        {
            _platformManager.Current.ExitApplication();
        }
    }
}

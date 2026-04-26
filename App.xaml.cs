namespace AIUsageMonitor
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new AppShell());

            // Set window size for Windows platform based on adjusted card height and layout
#if WINDOWS
            window.Width = Preferences.Default.Get("WindowWidth", 1130.0);
            window.Height = Preferences.Default.Get("WindowHeight", 900.0);

            window.MinimumWidth = 850;
            window.MinimumHeight = 700;
            
            window.Title = "AIUsageMonitor";

            window.Destroying += (s, e) =>
            {
                Preferences.Default.Set("WindowWidth", window.Width);
                Preferences.Default.Set("WindowHeight", window.Height);
            };
#endif

            return window;
        }
    }
}
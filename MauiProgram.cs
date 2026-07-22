using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;
using H.NotifyIcon;
using AIUsageMonitor.PlatformAbstractions;
using AIUsageMonitor.PlatformImplementations;
using AIUsageMonitor.Services;

namespace AIUsageMonitor
{
    public static class MauiProgram
    {
        public static IServiceProvider Services { get; private set; } = default!;

        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .UseNotifyIcon()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            builder.Services.AddSingleton<IPlatformController, WindowsController>();
            builder.Services.AddSingleton<PlatformManager>();
            builder.Services.AddSingleton<ModelCatalogService>();
            builder.Services.AddSingleton<TokenStorageService>();
            builder.Services.AddSingleton<NotificationService>();

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            var app = builder.Build();
            Services = app.Services;
            return app;
        }
    }
}

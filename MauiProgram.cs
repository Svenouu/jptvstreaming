using CommunityToolkit.Maui;
using Jptv.streaming.Services;
using Microsoft.Extensions.Logging;

namespace Jptv.streaming
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkitMediaElement()
                .UseMauiCommunityToolkit()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

            // Enregistrement du HttpClient
            builder.Services.AddSingleton<HttpClient>();
            
            // Service FlareSolverr pour bypass Cloudflare
            builder.Services.AddSingleton<FlareSolverrService>(sp =>
            {
                var service = new FlareSolverrService();
                // Charger l'URL sauvegardée si disponible
                var savedUrl = Preferences.Get("FlareSolverrUrl", "http://4.211.70.50:8191/v1");
                service.FlareSolverrUrl = savedUrl;
                return service;
            });
            
            // Service de résolution d'URLs vidéo (similaire à streamlink/yt-dlp)
            builder.Services.AddSingleton<VideoUrlResolverService>();
            
            // Enregistrement des services
            // ScrapingService utilise FlareSolverr avec fallback vers données mock
            builder.Services.AddSingleton<IScrapingService, ScrapingService>();
            builder.Services.AddSingleton<ITranslationService, TranslationService>();
            
            // Service pour gérer le bouton retour Android et le mode plein écran
            builder.Services.AddSingleton<IBackButtonService, BackButtonService>();

#if DEBUG
    		builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}

using Android.App;
using Android.Content.PM;
using Android.OS;
using Jptv.streaming.Services;

namespace Jptv.streaming
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, 
        LaunchMode = LaunchMode.SingleTop,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        private IBackButtonService? _backButtonService;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
        }

        protected override void OnResume()
        {
            base.OnResume();
            // Récupérer le service depuis le conteneur DI
            _backButtonService = IPlatformApplication.Current?.Services.GetService<IBackButtonService>();
        }

        public override void OnBackPressed()
        {
            if (_backButtonService != null)
            {
                _backButtonService.TriggerBackButton();
                
                // Si le handler a consommé l'événement, ne pas faire le comportement par défaut
                if (_backButtonService.IsHandled)
                {
                    return;
                }
            }
            
            // Comportement par défaut
            base.OnBackPressed();
        }
    }
}

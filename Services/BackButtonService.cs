#if ANDROID
using Android.Views;
#endif

namespace Jptv.streaming.Services
{
    /// <summary>
    /// Implémentation du service de gestion du bouton retour et plein écran
    /// </summary>
    public class BackButtonService : IBackButtonService
    {
        public event Action? OnBackButtonPressed;
        public bool IsHandled { get; set; }

        public void TriggerBackButton()
        {
            IsHandled = false;
            OnBackButtonPressed?.Invoke();
        }

        public void EnterFullScreen()
        {
#if ANDROID
            var activity = Platform.CurrentActivity;
            if (activity?.Window != null)
            {
                // Cache la barre de statut et la barre de navigation
                if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.R)
                {
                    // API 30+ (Android 11+)
                    activity.Window.SetDecorFitsSystemWindows(false);
                    var controller = activity.Window.InsetsController;
                    if (controller != null)
                    {
                        controller.Hide(WindowInsets.Type.StatusBars() | WindowInsets.Type.NavigationBars());
                        controller.SystemBarsBehavior = (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
                    }
                }
                else
                {
                    // API < 30
#pragma warning disable CA1422
                    activity.Window.DecorView.SystemUiVisibility = (StatusBarVisibility)(
                        SystemUiFlags.Fullscreen |
                        SystemUiFlags.HideNavigation |
                        SystemUiFlags.ImmersiveSticky |
                        SystemUiFlags.LayoutFullscreen |
                        SystemUiFlags.LayoutHideNavigation |
                        SystemUiFlags.LayoutStable
                    );
#pragma warning restore CA1422
                }
            }
#endif
        }

        public void ExitFullScreen()
        {
#if ANDROID
            var activity = Platform.CurrentActivity;
            if (activity?.Window != null)
            {
                if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.R)
                {
                    // API 30+ (Android 11+)
                    activity.Window.SetDecorFitsSystemWindows(true);
                    var controller = activity.Window.InsetsController;
                    controller?.Show(WindowInsets.Type.StatusBars() | WindowInsets.Type.NavigationBars());
                }
                else
                {
                    // API < 30
#pragma warning disable CA1422
                    activity.Window.DecorView.SystemUiVisibility = (StatusBarVisibility)SystemUiFlags.Visible;
#pragma warning restore CA1422
                }
            }
#endif
        }
    }
}

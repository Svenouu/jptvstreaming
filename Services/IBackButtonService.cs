namespace Jptv.streaming.Services
{
    /// <summary>
    /// Service pour gérer le bouton retour Android et le mode plein écran
    /// </summary>
    public interface IBackButtonService
    {
        /// <summary>
        /// Événement déclenché quand le bouton retour est pressé
        /// </summary>
        event Action? OnBackButtonPressed;

        /// <summary>
        /// Indique si un handler consomme actuellement le bouton retour
        /// </summary>
        bool IsHandled { get; set; }

        /// <summary>
        /// Active le mode plein écran (cache la barre de statut)
        /// </summary>
        void EnterFullScreen();

        /// <summary>
        /// Désactive le mode plein écran (affiche la barre de statut)
        /// </summary>
        void ExitFullScreen();

        /// <summary>
        /// Déclenche l'événement du bouton retour
        /// </summary>
        void TriggerBackButton();
    }
}

namespace Jptv.streaming.Services;

/// <summary>
/// Interface pour le service de traduction
/// </summary>
public interface ITranslationService
{
    /// <summary>
    /// Traduit un texte japonais en romanji
    /// </summary>
    /// <param name="japaneseText">Texte en japonais</param>
    /// <param name="cancellationToken">Token d'annulation</param>
    /// <returns>Texte en romanji</returns>
    Task<string> TranslateToRomanjiAsync(string japaneseText, CancellationToken cancellationToken = default);

    /// <summary>
    /// Traduit un texte japonais vers la langue locale de l'appareil
    /// </summary>
    /// <param name="japaneseText">Texte en japonais</param>
    /// <param name="cancellationToken">Token d'annulation</param>
    /// <returns>Texte traduit</returns>
    Task<string> TranslateToLocalLanguageAsync(string japaneseText, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtient le code de la langue locale de l'appareil
    /// </summary>
    string LocalLanguageCode { get; }
}

using Jptv.streaming.Models;

namespace Jptv.streaming.Services;

/// <summary>
/// Interface pour le service de scraping des sites de streaming
/// </summary>
public interface IScrapingService
{
    /// <summary>
    /// Récupère une page de posts vidéo
    /// </summary>
    /// <param name="page">Numéro de page (commence à 1)</param>
    /// <param name="pageSize">Nombre d'éléments par page</param>
    /// <param name="cancellationToken">Token d'annulation</param>
    /// <returns>Liste des posts vidéo</returns>
    Task<IEnumerable<VideoPost>> GetVideosAsync(int page, int pageSize = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extrait l'URL directe de la vidéo depuis la page du post
    /// </summary>
    /// <param name="pageUrl">URL de la page du post</param>
    /// <param name="cancellationToken">Token d'annulation</param>
    /// <returns>URL directe de la vidéo</returns>
    Task<string?> ExtractVideoUrlAsync(string pageUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// URL de base du site à scraper
    /// </summary>
    string BaseUrl { get; }
}

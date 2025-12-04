namespace Jptv.streaming.Models;

/// <summary>
/// Représente un post vidéo récupéré depuis un site de streaming
/// </summary>
public class VideoPost
{
    /// <summary>
    /// Identifiant unique du post
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// URL de l'image de couverture
    /// </summary>
    public string ThumbnailUrl { get; set; } = string.Empty;

    /// <summary>
    /// Titre original en japonais
    /// </summary>
    public string OriginalTitle { get; set; } = string.Empty;

    /// <summary>
    /// Titre traduit en romanji
    /// </summary>
    public string RomanjiTitle { get; set; } = string.Empty;

    /// <summary>
    /// Titre traduit dans la langue locale de l'appareil
    /// </summary>
    public string LocalizedTitle { get; set; } = string.Empty;

    /// <summary>
    /// URL de la page du post sur le site source
    /// </summary>
    public string PageUrl { get; set; } = string.Empty;

    /// <summary>
    /// URL directe de la vidéo (après extraction)
    /// </summary>
    public string? VideoUrl { get; set; }

    /// <summary>
    /// Date de publication
    /// </summary>
    public DateTime? PublishedDate { get; set; }

    /// <summary>
    /// Durée de la vidéo si disponible
    /// </summary>
    public TimeSpan? Duration { get; set; }
}

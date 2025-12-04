using HtmlAgilityPack;
using Jptv.streaming.Models;
using System.Net;
using System.Web;

namespace Jptv.streaming.Services;

/// <summary>
/// Service de scraping pour le site 9tsu.cc
/// Utilise FlareSolverr pour contourner la protection Cloudflare
/// </summary>
public class ScrapingService : IScrapingService
{
    private const string SiteBaseUrl = "https://9tsu.cc";
    private const string AjaxUrl = "https://9tsu.cc/wp-admin/admin-ajax.php";
    private const string CategoryName = "douga";
    
    private readonly FlareSolverrService _flareSolverr;
    private readonly VideoUrlResolverService _videoUrlResolverService;
    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer;
    
    private bool _isInitialized;
    private bool _usesFlareSolverr;
    private bool _usesFallback;
    private string? _userAgent;

    public string BaseUrl => $"{SiteBaseUrl}/{CategoryName}";

    public ScrapingService(FlareSolverrService flareSolverr, VideoUrlResolverService videoUrlResolverService)
    {
        _flareSolverr = flareSolverr;
        _videoUrlResolverService = videoUrlResolverService;
        _cookieContainer = new CookieContainer();
        
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            UseCookies = true,
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        
        SetupDefaultHeaders();
    }

    private void SetupDefaultHeaders(string? userAgent = null)
    {
        _httpClient.DefaultRequestHeaders.Clear();
        
        _userAgent = userAgent ?? 
            "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";
        
        _httpClient.DefaultRequestHeaders.Add("User-Agent", _userAgent);
        _httpClient.DefaultRequestHeaders.Add("Accept", 
            "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "ja,en-US;q=0.9,en;q=0.8");
        _httpClient.DefaultRequestHeaders.Add("Referer", SiteBaseUrl);
    }

    /// <summary>
    /// Initialise la session - teste d'abord FlareSolverr, puis connexion directe
    /// </summary>
    private async Task InitializeSessionAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized) return;

        Console.WriteLine("Initialisation de la session de scraping...");

        // 1. Tester FlareSolverr
        if (await _flareSolverr.ConfigureAsync())
        {
            Console.WriteLine("FlareSolverr disponible - utilisation pour le scraping");
            _usesFlareSolverr = true;
            _usesFallback = false;
            _isInitialized = true;
            await _flareSolverr.ObtainCookiesAsync(SiteBaseUrl, cancellationToken);
            return;
        }

        Console.WriteLine("FlareSolverr non disponible - tentative de connexion directe");

        // 2. Tester la connexion directe
        try
        {
            var response = await _httpClient.GetAsync(BaseUrl, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                
                // Vérifier si on a reçu une page Cloudflare challenge
                if (IsCloudflareChallenge(content))
                {
                    Console.WriteLine("Challenge Cloudflare détecté - mode fallback activé");
                    _usesFallback = true;
                }
                else
                {
                    Console.WriteLine("Connexion directe réussie");
                    _usesFallback = false;
                }
            }
            else
            {
                Console.WriteLine($"Connexion directe échouée ({response.StatusCode}) - mode fallback");
                _usesFallback = true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur connexion directe: {ex.Message} - mode fallback");
            _usesFallback = true;
        }

        _isInitialized = true;
    }

    /// <summary>
    /// Vérifie si le contenu HTML est un challenge Cloudflare
    /// </summary>
    private static bool IsCloudflareChallenge(string content)
    {
        return content.Contains("cf-browser-verification") || 
               content.Contains("challenge-platform") ||
               content.Contains("Just a moment") ||
               content.Contains("Checking your browser") ||
               content.Contains("cf-spinner");
    }

    public async Task<IEnumerable<VideoPost>> GetVideosAsync(int page, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        await InitializeSessionAsync(cancellationToken);

        var videos = new List<VideoPost>();

        // Mode fallback - données de démonstration
        if (_usesFallback && !_usesFlareSolverr)
        {
            Console.WriteLine($"Mode fallback - données de démonstration (page {page})");
            return videos;
        }

        try
        {
            string? htmlContent;
            
            htmlContent = await GetAjaxPageHtmlAsync(page-1, cancellationToken);

            if (htmlContent != null)
            {
                videos = ParseArticles(htmlContent);
            }
            
            if (videos.Count == 0)
            {
                Console.WriteLine("Aucune vidéo parsée - passage en mode fallback");
            }
            
            Console.WriteLine($"Page {page}: {videos.Count} vidéos trouvées");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur scraping page {page}: {ex.Message} - passage en mode fallback");
        }

        return videos;
    }

    /// <summary>
    /// Récupère le HTML via l'appel AJAX pour les pages suivantes
    /// </summary>
    private async Task<string?> GetAjaxPageHtmlAsync(int page, CancellationToken cancellationToken)
    {
        var postData = $"action=load_more&page={page}&template=html%2Floop%2Fcontent&vars%5Bcategory_name%5D={CategoryName}&id_playlist=";

        if (_usesFlareSolverr)
        {
            var response = await _flareSolverr.PostAsync(AjaxUrl, postData, 60000, cancellationToken);
            return response?.Solution?.Response;
        }
        else
        {
            var formData = new Dictionary<string, string>
            {
                { "action", "load_more" },
                { "page", page.ToString() },
                { "template", "html/loop/content" },
                { "vars[category_name]", CategoryName },
                { "id_playlist", "" }
            };

            var content = new FormUrlEncodedContent(formData);
            
            using var request = new HttpRequestMessage(HttpMethod.Post, AjaxUrl);
            request.Content = content;
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            request.Headers.Add("Origin", SiteBaseUrl);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Parse les articles HTML pour extraire les vidéos
    /// </summary>
    private List<VideoPost> ParseArticles(string htmlContent)
    {
        var videos = new List<VideoPost>();
        var doc = new HtmlDocument();
        doc.LoadHtml(htmlContent);

        var articles = doc.DocumentNode.SelectNodes("//article[contains(@class, 'cactus-post-item')]");
        
        if (articles == null)
        {
            Console.WriteLine("Aucun article trouvé dans le HTML");
            return videos;
        }

        int index = 0;
        foreach (var article in articles)
        {
            try
            {
                var video = ParseArticle(article, index++);
                if (video != null)
                {
                    videos.Add(video);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur parsing article: {ex.Message}");
            }
        }

        return videos;
    }

    /// <summary>
    /// Parse un article HTML individuel
    /// </summary>
    private VideoPost? ParseArticle(HtmlNode article, int index)
    {
        var imgNode = article.SelectSingleNode(".//div[contains(@class, 'picture-content')]//img");
        var thumbnailUrl = imgNode?.GetAttributeValue("data-src", null) 
                          ?? imgNode?.GetAttributeValue("src", null)
                          ?? "";

        var titleNode = article.SelectSingleNode(".//h3[contains(@class, 'cactus-post-title')]//a");
        var title = titleNode?.InnerText?.Trim() ?? "";
        var pageUrl = titleNode?.GetAttributeValue("href", "") ?? "";

        title = HttpUtility.HtmlDecode(title);

        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(pageUrl))
        {
            return null;
        }

        var id = ExtractIdFromUrl(pageUrl) ?? $"video_{index}";

        return new VideoPost
        {
            Id = id,
            ThumbnailUrl = thumbnailUrl,
            OriginalTitle = title,
            RomanjiTitle = string.Empty,
            LocalizedTitle = string.Empty,
            PageUrl = pageUrl,
            PublishedDate = null,
            Duration = null
        };
    }

    private static string? ExtractIdFromUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        
        try
        {
            var uri = new Uri(url);
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            return segments.Length > 0 ? segments[^1] : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extrait l'URL de la vidéo depuis la page du post
    /// Recherche le script jQuery qui contient l'URL de l'iframe vidéo
    /// </summary>
    public async Task<string?> ExtractVideoUrlAsync(string pageUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            Console.WriteLine($"Extraction de l'URL vidéo depuis: {pageUrl}");
            
            string? htmlContent;
            
            if (_usesFlareSolverr)
            {
                var response = await _flareSolverr.GetAsync(pageUrl, 60000, cancellationToken);
                htmlContent = response?.Solution?.Response;
            }
            else
            {
                var response = await _httpClient.GetAsync(pageUrl, cancellationToken);
                response.EnsureSuccessStatusCode();
                htmlContent = await response.Content.ReadAsStringAsync(cancellationToken);
            }

            if (string.IsNullOrEmpty(htmlContent))
            {
                Console.WriteLine("Contenu HTML vide");
                return null;
            }

            // Méthode 1: Chercher l'URL dans le script jQuery (fallback iframe src)
            var videoUrl = ExtractVideoUrlFromScript(htmlContent);
            
            if (!string.IsNullOrEmpty(videoUrl))
            {
                Console.WriteLine($"URL vidéo trouvée (script): {videoUrl}");
                return (await _videoUrlResolverService.ResolveAsync(videoUrl, cancellationToken)).DirectUrl;
            }

            // Méthode 2: Chercher directement dans un iframe existant
            videoUrl = ExtractVideoUrlFromIframe(htmlContent);
            
            if (!string.IsNullOrEmpty(videoUrl))
            {
                Console.WriteLine($"URL vidéo trouvée (iframe): {videoUrl}");
                return (await _videoUrlResolverService.ResolveAsync(videoUrl, cancellationToken)).DirectUrl;
            }

            // Méthode 3: Appeler l'API AJAX pour obtenir l'iframe
            videoUrl = await ExtractVideoUrlFromAjaxAsync(htmlContent, cancellationToken);
            
            if (!string.IsNullOrEmpty(videoUrl))
            {
                Console.WriteLine($"URL vidéo trouvée (ajax): {videoUrl}");
                return (await _videoUrlResolverService.ResolveAsync(videoUrl, cancellationToken)).DirectUrl;
            }

            Console.WriteLine("Aucune URL vidéo trouvée");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur extraction URL vidéo: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Extrait l'URL vidéo depuis le script jQuery (cherche le src dans le fallback)
    /// </summary>
    private static string? ExtractVideoUrlFromScript(string htmlContent)
    {
        // Pattern pour trouver l'URL dans le script jQuery
        // Exemple: src="//ok.ru/videoembed/10521329404563"
        var patterns = new[]
        {
            @"src=['""]([^'""]*(?:ok\.ru|dailymotion|youtube|vimeo|streamtape|dood|mixdrop)[^'""]*)['""]",
            @"src=['""]([^'""]+/videoembed/[^'""]+)['""]",
            @"src=['""]([^'""]+embed[^'""]+)['""]",
            @"<iframe[^>]+src=['""]([^'""]+)['""]"
        };

        foreach (var pattern in patterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(htmlContent, pattern, 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            if (match.Success && match.Groups.Count > 1)
            {
                var url = match.Groups[1].Value;
                url = NormalizeVideoUrl(url);
                
                if (IsValidVideoUrl(url))
                {
                    return url;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Extrait l'URL vidéo depuis un iframe existant dans le HTML
    /// </summary>
    private static string? ExtractVideoUrlFromIframe(string htmlContent)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(htmlContent);

        // Chercher dans le player-embed div
        var playerEmbed = doc.DocumentNode.SelectSingleNode("//div[@id='player-embed']//iframe");
        if (playerEmbed != null)
        {
            var src = playerEmbed.GetAttributeValue("src", null);
            if (!string.IsNullOrEmpty(src))
            {
                return NormalizeVideoUrl(src);
            }
        }

        // Chercher tout iframe avec une URL vidéo
        var iframes = doc.DocumentNode.SelectNodes("//iframe[@src]");
        if (iframes != null)
        {
            foreach (var iframe in iframes)
            {
                var src = iframe.GetAttributeValue("src", null);
                if (!string.IsNullOrEmpty(src) && IsValidVideoUrl(src))
                {
                    return NormalizeVideoUrl(src);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Appelle l'API AJAX pour obtenir l'iframe vidéo
    /// </summary>
    private async Task<string?> ExtractVideoUrlFromAjaxAsync(string htmlContent, CancellationToken cancellationToken)
    {
        try
        {
            // Extraire postId et wpnonce du script
            var postIdMatch = System.Text.RegularExpressions.Regex.Match(htmlContent, @"var\s+postId\s*=\s*(\d+)");
            var nonceMatch = System.Text.RegularExpressions.Regex.Match(htmlContent, @"_wpnonce:\s*['""]([^'""]+)['""]");

            if (!postIdMatch.Success)
            {
                Console.WriteLine("postId non trouvé dans le HTML");
                return null;
            }

            var postId = postIdMatch.Groups[1].Value;
            var nonce = nonceMatch.Success ? nonceMatch.Groups[1].Value : "";

            Console.WriteLine($"Appel AJAX pour postId={postId}, nonce={nonce}");

            string? responseContent;

            if (_usesFlareSolverr)
            {
                var postData = $"action=load_video_iframe&post_id={postId}&_wpnonce={nonce}";
                var response = await _flareSolverr.PostAsync(AjaxUrl, postData, 30000, cancellationToken);
                responseContent = response?.Solution?.Response;
            }
            else
            {
                var formData = new Dictionary<string, string>
                {
                    { "action", "load_video_iframe" },
                    { "post_id", postId },
                    { "_wpnonce", nonce }
                };

                var content = new FormUrlEncodedContent(formData);
                
                using var request = new HttpRequestMessage(HttpMethod.Post, AjaxUrl);
                request.Content = content;
                request.Headers.Add("X-Requested-With", "XMLHttpRequest");

                var response = await _httpClient.SendAsync(request, cancellationToken);
                responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            }

            if (string.IsNullOrEmpty(responseContent))
            {
                return null;
            }

            // La réponse contient le HTML de l'iframe
            return ExtractVideoUrlFromIframe(responseContent) ?? ExtractVideoUrlFromScript(responseContent);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur appel AJAX vidéo: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Normalise l'URL vidéo (ajoute https: si nécessaire)
    /// </summary>
    private static string NormalizeVideoUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return url;

        url = url.Trim();
        
        // Ajouter le protocole si manquant
        if (url.StartsWith("//"))
        {
            url = "https:" + url;
        }
        
        return url;
    }

    /// <summary>
    /// Vérifie si l'URL est une URL vidéo valide
    /// </summary>
    private static bool IsValidVideoUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return false;

        var videoHosts = new[]
        {
            "ok.ru", "dailymotion", "youtube", "youtu.be", "vimeo",
            "streamtape", "dood", "mixdrop", "fembed", "vidoza",
            "upstream", "videobin", "mp4upload", "vidlox"
        };

        return videoHosts.Any(host => url.Contains(host, StringComparison.OrdinalIgnoreCase)) ||
               url.Contains("embed", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("player", StringComparison.OrdinalIgnoreCase);
    }
}

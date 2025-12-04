using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jptv.streaming.Services;

/// <summary>
/// Service pour résoudre les URLs d'iframe en URLs de streaming directes
/// Similaire à streamlink/yt-dlp mais en .NET natif
/// </summary>
public partial class VideoUrlResolverService
{
    private readonly HttpClient _httpClient;
    private readonly FlareSolverrService _flareSolverr;

    public VideoUrlResolverService(FlareSolverrService flareSolverr)
    {
        _flareSolverr = flareSolverr;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    /// <summary>
    /// Résout une URL d'iframe en URL de streaming directe
    /// </summary>
    public async Task<VideoStreamInfo?> ResolveAsync(string iframeUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(iframeUrl))
            return null;

        Console.WriteLine($"Résolution de l'URL: {iframeUrl}");

        try
        {
            // Identifier le host et utiliser l'extracteur approprié
            var uri = new Uri(iframeUrl);
            var host = uri.Host.ToLower();

            return host switch
            {
                var h when h.Contains("ok.ru") => await ResolveOkRuAsync(iframeUrl, cancellationToken),
                var h when h.Contains("dailymotion") => await ResolveDailymotionAsync(iframeUrl, cancellationToken),
                var h when h.Contains("youtube") || h.Contains("youtu.be") => await ResolveYouTubeAsync(iframeUrl, cancellationToken),
                var h when h.Contains("vimeo") => await ResolveVimeoAsync(iframeUrl, cancellationToken),
                var h when h.Contains("streamtape") => await ResolveStreamtapeAsync(iframeUrl, cancellationToken),
                var h when h.Contains("dood") => await ResolveDoodAsync(iframeUrl, cancellationToken),
                var h when h.Contains("mixdrop") => await ResolveMixdropAsync(iframeUrl, cancellationToken),
                _ => new VideoStreamInfo { IframeUrl = iframeUrl, DirectUrl = iframeUrl, Host = host }
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur résolution URL: {ex.Message}");
            return new VideoStreamInfo { IframeUrl = iframeUrl, DirectUrl = iframeUrl, Error = ex.Message };
        }
    }

    /// <summary>
    /// Résout une URL ok.ru
    /// </summary>
    private async Task<VideoStreamInfo?> ResolveOkRuAsync(string url, CancellationToken cancellationToken)
    {
        Console.WriteLine("Résolution ok.ru...");

        // Extraire l'ID de la vidéo
        var videoIdMatch = OkRuVideoIdRegex().Match(url);
        if (!videoIdMatch.Success)
        {
            Console.WriteLine("ID vidéo ok.ru non trouvé");
            return null;
        }

        var videoId = videoIdMatch.Groups[1].Value;
        Console.WriteLine($"ok.ru video ID: {videoId}");

        // Appeler l'API ok.ru pour obtenir les métadonnées
        var metadataUrl = $"https://ok.ru/videoembed/{videoId}";
        
        string? html;
        if (_flareSolverr.HasValidCookies)
        {
            var response = await _flareSolverr.GetAsync(metadataUrl, 30000, cancellationToken);
            html = response?.Solution?.Response;
        }
        else
        {
            var response = await _httpClient.GetAsync(metadataUrl, cancellationToken);
            html = await response.Content.ReadAsStringAsync(cancellationToken);
        }

        if (string.IsNullOrEmpty(html))
            return null;

        // Chercher les URLs de streaming dans le HTML
        // ok.ru stocke les URLs dans un JSON encodé dans data-options
        var optionsMatch = OkRuOptionsRegex().Match(html);
        if (optionsMatch.Success)
        {
            var optionsJson = System.Net.WebUtility.HtmlDecode(optionsMatch.Groups[1].Value);
            try
            {
                var options = JsonSerializer.Deserialize<JsonElement>(optionsJson);
                if (options.TryGetProperty("flashvars", out var flashvars))
                {
                    if (flashvars.TryGetProperty("metadata", out var metadataElement))
                    {
                        var metadataJson = metadataElement.GetString();
                        if (!string.IsNullOrEmpty(metadataJson))
                        {
                            var metadata = JsonSerializer.Deserialize<JsonElement>(metadataJson);
                            
                            // Chercher les différentes qualités
                            var qualities = new List<VideoQuality>();
                            
                            if (metadata.TryGetProperty("videos", out var videos))
                            {
                                foreach (var video in videos.EnumerateArray())
                                {
                                    if (video.TryGetProperty("url", out var urlProp) &&
                                        video.TryGetProperty("name", out var nameProp))
                                    {
                                        qualities.Add(new VideoQuality
                                        {
                                            Name = nameProp.GetString() ?? "",
                                            Url = urlProp.GetString() ?? ""
                                        });
                                    }
                                }
                            }

                            // Prendre la meilleure qualité disponible
                            var bestQuality = qualities
                                .OrderByDescending(q => GetQualityPriority(q.Name))
                                .FirstOrDefault();

                            if (bestQuality != null && !string.IsNullOrEmpty(bestQuality.Url))
                            {
                                return new VideoStreamInfo
                                {
                                    IframeUrl = url,
                                    DirectUrl = bestQuality.Url,
                                    Host = "ok.ru",
                                    Quality = bestQuality.Name,
                                    AvailableQualities = qualities
                                };
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur parsing ok.ru JSON: {ex.Message}");
            }
        }

        // Méthode alternative: chercher directement les URLs dans le HTML
        var hlsMatch = HlsUrlRegex().Match(html);
        if (hlsMatch.Success)
        {
            return new VideoStreamInfo
            {
                IframeUrl = url,
                DirectUrl = hlsMatch.Groups[1].Value.Replace("\\/", "/"),
                Host = "ok.ru",
                IsHls = true
            };
        }

        var mp4Match = Mp4UrlRegex().Match(html);
        if (mp4Match.Success)
        {
            return new VideoStreamInfo
            {
                IframeUrl = url,
                DirectUrl = mp4Match.Groups[1].Value.Replace("\\/", "/"),
                Host = "ok.ru"
            };
        }

        return new VideoStreamInfo { IframeUrl = url, DirectUrl = url, Host = "ok.ru", Error = "URL directe non trouvée" };
    }

    /// <summary>
    /// Résout une URL Dailymotion
    /// </summary>
    private async Task<VideoStreamInfo?> ResolveDailymotionAsync(string url, CancellationToken cancellationToken)
    {
        Console.WriteLine("Résolution Dailymotion...");

        var videoIdMatch = DailymotionVideoIdRegex().Match(url);
        if (!videoIdMatch.Success)
            return null;

        var videoId = videoIdMatch.Groups[1].Value;
        
        // API Dailymotion pour les métadonnées
        var apiUrl = $"https://www.dailymotion.com/player/metadata/video/{videoId}";
        
        try
        {
            var response = await _httpClient.GetAsync(apiUrl, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var data = JsonSerializer.Deserialize<JsonElement>(json);

            if (data.TryGetProperty("qualities", out var qualities))
            {
                var qualityList = new List<VideoQuality>();
                
                foreach (var quality in qualities.EnumerateObject())
                {
                    foreach (var format in quality.Value.EnumerateArray())
                    {
                        if (format.TryGetProperty("url", out var urlProp))
                        {
                            qualityList.Add(new VideoQuality
                            {
                                Name = quality.Name,
                                Url = urlProp.GetString() ?? ""
                            });
                        }
                    }
                }

                var best = qualityList
                    .OrderByDescending(q => GetQualityPriority(q.Name))
                    .FirstOrDefault();

                if (best != null)
                {
                    return new VideoStreamInfo
                    {
                        IframeUrl = url,
                        DirectUrl = best.Url,
                        Host = "dailymotion",
                        Quality = best.Name,
                        AvailableQualities = qualityList
                    };
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur Dailymotion API: {ex.Message}");
        }

        return new VideoStreamInfo { IframeUrl = url, DirectUrl = url, Host = "dailymotion" };
    }

    /// <summary>
    /// Résout une URL YouTube (basique - pour une solution complète, utiliser YoutubeExplode)
    /// </summary>
    private async Task<VideoStreamInfo?> ResolveYouTubeAsync(string url, CancellationToken cancellationToken)
    {
        Console.WriteLine("Résolution YouTube...");
        
        // Pour YouTube, il est recommandé d'utiliser le package YoutubeExplode
        // Ici on retourne juste l'URL embed qui peut être lue par une WebView
        
        var videoIdMatch = YouTubeVideoIdRegex().Match(url);
        if (videoIdMatch.Success)
        {
            var videoId = videoIdMatch.Groups[1].Value;
            return new VideoStreamInfo
            {
                IframeUrl = url,
                DirectUrl = $"https://www.youtube.com/embed/{videoId}",
                Host = "youtube",
                RequiresWebView = true // YouTube nécessite généralement une WebView
            };
        }

        await Task.CompletedTask;
        return new VideoStreamInfo { IframeUrl = url, DirectUrl = url, Host = "youtube", RequiresWebView = true };
    }

    /// <summary>
    /// Résout une URL Vimeo
    /// </summary>
    private async Task<VideoStreamInfo?> ResolveVimeoAsync(string url, CancellationToken cancellationToken)
    {
        Console.WriteLine("Résolution Vimeo...");

        var videoIdMatch = VimeoVideoIdRegex().Match(url);
        if (!videoIdMatch.Success)
            return null;

        var videoId = videoIdMatch.Groups[1].Value;
        
        try
        {
            var configUrl = $"https://player.vimeo.com/video/{videoId}/config";
            var response = await _httpClient.GetAsync(configUrl, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var data = JsonSerializer.Deserialize<JsonElement>(json);

            if (data.TryGetProperty("request", out var request) &&
                request.TryGetProperty("files", out var files))
            {
                var qualityList = new List<VideoQuality>();

                if (files.TryGetProperty("progressive", out var progressive))
                {
                    foreach (var file in progressive.EnumerateArray())
                    {
                        if (file.TryGetProperty("url", out var urlProp) &&
                            file.TryGetProperty("quality", out var qualityProp))
                        {
                            qualityList.Add(new VideoQuality
                            {
                                Name = qualityProp.GetString() ?? "",
                                Url = urlProp.GetString() ?? ""
                            });
                        }
                    }
                }

                var best = qualityList
                    .OrderByDescending(q => GetQualityPriority(q.Name))
                    .FirstOrDefault();

                if (best != null)
                {
                    return new VideoStreamInfo
                    {
                        IframeUrl = url,
                        DirectUrl = best.Url,
                        Host = "vimeo",
                        Quality = best.Name,
                        AvailableQualities = qualityList
                    };
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur Vimeo API: {ex.Message}");
        }

        return new VideoStreamInfo { IframeUrl = url, DirectUrl = url, Host = "vimeo" };
    }

    /// <summary>
    /// Résout une URL Streamtape
    /// </summary>
    private async Task<VideoStreamInfo?> ResolveStreamtapeAsync(string url, CancellationToken cancellationToken)
    {
        Console.WriteLine("Résolution Streamtape...");

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            var html = await response.Content.ReadAsStringAsync(cancellationToken);

            // Streamtape utilise un script obfusqué pour générer l'URL
            // Pattern: document.getElementById('robotlink').innerHTML = '...' + ('...');
            var linkMatch = StreamtapeLinkRegex().Match(html);
            if (linkMatch.Success)
            {
                var part1 = linkMatch.Groups[1].Value;
                var part2 = linkMatch.Groups[2].Value;
                var directUrl = $"https:{part1}{part2.Substring(3)}";
                
                return new VideoStreamInfo
                {
                    IframeUrl = url,
                    DirectUrl = directUrl,
                    Host = "streamtape"
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur Streamtape: {ex.Message}");
        }

        return new VideoStreamInfo { IframeUrl = url, DirectUrl = url, Host = "streamtape" };
    }

    /// <summary>
    /// Résout une URL DoodStream
    /// </summary>
    private async Task<VideoStreamInfo?> ResolveDoodAsync(string url, CancellationToken cancellationToken)
    {
        Console.WriteLine("Résolution DoodStream...");

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            var html = await response.Content.ReadAsStringAsync(cancellationToken);

            // DoodStream génère une URL via une requête AJAX
            var passMatch = DoodPassRegex().Match(html);
            if (passMatch.Success)
            {
                var passUrl = "https://dood.to" + passMatch.Groups[1].Value;
                
                // Ajouter un délai et le referer
                using var request = new HttpRequestMessage(HttpMethod.Get, passUrl);
                request.Headers.Add("Referer", url);
                
                var passResponse = await _httpClient.SendAsync(request, cancellationToken);
                var directUrl = await passResponse.Content.ReadAsStringAsync(cancellationToken);
                
                if (!string.IsNullOrEmpty(directUrl) && directUrl.StartsWith("http"))
                {
                    // Ajouter un token aléatoire
                    directUrl += "?token=" + GenerateRandomToken() + "&expiry=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    
                    return new VideoStreamInfo
                    {
                        IframeUrl = url,
                        DirectUrl = directUrl,
                        Host = "doodstream",
                        RequiresReferer = true,
                        Referer = url
                    };
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur DoodStream: {ex.Message}");
        }

        return new VideoStreamInfo { IframeUrl = url, DirectUrl = url, Host = "doodstream" };
    }

    /// <summary>
    /// Résout une URL Mixdrop
    /// </summary>
    private async Task<VideoStreamInfo?> ResolveMixdropAsync(string url, CancellationToken cancellationToken)
    {
        Console.WriteLine("Résolution Mixdrop...");

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            var html = await response.Content.ReadAsStringAsync(cancellationToken);

            // Mixdrop utilise un script MDCore.wurl
            var urlMatch = MixdropUrlRegex().Match(html);
            if (urlMatch.Success)
            {
                var encodedUrl = urlMatch.Groups[1].Value;
                // Décoder l'URL (Mixdrop utilise un encoding simple)
                var directUrl = "https:" + DecodeMixdropUrl(encodedUrl);
                
                return new VideoStreamInfo
                {
                    IframeUrl = url,
                    DirectUrl = directUrl,
                    Host = "mixdrop"
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur Mixdrop: {ex.Message}");
        }

        return new VideoStreamInfo { IframeUrl = url, DirectUrl = url, Host = "mixdrop" };
    }

    /// <summary>
    /// Décode l'URL Mixdrop (encodage propriétaire simple)
    /// </summary>
    private static string DecodeMixdropUrl(string encoded)
    {
        // Mixdrop utilise parfois un simple décalage de caractères
        // Cette implémentation peut nécessiter des ajustements selon la version du site
        return encoded.Replace("\\", "");
    }

    /// <summary>
    /// Génère un token aléatoire pour DoodStream
    /// </summary>
    private static string GenerateRandomToken()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, 10).Select(s => s[random.Next(s.Length)]).ToArray());
    }

    /// <summary>
    /// Retourne la priorité d'une qualité (plus haut = meilleur)
    /// </summary>
    private static int GetQualityPriority(string quality)
    {
        return quality.ToLower() switch
        {
            "4k" or "2160p" or "2160" => 2160,
            "1440p" or "1440" => 1440,
            "1080p" or "1080" or "full" or "hd" => 1080,
            "720p" or "720" or "hd" => 720,
            "480p" or "480" or "sd" => 480,
            "360p" or "360" => 360,
            "240p" or "240" => 240,
            "144p" or "144" => 144,
            "auto" => 500, // Auto généralement bon
            _ when quality.Contains("1080") => 1080,
            _ when quality.Contains("720") => 720,
            _ when quality.Contains("480") => 480,
            _ => 0
        };
    }

    #region Regex Patterns

    [GeneratedRegex(@"videoembed/(\d+)")]
    private static partial Regex OkRuVideoIdRegex();

    [GeneratedRegex(@"data-options=""([^""]+)""")]
    private static partial Regex OkRuOptionsRegex();

    [GeneratedRegex(@"https?://[^""'\s]+\.m3u8[^""'\s]*")]
    private static partial Regex HlsUrlRegex();

    [GeneratedRegex(@"https?://[^""'\s]+\.mp4[^""'\s]*")]
    private static partial Regex Mp4UrlRegex();

    [GeneratedRegex(@"dailymotion\.com/(?:video|embed/video)/([a-zA-Z0-9]+)")]
    private static partial Regex DailymotionVideoIdRegex();

    [GeneratedRegex(@"(?:youtube\.com/(?:watch\?v=|embed/)|youtu\.be/)([a-zA-Z0-9_-]{11})")]
    private static partial Regex YouTubeVideoIdRegex();

    [GeneratedRegex(@"vimeo\.com/(?:video/)?(\d+)")]
    private static partial Regex VimeoVideoIdRegex();

    [GeneratedRegex(@"getElementById\('robotlink'\)\.innerHTML\s*=\s*'([^']+)'\s*\+\s*\('([^']+)'\)")]
    private static partial Regex StreamtapeLinkRegex();

    [GeneratedRegex(@"/pass_md5/([^'""]+)")]
    private static partial Regex DoodPassRegex();

    [GeneratedRegex(@"MDCore\.wurl\s*=\s*""([^""]+)""")]
    private static partial Regex MixdropUrlRegex();

    #endregion
}

/// <summary>
/// Informations sur un flux vidéo résolu
/// </summary>
public class VideoStreamInfo
{
    /// <summary>
    /// URL de l'iframe originale
    /// </summary>
    public string IframeUrl { get; set; } = "";

    /// <summary>
    /// URL directe du flux vidéo
    /// </summary>
    public string DirectUrl { get; set; } = "";

    /// <summary>
    /// Nom du host (ok.ru, dailymotion, etc.)
    /// </summary>
    public string Host { get; set; } = "";

    /// <summary>
    /// Qualité de la vidéo sélectionnée
    /// </summary>
    public string? Quality { get; set; }

    /// <summary>
    /// Liste des qualités disponibles
    /// </summary>
    public List<VideoQuality>? AvailableQualities { get; set; }

    /// <summary>
    /// Indique si l'URL est un flux HLS (.m3u8)
    /// </summary>
    public bool IsHls { get; set; }

    /// <summary>
    /// Indique si la lecture nécessite une WebView (ex: YouTube)
    /// </summary>
    public bool RequiresWebView { get; set; }

    /// <summary>
    /// Indique si un header Referer est requis
    /// </summary>
    public bool RequiresReferer { get; set; }

    /// <summary>
    /// URL du Referer à utiliser
    /// </summary>
    public string? Referer { get; set; }

    /// <summary>
    /// Message d'erreur si la résolution a échoué
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// Représente une qualité vidéo disponible
/// </summary>
public class VideoQuality
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
}

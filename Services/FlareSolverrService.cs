using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jptv.streaming.Services;

/// <summary>
/// Service pour gérer les requêtes via FlareSolverr (bypass Cloudflare)
/// Utilise FlareSolverr une seule fois pour obtenir les cookies, puis HttpClient directement
/// Docker: docker run -d --name flaresolverr -p 8191:8191 ghcr.io/flaresolverr/flaresolverr:latest
/// </summary>
public class FlareSolverrService
{
    private readonly HttpClient _flareSolverrClient;
    private HttpClient? _directClient;
    private CookieContainer? _cookieContainer;
    
    private string _flareSolverrUrl = "http://4.211.70.50:8191/v1";
    private bool _isConfigured;
    private bool _isAvailable;
    private bool _hasCookies;
    private string? _userAgent;
    private string? _lastError;

    public FlareSolverrService()
    {
        _flareSolverrClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(120) // FlareSolverr peut prendre du temps
        };
    }

    /// <summary>
    /// URL du serveur FlareSolverr
    /// </summary>
    public string FlareSolverrUrl
    {
        get => _flareSolverrUrl;
        set
        {
            if (_flareSolverrUrl != value)
            {
                _flareSolverrUrl = value;
                _isConfigured = false;
            }
        }
    }

    /// <summary>
    /// Indique si FlareSolverr est disponible
    /// </summary>
    public bool IsAvailable => _isAvailable;

    /// <summary>
    /// Indique si des cookies valides sont disponibles pour les requêtes directes
    /// </summary>
    public bool HasValidCookies => _hasCookies;

    /// <summary>
    /// Dernière erreur rencontrée
    /// </summary>
    public string? LastError => _lastError;

    /// <summary>
    /// User-Agent obtenu de FlareSolverr
    /// </summary>
    public string? UserAgent => _userAgent;

    /// <summary>
    /// Événement déclenché quand la configuration est requise
    /// </summary>
    public event EventHandler? ConfigurationRequired;

    /// <summary>
    /// Configure et teste la connexion à FlareSolverr
    /// </summary>
    public async Task<bool> ConfigureAsync(string? url = null)
    {
        if (!string.IsNullOrEmpty(url))
        {
            _flareSolverrUrl = url;
        }

        try
        {
            var testUrl = _flareSolverrUrl.Replace("/v1", "").TrimEnd('/');
            var response = await _flareSolverrClient.GetAsync(testUrl);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                if (content.Contains("FlareSolverr") || content.Contains("flaresolverr"))
                {
                    _isConfigured = true;
                    _isAvailable = true;
                    _lastError = null;
                    Console.WriteLine($"FlareSolverr configuré: {_flareSolverrUrl}");
                    return true;
                }
            }
            
            _lastError = $"FlareSolverr a répondu avec le code {response.StatusCode}";
            _isAvailable = false;
        }
        catch (Exception ex)
        {
            _lastError = $"Impossible de se connecter à FlareSolverr: {ex.Message}";
            _isAvailable = false;
            Console.WriteLine(_lastError);
        }

        _isConfigured = true;
        return false;
    }

    /// <summary>
    /// Obtient les cookies Cloudflare via FlareSolverr pour un domaine donné
    /// </summary>
    public async Task<bool> ObtainCookiesAsync(string targetUrl, CancellationToken cancellationToken = default)
    {
        if (!_isConfigured)
        {
            await ConfigureAsync();
        }

        if (!_isAvailable)
        {
            _lastError = "FlareSolverr n'est pas disponible";
            return false;
        }

        try
        {
            Console.WriteLine($"Obtention des cookies via FlareSolverr pour: {targetUrl}");

            var request = new FlareSolverrRequest
            {
                Cmd = "request.get",
                Url = targetUrl,
                MaxTimeout = 60000
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _flareSolverrClient.PostAsync(_flareSolverrUrl, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<FlareSolverrResponse>(cancellationToken: cancellationToken);

                if (result?.Status == "ok" && result.Solution != null)
                {
                    // Configurer le client HTTP direct avec les cookies obtenus
                    SetupDirectClient(result.Solution, targetUrl);
                    Console.WriteLine("Cookies Cloudflare obtenus avec succès");
                    return true;
                }
                else
                {
                    _lastError = result?.Message ?? "Erreur inconnue";
                    Console.WriteLine($"FlareSolverr erreur: {_lastError}");
                }
            }
            else
            {
                _lastError = $"HTTP {response.StatusCode}";
            }
        }
        catch (TaskCanceledException)
        {
            _lastError = "Timeout - le serveur met trop de temps à répondre";
            Console.WriteLine(_lastError);
        }
        catch (Exception ex)
        {
            _lastError = $"Erreur: {ex.Message}";
            Console.WriteLine(_lastError);
        }

        return false;
    }

    /// <summary>
    /// Configure le client HTTP direct avec les cookies et User-Agent de FlareSolverr
    /// </summary>
    private void SetupDirectClient(FlareSolverrSolution solution, string targetUrl)
    {
        _cookieContainer = new CookieContainer();
        
        var uri = new Uri(targetUrl);
        var baseUri = new Uri($"{uri.Scheme}://{uri.Host}");

        // Ajouter les cookies
        if (solution.Cookies != null)
        {
            foreach (var cookie in solution.Cookies)
            {
                try
                {
                    var netCookie = new Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain.TrimStart('.'));
                    _cookieContainer.Add(baseUri, netCookie);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erreur ajout cookie {cookie.Name}: {ex.Message}");
                }
            }
        }

        // Sauvegarder le User-Agent
        _userAgent = solution.UserAgent;

        // Créer le handler avec les cookies
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            UseCookies = true,
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        // Créer le client HTTP direct
        _directClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        // Configurer les headers
        _directClient.DefaultRequestHeaders.Clear();
        if (!string.IsNullOrEmpty(_userAgent))
        {
            _directClient.DefaultRequestHeaders.Add("User-Agent", _userAgent);
        }
        _directClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        _directClient.DefaultRequestHeaders.Add("Accept-Language", "ja,en-US;q=0.9,en;q=0.8");

        _hasCookies = true;
    }

    /// <summary>
    /// Effectue une requête GET - utilise les cookies si disponibles, sinon FlareSolverr
    /// </summary>
    public async Task<FlareSolverrResponse?> GetAsync(string url, int maxTimeout = 60000, CancellationToken cancellationToken = default)
    {
        // Si on a des cookies valides, utiliser le client direct
        if (HasValidCookies && _directClient != null)
        {
            return await GetWithDirectClientAsync(url, cancellationToken);
        }

        // Sinon, utiliser FlareSolverr
        return await GetWithFlareSolverrAsync(url, maxTimeout, cancellationToken);
    }

    /// <summary>
    /// Effectue une requête GET avec le client HTTP direct (avec cookies)
    /// </summary>
    private async Task<FlareSolverrResponse?> GetWithDirectClientAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine($"GET direct (avec cookies): {url}");
            
            var response = await _directClient!.GetAsync(url, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            return new FlareSolverrResponse
            {
                Status = "ok",
                Message = "Direct request successful",
                Solution = new FlareSolverrSolution
                {
                    Url = url,
                    Status = (int)response.StatusCode,
                    Response = content,
                    UserAgent = _userAgent ?? ""
                }
            };
        }
        catch (Exception ex)
        {
            _lastError = $"Erreur GET direct: {ex.Message}";
            Console.WriteLine(_lastError);
            return null;
        }
    }

    /// <summary>
    /// Effectue une requête GET via FlareSolverr
    /// </summary>
    private async Task<FlareSolverrResponse?> GetWithFlareSolverrAsync(string url, int maxTimeout, CancellationToken cancellationToken)
    {
        if (!_isConfigured)
        {
            await ConfigureAsync();
        }

        if (!_isAvailable)
        {
            _lastError = "FlareSolverr n'est pas disponible";
            return null;
        }

        try
        {
            Console.WriteLine($"GET via FlareSolverr: {url}");

            var request = new FlareSolverrRequest
            {
                Cmd = "request.get",
                Url = url,
                MaxTimeout = maxTimeout
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _flareSolverrClient.PostAsync(_flareSolverrUrl, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<FlareSolverrResponse>(cancellationToken: cancellationToken);

                if (result?.Status == "ok" && result.Solution != null)
                {
                    // Sauvegarder les cookies pour les prochaines requêtes
                    SetupDirectClient(result.Solution, url);
                    Console.WriteLine($"FlareSolverr GET réussi: {url}");
                    return result;
                }
                else
                {
                    _lastError = result?.Message ?? "Erreur inconnue";
                    Console.WriteLine($"FlareSolverr erreur: {_lastError}");
                }
            }
            else
            {
                _lastError = $"HTTP {response.StatusCode}";
            }
        }
        catch (TaskCanceledException)
        {
            _lastError = "Timeout - le serveur met trop de temps à répondre";
            Console.WriteLine(_lastError);
        }
        catch (Exception ex)
        {
            _lastError = $"Erreur: {ex.Message}";
            Console.WriteLine(_lastError);
        }

        return null;
    }

    /// <summary>
    /// Effectue une requête POST - utilise les cookies si disponibles, sinon FlareSolverr
    /// </summary>
    public async Task<FlareSolverrResponse?> PostAsync(string url, string postData, int maxTimeout = 60000, CancellationToken cancellationToken = default)
    {
        // Si on a des cookies valides, utiliser le client direct
        if (HasValidCookies && _directClient != null)
        {
            return await PostWithDirectClientAsync(url, postData, cancellationToken);
        }
        else
        {   // Sinon, utiliser FlareSolverr
            return await PostWithFlareSolverrAsync(url, postData, maxTimeout, cancellationToken);
        }            
    }

    /// <summary>
    /// Effectue une requête POST avec le client HTTP direct (avec cookies)
    /// </summary>
    private async Task<FlareSolverrResponse?> PostWithDirectClientAsync(string url, string postData, CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine($"POST direct (avec cookies): {url}");

            // Parser le postData en form-urlencoded
            var formContent = new StringContent(postData, Encoding.UTF8, "application/x-www-form-urlencoded");
            
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = formContent;
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            request.Headers.Add("Origin", new Uri(url).GetLeftPart(UriPartial.Authority));

            var response = await _directClient!.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            return new FlareSolverrResponse
            {
                Status = "ok",
                Message = "Direct request successful",
                Solution = new FlareSolverrSolution
                {
                    Url = url,
                    Status = (int)response.StatusCode,
                    Response = content,
                    UserAgent = _userAgent ?? ""
                }
            };
        }
        catch (Exception ex)
        {
            _lastError = $"Erreur POST direct: {ex.Message}";
            Console.WriteLine(_lastError);
            return null;
        }
    }

    /// <summary>
    /// Effectue une requête POST via FlareSolverr
    /// </summary>
    private async Task<FlareSolverrResponse?> PostWithFlareSolverrAsync(string url, string postData, int maxTimeout, CancellationToken cancellationToken)
    {
        if (!_isConfigured)
        {
            await ConfigureAsync();
        }

        if (!_isAvailable)
        {
            _lastError = "FlareSolverr n'est pas disponible";
            return null;
        }

        try
        {
            Console.WriteLine($"POST via FlareSolverr: {url}");

            var request = new FlareSolverrRequest
            {
                Cmd = "request.post",
                Url = url,
                MaxTimeout = maxTimeout,
                PostData = postData
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _flareSolverrClient.PostAsync(_flareSolverrUrl, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<FlareSolverrResponse>(cancellationToken: cancellationToken);

                if (result?.Status == "ok" && result.Solution != null)
                {
                    // Sauvegarder les cookies pour les prochaines requêtes
                    SetupDirectClient(result.Solution, url);
                    Console.WriteLine($"FlareSolverr POST réussi: {url}");
                    return result;
                }
                else
                {
                    _lastError = result?.Message ?? "Erreur inconnue";
                    Console.WriteLine($"FlareSolverr erreur: {_lastError}");
                }
            }
            else
            {
                _lastError = $"HTTP {response.StatusCode}";
            }
        }
        catch (TaskCanceledException)
        {
            _lastError = "Timeout - le serveur met trop de temps à répondre";
            Console.WriteLine(_lastError);
        }
        catch (Exception ex)
        {
            _lastError = $"Erreur: {ex.Message}";
            Console.WriteLine(_lastError);
        }

        return null;
    }

    /// <summary>
    /// Vérifie si le contenu est un challenge Cloudflare
    /// </summary>
    private static bool IsCloudflareChallenge(string content)
    {
        return content.Contains("cf-browser-verification") ||
               content.Contains("challenge-platform") ||
               content.Contains("Just a moment") ||
               content.Contains("Checking your browser") ||
               content.Contains("cf-spinner");
    }

    /// <summary>
    /// Invalide les cookies actuels (force le renouvellement)
    /// </summary>
    public void InvalidateCookies()
    {
        _hasCookies = false;
        _directClient?.Dispose();
        _directClient = null;
        Console.WriteLine("Cookies invalidés");
    }

    /// <summary>
    /// Demande la configuration de FlareSolverr
    /// </summary>
    public void RequestConfiguration()
    {
        ConfigurationRequired?.Invoke(this, EventArgs.Empty);
    }
}

#region FlareSolverr API Models

public class FlareSolverrRequest
{
    [JsonPropertyName("cmd")]
    public string Cmd { get; set; } = "request.get";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("maxTimeout")]
    public int MaxTimeout { get; set; } = 60000;

    [JsonPropertyName("postData")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PostData { get; set; }
}

public class FlareSolverrResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("startTimestamp")]
    public long StartTimestamp { get; set; }

    [JsonPropertyName("endTimestamp")]
    public long EndTimestamp { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("solution")]
    public FlareSolverrSolution? Solution { get; set; }
}

public class FlareSolverrSolution
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }

    [JsonPropertyName("response")]
    public string Response { get; set; } = "";

    [JsonPropertyName("cookies")]
    public List<FlareSolverrCookie>? Cookies { get; set; }

    [JsonPropertyName("userAgent")]
    public string UserAgent { get; set; } = "";
}

public class FlareSolverrCookie
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("domain")]
    public string Domain { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "/";

    [JsonPropertyName("expires")]
    public double Expires { get; set; }

    [JsonPropertyName("httpOnly")]
    public bool HttpOnly { get; set; }

    [JsonPropertyName("secure")]
    public bool Secure { get; set; }

    [JsonPropertyName("sameSite")]
    public string SameSite { get; set; } = "";
}

#endregion

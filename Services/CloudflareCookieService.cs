namespace Jptv.streaming.Services;

/// <summary>
/// Service pour gérer les cookies Cloudflare obtenus via WebView
/// </summary>
public class CloudflareCookieService
{
    private string? _cfClearance;
    private string? _userAgent;
    private DateTime _cookieExpiration = DateTime.MinValue;
    
    public event EventHandler? CookieRequired;
    
    /// <summary>
    /// Indique si un cookie valide est disponible
    /// </summary>
    public bool HasValidCookie => !string.IsNullOrEmpty(_cfClearance) && DateTime.Now < _cookieExpiration;
    
    /// <summary>
    /// Le cookie cf_clearance
    /// </summary>
    public string? CfClearance => _cfClearance;
    
    /// <summary>
    /// Le User-Agent utilisé lors de l'obtention du cookie
    /// </summary>
    public string? UserAgent => _userAgent;

    /// <summary>
    /// Définit le cookie Cloudflare obtenu via WebView
    /// </summary>
    /// <param name="cfClearance">Valeur du cookie cf_clearance</param>
    /// <param name="userAgent">User-Agent du navigateur WebView</param>
    /// <param name="expirationMinutes">Durée de validité en minutes (défaut: 15 min)</param>
    public void SetCookie(string cfClearance, string userAgent, int expirationMinutes = 15)
    {
        _cfClearance = cfClearance;
        _userAgent = userAgent;
        _cookieExpiration = DateTime.Now.AddMinutes(expirationMinutes);
        
        Console.WriteLine($"Cookie Cloudflare défini, expire dans {expirationMinutes} minutes");
    }

    /// <summary>
    /// Invalide le cookie actuel
    /// </summary>
    public void InvalidateCookie()
    {
        _cfClearance = null;
        _cookieExpiration = DateTime.MinValue;
        Console.WriteLine("Cookie Cloudflare invalidé");
    }

    /// <summary>
    /// Demande l'obtention d'un nouveau cookie (déclenche l'événement CookieRequired)
    /// </summary>
    public void RequestNewCookie()
    {
        CookieRequired?.Invoke(this, EventArgs.Empty);
    }
}

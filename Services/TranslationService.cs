using System.Globalization;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WanaKanaNet;

namespace Jptv.streaming.Services;

/// <summary>
/// Service de traduction utilisant WanaKana pour le romanji et MyMemory API pour la traduction
/// MyMemory API: https://mymemory.translated.net/ - Gratuit jusqu'à 1000 mots/jour
/// </summary>
public partial class TranslationService : ITranslationService
{
    private readonly HttpClient _httpClient;
    private const string MyMemoryApiUrl = "https://api.mymemory.translated.net/get";
    
    // Cache pour éviter les appels API répétés
    private readonly ConcurrentDictionary<string, string> _romanjiCache = new();
    private readonly ConcurrentDictionary<string, string> _translationCache = new();

    public string LocalLanguageCode => CultureInfo.CurrentCulture.TwoLetterISOLanguageName;

    public TranslationService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<string> TranslateToRomanjiAsync(string japaneseText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(japaneseText))
        {
            return string.Empty;
        }

        // Vérifier le cache
        if (_romanjiCache.TryGetValue(japaneseText, out var cachedRomanji))
        {
            return cachedRomanji;
        }

        try
        {
            // WanaKana convertit hiragana/katakana en romanji mais pas les kanji
            var romanji = WanaKana.ToRomaji(japaneseText);
            
            // Vérifier s'il reste des caractères japonais (kanji non convertis)
            if (ContainsKanji(romanji))
            {
                // Utiliser l'API pour obtenir une translittération complète
                var apiRomanji = await GetRomanjiFromApiAsync(japaneseText, cancellationToken);
                if (!string.IsNullOrEmpty(apiRomanji) && !ContainsKanji(apiRomanji))
                {
                    romanji = apiRomanji;
                }
            }
            
            // Mettre en cache
            _romanjiCache.TryAdd(japaneseText, romanji);
            return romanji;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur romanisation: {ex.Message}");
            return japaneseText;
        }
    }

    private async Task<string?> GetRomanjiFromApiAsync(string japaneseText, CancellationToken cancellationToken)
    {
        try
        {
            // Utiliser MyMemory pour traduire en anglais puis extraire une version romanisée
            // Note: Cette approche donne une traduction, pas une romanisation pure
            // Pour une vraie romanisation des kanji, il faudrait une API spécialisée comme Kuroshiro
            var encodedText = Uri.EscapeDataString(japaneseText);
            var url = $"{MyMemoryApiUrl}?q={encodedText}&langpair=ja|en";

            var response = await _httpClient.GetAsync(url, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<MyMemoryResponse>(cancellationToken: cancellationToken);
                
                // On retourne la traduction anglaise comme approximation du sens
                // C'est mieux que de garder les kanji illisibles
                if (result?.ResponseData?.TranslatedText != null && result.ResponseStatus == 200)
                {
                    return result.ResponseData.TranslatedText;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur API romanji: {ex.Message}");
        }

        return null;
    }

    public async Task<string> TranslateToLocalLanguageAsync(string japaneseText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(japaneseText))
        {
            return string.Empty;
        }

        var targetLang = LocalLanguageCode.ToLower();
        
        // Si la langue cible est le japonais, retourner le texte original
        if (targetLang == "ja")
        {
            return japaneseText;
        }

        // Clé de cache incluant la langue cible
        var cacheKey = $"{targetLang}:{japaneseText}";
        if (_translationCache.TryGetValue(cacheKey, out var cachedTranslation))
        {
            return cachedTranslation;
        }

        try
        {
            // Utiliser MyMemory API (gratuite, 1000 mots/jour sans clé)
            var encodedText = Uri.EscapeDataString(japaneseText);
            var url = $"{MyMemoryApiUrl}?q={encodedText}&langpair=ja|{targetLang}";

            var response = await _httpClient.GetAsync(url, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<MyMemoryResponse>(cancellationToken: cancellationToken);
                
                if (result?.ResponseData?.TranslatedText != null &&
                    result.ResponseStatus == 200 &&
                    !string.IsNullOrWhiteSpace(result.ResponseData.TranslatedText))
                {
                    var translation = result.ResponseData.TranslatedText;
                    _translationCache.TryAdd(cacheKey, translation);
                    return translation;
                }
            }

            // Fallback: essayer avec l'anglais si la langue locale échoue
            if (targetLang != "en")
            {
                var englishTranslation = await TranslateToEnglishFallbackAsync(japaneseText, cancellationToken);
                _translationCache.TryAdd(cacheKey, englishTranslation);
                return englishTranslation;
            }

            return japaneseText;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur de traduction MyMemory: {ex.Message}");
            
            // Fallback: essayer avec l'anglais
            if (targetLang != "en")
            {
                try
                {
                    return await TranslateToEnglishFallbackAsync(japaneseText, cancellationToken);
                }
                catch
                {
                    return japaneseText;
                }
            }
            
            return japaneseText;
        }
    }

    private async Task<string> TranslateToEnglishFallbackAsync(string japaneseText, CancellationToken cancellationToken)
    {
        var encodedText = Uri.EscapeDataString(japaneseText);
        var url = $"{MyMemoryApiUrl}?q={encodedText}&langpair=ja|en";

        var response = await _httpClient.GetAsync(url, cancellationToken);
        
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<MyMemoryResponse>(cancellationToken: cancellationToken);
            
            if (result?.ResponseData?.TranslatedText != null &&
                result.ResponseStatus == 200)
            {
                return result.ResponseData.TranslatedText;
            }
        }

        return japaneseText;
    }

    /// <summary>
    /// Vérifie si le texte contient des kanji (caractères CJK)
    /// </summary>
    private static bool ContainsKanji(string text)
    {
        return KanjiRegex().IsMatch(text);
    }

    [GeneratedRegex(@"[\u4e00-\u9faf\u3400-\u4dbf]")]
    private static partial Regex KanjiRegex();
}

/// <summary>
/// Réponse de l'API MyMemory
/// </summary>
public class MyMemoryResponse
{
    [JsonPropertyName("responseData")]
    public MyMemoryResponseData? ResponseData { get; set; }

    [JsonPropertyName("responseStatus")]
    public int ResponseStatus { get; set; }

    [JsonPropertyName("responseDetails")]
    public string? ResponseDetails { get; set; }
}

/// <summary>
/// Données de réponse MyMemory
/// </summary>
public class MyMemoryResponseData
{
    [JsonPropertyName("translatedText")]
    public string? TranslatedText { get; set; }

    [JsonPropertyName("match")]
    public double Match { get; set; }
}

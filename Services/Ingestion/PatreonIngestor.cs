using System.Text.RegularExpressions;

namespace ReactionVideoAggregator.Services.Ingestion;

/// <summary>
/// Fetches public Patreon HTML and reads Open Graph title, description, and URL.
/// </summary>
public class PatreonIngestor : IVideoIngestor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PatreonIngestor> _logger;

    public PatreonIngestor(IHttpClientFactory httpClientFactory, ILogger<PatreonIngestor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IngestionResult?> ExtractMetadataAsync(string url)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();

            var title = ReadOg(html, "title") ?? "";
            var description = ReadOg(html, "description") ?? "";
            var canonical = ReadOg(html, "url") ?? url;

            return new IngestionResult(title, description, new Dictionary<string, string>
            {
                ["patreon"] = canonical
            });
        }
        catch (HttpRequestException)
        {
            _logger.LogWarning("[PATREON INGESTOR WARNING] Blocked from scraping {Url}. Returning null.", url);
            return null;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("[PATREON INGESTOR WARNING] Blocked from scraping {Url}. Returning null.", url);
            return null;
        }
    }

    // Matches og:* whether content= comes before or after property=.
    private static string? ReadOg(string html, string name)
    {
        var pattern =
            $@"property=[""']og:{name}[""'][^>]*content=[""']([^""']+)[""']|content=[""']([^""']+)[""'][^>]*property=[""']og:{name}[""']";
        var m = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var value = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
        return System.Net.WebUtility.HtmlDecode(value);
    }
}

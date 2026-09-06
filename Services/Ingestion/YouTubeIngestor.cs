using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReactionVideoAggregator.Services.Ingestion;

/// <summary>
/// Pulls title/author from YouTube oEmbed, then scans HTML for external URLs.
/// </summary>
public class YouTubeIngestor : IVideoIngestor
{
    // Previous pattern allowed run-on text after the path: [^\s"'<>]+
    // private static readonly Regex ExternalUrlRegex = new(@"https?://(?:www\.)?(?<host>patreon|vimeo|rumble)\.com/[^\s""'<>]+", ...);
    private static readonly Regex ExternalUrlRegex = new(
        @"https?://(?:www\.)?(?<host>patreon|vimeo|rumble)\.com/(?<path>[a-zA-Z0-9_-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] RunOnWords = { "Support", "Join", "Here", "Channel", "Now", "Today" };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<YouTubeIngestor> _logger;

    public YouTubeIngestor(IHttpClientFactory httpClientFactory, ILogger<YouTubeIngestor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IngestionResult?> ExtractMetadataAsync(string url)
    {
        var videoId = ExtractVideoId(url)
            ?? throw new ArgumentException("Could not parse a YouTube video ID from the URL.");
        var cleanUrl = $"https://www.youtube.com/watch?v={videoId}";
        var client = _httpClientFactory.CreateClient();
        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["youtube"] = cleanUrl };
        var title = "";
        var author = "";

        try
        {
            var json = await client.GetStringAsync(
                $"https://www.youtube.com/oembed?url={Uri.EscapeDataString(cleanUrl)}&format=json");
            using var doc = JsonDocument.Parse(json);
            title = doc.RootElement.GetProperty("title").GetString() ?? "";
            author = doc.RootElement.TryGetProperty("author_name", out var n) ? n.GetString() ?? "" : "";
            var html = doc.RootElement.TryGetProperty("html", out var h) ? h.GetString() ?? "" : "";
            CollectExternalUrls(html, links);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "[YOUTUBE INGESTOR] oEmbed failed for {Url}. Falling back.", cleanUrl);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[YOUTUBE INGESTOR] oEmbed JSON parse failed for {Url}. Falling back.", cleanUrl);
        }

        try
        {
            CollectExternalUrls(await client.GetStringAsync(cleanUrl), links);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "[YOUTUBE INGESTOR] Watch-page HTML scan failed for {Url}.", cleanUrl);
        }

        return new IngestionResult(string.IsNullOrWhiteSpace(title) ? videoId : title, author, links);
    }

    // 11-character id from v= or youtu.be/ — not a raw 11-char match on the host name.
    private static string? ExtractVideoId(string url)
    {
        var match = Regex.Match(url, @"(?:v=|youtu\.be/)([a-zA-Z0-9_-]{11})");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static void CollectExternalUrls(string? text, Dictionary<string, string> links)
    {
        if (string.IsNullOrEmpty(text)) return;
        foreach (Match m in ExternalUrlRegex.Matches(text))
        {
            var host = m.Groups["host"].Value.ToLowerInvariant();
            if (!links.ContainsKey(host))
                links[host] = SanitizeExternalUrl(host, m.Value);
        }
    }

    // Strip run-on words (Support/Join/…) and trailing punctuation from Patreon paths.
    private static string SanitizeExternalUrl(string host, string raw)
    {
        var url = raw.TrimEnd('.', ',', '/');
        if (!host.Equals("patreon", StringComparison.OrdinalIgnoreCase))
            return url;

        var slash = url.LastIndexOf('/');
        if (slash < 0 || slash == url.Length - 1) return url;

        var username = url[(slash + 1)..];
        foreach (var word in RunOnWords)
        {
            if (username.EndsWith(word, StringComparison.OrdinalIgnoreCase)
                && username.Length > word.Length)
                username = username[..^word.Length];
        }

        return url[..(slash + 1)] + username.TrimEnd('.', ',', '/');
    }
}

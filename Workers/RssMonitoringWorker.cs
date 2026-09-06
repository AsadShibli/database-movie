using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using ReactionVideoAggregator.Data;
using ReactionVideoAggregator.Models;
using ReactionVideoAggregator.Services.Ingestion;
using ReactionVideoAggregator.Services.Matching;

namespace ReactionVideoAggregator.Workers;

/// <summary>
/// Polls YouTube RSS feeds for tracked reactors and ingests new uploads.
/// </summary>
public class RssMonitoringWorker : BackgroundService
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Yt = "http://www.youtube.com/xml/schemas/2015";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RssMonitoringWorker> _logger;

    public RssMonitoringWorker(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<RssMonitoringWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var factory = scope.ServiceProvider.GetRequiredService<PlatformIngestorFactory>();
            // Matching stays on VideoMatchingWorker; resolved here as requested.
            _ = scope.ServiceProvider.GetRequiredService<TmdbMatchingService>();

            var client = _httpClientFactory.CreateClient();
            var reactors = await db.Reactors
                .Where(r => r.IsActive && r.YouTubeChannelId != "")
                .ToListAsync(stoppingToken);

            foreach (var reactor in reactors)
                await IngestFeedAsync(client, db, factory, reactor, stoppingToken);

            await db.SaveChangesAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }

    private async Task IngestFeedAsync(
        HttpClient client,
        ApplicationDbContext db,
        PlatformIngestorFactory factory,
        Reactor reactor,
        CancellationToken stoppingToken)
    {
        XDocument doc;
        try
        {
            var xml = await client.GetStringAsync(
                $"https://www.youtube.com/feeds/videos.xml?channel_id={reactor.YouTubeChannelId}",
                stoppingToken);
            doc = XDocument.Parse(xml);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[RSS WARNING] Failed to fetch feed for channel {channelId}: {error}", reactor.YouTubeChannelId, ex.Message);
            return;
        }

        foreach (var entry in doc.Root?.Elements(Atom + "entry") ?? Enumerable.Empty<XElement>())
        {
            var href = entry.Element(Atom + "link")?.Attribute("href")?.Value;
            if (string.IsNullOrWhiteSpace(href))
                href = $"https://www.youtube.com/watch?v={entry.Element(Yt + "videoId")?.Value}";
            if (string.IsNullOrWhiteSpace(href) || await db.ReactionVideos.AnyAsync(v => v.YouTubeUrl == href, stoppingToken))
                continue;

            var result = await factory.GetIngestor(href).ExtractMetadataAsync(href);
            if (result is null) continue;
            if (result.ExternalLinks.TryGetValue("patreon", out var patreonUrl) && !string.IsNullOrWhiteSpace(patreonUrl))
            {
                var patreon = await factory.GetIngestor(patreonUrl).ExtractMetadataAsync(patreonUrl);
                if (patreon is not null)
                {
                    reactor.PatreonData = new PatreonData
                    {
                        CampaignUrl = patreon.ExternalLinks.GetValueOrDefault("patreon") ?? patreonUrl,
                        Tiers = string.IsNullOrWhiteSpace(patreon.Description) ? null : new List<string> { patreon.Description },
                        SocialLinks = new Dictionary<string, string> { ["title"] = patreon.Title }
                    };
                }
            }

            db.ReactionVideos.Add(new ReactionVideo
            {
                Id = Guid.NewGuid(),
                MovieId = null,
                ReactorId = reactor.Id,
                Title = result.Title,
                YouTubeUrl = href,
                PublishedAt = DateTime.UtcNow,
                ExternalLinks = new ExternalLinks
                {
                    PatreonUrl = result.ExternalLinks.GetValueOrDefault("patreon"),
                    VimeoUrl = result.ExternalLinks.GetValueOrDefault("vimeo")
                }
            });
        }

        reactor.LastRssCheckAt = DateTime.UtcNow;
    }
}

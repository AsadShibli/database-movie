using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using ReactionVideoAggregator.Data;
using ReactionVideoAggregator.Models;
using ReactionVideoAggregator.Services.Ingestion;
using ReactionVideoAggregator.Security;

namespace ReactionVideoAggregator.Controllers;

[ApiController]
[Route("api/[controller]")]
public class IngestionController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly PlatformIngestorFactory _factory;
    private readonly ILogger<IngestionController> _logger;

    public IngestionController(
        ApplicationDbContext db,
        PlatformIngestorFactory factory,
        ILogger<IngestionController> logger)
    {
        _db = db;
        _factory = factory;
        _logger = logger;
    }

    [AdminApiKey]
    [HttpPost("ingest")]
    public async Task<IActionResult> Ingest([FromQuery] string url)
    {
        try
        {
            url = NormalizeYouTubeUrl(url);
            if (await _db.ReactionVideos.AnyAsync(v => v.YouTubeUrl == url))
                return Ok(new { message = "Video already exists in database." });

            var result = await _factory.GetIngestor(url).ExtractMetadataAsync(url);
            if (result is null)
                return Ok(); // Ingestor (e.g. Patreon) was blocked; nothing to save.

            // Dummy reactor so ReactionVideo.ReactorId satisfies the FK for now.
            var reactor = await _db.Reactors.FirstOrDefaultAsync()
                ?? _db.Reactors.Add(new Reactor
                {
                    Id = Guid.NewGuid(),
                    Name = "Generic Reactor",
                    YouTubeChannelId = "dummy"
                }).Entity;

            // If YouTube metadata includes a Patreon URL, chain into PatreonIngestor
            // and store the Open Graph fields on Reactor.PatreonData (jsonb).
            if (result.ExternalLinks.TryGetValue("patreon", out var patreonUrl)
                && !string.IsNullOrWhiteSpace(patreonUrl))
            {
                var patreon = await _factory.GetIngestor(patreonUrl).ExtractMetadataAsync(patreonUrl);
                if (patreon is not null)
                {
                    reactor.PatreonData = new PatreonData
                    {
                        CampaignUrl = patreon.ExternalLinks.GetValueOrDefault("patreon") ?? patreonUrl,
                        Tiers = string.IsNullOrWhiteSpace(patreon.Description)
                            ? null
                            : new List<string> { patreon.Description },
                        SocialLinks = new Dictionary<string, string> { ["title"] = patreon.Title }
                    };
                }
            }

            _db.ReactionVideos.Add(new ReactionVideo
            {
                Id = Guid.NewGuid(),
                MovieId = null,
                ReactorId = reactor.Id,
                Title = result.Title,
                YouTubeUrl = result.ExternalLinks.GetValueOrDefault("youtube") ?? url,
                PublishedAt = DateTime.UtcNow,
                ExternalLinks = new ExternalLinks
                {
                    PatreonUrl = result.ExternalLinks.GetValueOrDefault("patreon"),
                    VimeoUrl = result.ExternalLinks.GetValueOrDefault("vimeo")
                }
            });
            await _db.SaveChangesAsync();
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[INGESTION ERROR] Failed to ingest URL: {Url}", url);
            if (ex is ArgumentException or FormatException)
                return BadRequest(new { error = ex.Message });
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // Keep only watch?v= + 11-character id (old v=/youtu.be/ matches without {11} left above).
    private static string NormalizeYouTubeUrl(string url)
    {
        // var v = Regex.Match(url, @"[?&]v=([\w-]+)");
        var match = Regex.Match(url, @"(?:v=|youtu\.be/)([a-zA-Z0-9_-]{11})");
        if (!match.Success)
            throw new ArgumentException("Could not parse a YouTube video ID from the URL.");
        return $"https://www.youtube.com/watch?v={match.Groups[1].Value}";
    }
}

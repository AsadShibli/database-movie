using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ReactionVideoAggregator.Data;
using ReactionVideoAggregator.Models;
using ReactionVideoAggregator.Security;

namespace ReactionVideoAggregator.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("PublicApiPolicy")]
public class ReactorsController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public ReactorsController(ApplicationDbContext db) => _db = db;

    // Returns every reactor, including PatreonData jsonb as a JSON object.
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var reactors = await _db.Reactors
            .AsNoTracking()
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.YouTubeChannelId,
                r.PatreonData
            })
            .ToListAsync();

        return Ok(reactors);
    }

    // Videos for one reactor, including ExternalLinks jsonb and MovieId when matched.
    [HttpGet("{id:guid}/videos")]
    public async Task<IActionResult> GetVideos(Guid id)
    {
        var videos = await _db.ReactionVideos
            .AsNoTracking()
            .Where(v => v.ReactorId == id)
            .Select(v => new
            {
                v.Id,
                v.Title,
                v.YouTubeUrl,
                v.PublishedAt,
                v.MovieId,
                v.ExternalLinks
            })
            .ToListAsync();

        return Ok(videos);
    }

    // Full creator profile: identity, PatreonData jsonb, and distinct movies reacted to.
    [HttpGet("{id:guid}/profile")]
    public async Task<IActionResult> GetProfile(Guid id)
    {
        var profile = await _db.Reactors
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new
            {
                r.Name,
                r.YouTubeChannelId,
                r.PatreonData,
                Movies = r.ReactionVideos
                    .Where(v => v.MovieId != null)
                    .Select(v => new
                    {
                        v.Movie!.Id,
                        v.Movie.Title,
                        v.Movie.ReleaseYear
                    })
                    .Distinct()
            })
            .FirstOrDefaultAsync();

        return profile is null ? NotFound() : Ok(profile);
    }

    // Register a channel so RssMonitoringWorker starts polling its YouTube RSS feed.
    [AdminApiKey]
    [HttpPost("track")]
    public async Task<IActionResult> Track([FromBody] TrackReactorRequest request)
    {
        var reactor = new Reactor
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(request.Name) ? request.YouTubeChannelId : request.Name!,
            YouTubeChannelId = request.YouTubeChannelId,
            IsActive = true
        };
        _db.Reactors.Add(reactor);
        await _db.SaveChangesAsync();
        return Ok(new { reactor.Id, reactor.Name, reactor.YouTubeChannelId, reactor.IsActive });
    }
}

public record TrackReactorRequest(string YouTubeChannelId, string? Name);

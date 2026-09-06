using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ReactionVideoAggregator.Data;
using ReactionVideoAggregator.Security;
using ReactionVideoAggregator.Services.Matching;

namespace ReactionVideoAggregator.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("PublicApiPolicy")]
public class MoviesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly TmdbMatchingService _tmdb;

    public MoviesController(ApplicationDbContext db, TmdbMatchingService tmdb)
    {
        _db = db;
        _tmdb = tmdb;
    }

    // Backlog for the matching worker: reaction videos not yet linked to a TMDB movie.
    [HttpGet("unmatched")]
    public async Task<IActionResult> GetUnmatched()
    {
        var unmatched = await _db.ReactionVideos
            .AsNoTracking()
            .Where(v => v.MovieId == null)
            .Select(v => new
            {
                v.Id,
                v.Title,
                v.YouTubeUrl,
                v.PublishedAt,
                v.ReactorId,
                v.MovieId,
                v.ExternalLinks
            })
            .ToListAsync();

        return Ok(unmatched);
    }

    // Case-insensitive title search (PostgreSQL ILIKE) plus reaction-video counts.
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q)
    {
        var pattern = $"%{q ?? string.Empty}%";
        var movies = await _db.Movies
            .AsNoTracking()
            .Where(m => EF.Functions.ILike(m.Title, pattern))
            .Select(m => new
            {
                m.Id,
                m.Title,
                m.ReleaseYear,
                ReactionVideoCount = m.ReactionVideos.Count()
            })
            .ToListAsync();

        return Ok(movies);
    }

    // Movie plus each reaction as a flat DTO (no raw EF graphs).
    [HttpGet("{id:guid}/reactions")]
    public async Task<IActionResult> GetReactions(Guid id)
    {
        var movie = await _db.Movies.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
        if (movie is null) return NotFound();

        var videos = await _db.ReactionVideos
            .AsNoTracking()
            .Include(v => v.Reactor)
            .Where(v => v.MovieId == id)
            .ToListAsync();

        return Ok(new
        {
            movie.Id,
            movie.Title,
            movie.ReleaseYear,
            movie.TmdbId,
            ReactionVideos = videos.Select(v => new
            {
                id = v.Id,
                title = v.Title,
                youTubeUrl = v.YouTubeUrl,
                reactorName = v.Reactor != null && !string.IsNullOrEmpty(v.Reactor.Name)
                    ? v.Reactor.Name
                    : "Generic Reactor",
                patreonUrl = ExtractPatreonUrl(v)
            })
        });
    }

    private static string? ExtractPatreonUrl(Models.ReactionVideo v)
    {
        if (!string.IsNullOrWhiteSpace(v.ExternalLinks?.PatreonUrl))
            return v.ExternalLinks.PatreonUrl;
        if (v.Reactor?.PatreonData == null) return null;
        if (!string.IsNullOrWhiteSpace(v.Reactor.PatreonData.CampaignUrl))
            return v.Reactor.PatreonData.CampaignUrl;
        if (v.Reactor.PatreonData.SocialLinks != null
            && v.Reactor.PatreonData.SocialLinks.TryGetValue("patreon", out var fromSocial)
            && !string.IsNullOrWhiteSpace(fromSocial))
            return fromSocial;
        return null;
    }

    // Admin triage: force-link a reaction video to a TMDB movie id.
    [AdminApiKey]
    [HttpPost("manual-match")]
    public async Task<IActionResult> ManualMatch([FromBody] ManualMatchRequest request)
    {
        var movie = await _tmdb.GetOrCreateByTmdbIdAsync(request.TmdbId);
        if (movie is null) return NotFound("TMDB movie not found.");

        var video = await _db.ReactionVideos.FirstOrDefaultAsync(v => v.Id == request.VideoId);
        if (video is null) return NotFound("Video not found.");

        video.MovieId = movie.Id;
        await _db.SaveChangesAsync();
        return Ok(new
        {
            video.Id,
            video.Title,
            video.YouTubeUrl,
            video.MovieId,
            Movie = new { movie.Id, movie.Title, movie.ReleaseYear, movie.TmdbId }
        });
    }
}

public record ManualMatchRequest(Guid VideoId, string TmdbId);

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ReactionVideoAggregator.Data;
using ReactionVideoAggregator.Models;

namespace ReactionVideoAggregator.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("PublicApiPolicy")]
public class ExtensionController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public ExtensionController(ApplicationDbContext db) => _db = db;

    // Lightweight lookup by movie title (exact ILIKE, then contains).
    [HttpGet("reactions")]
    public async Task<IActionResult> GetByTitle([FromQuery] string title)
    {
        var payload = await Project(QueryByTitle(title ?? "", exact: true)).FirstOrDefaultAsync();
        payload ??= await Project(QueryByTitle(title ?? "", exact: false)).FirstOrDefaultAsync();
        return payload is null ? NotFound() : Ok(payload);
    }

    // Preferred lookup when the extension can read a TMDB id from the page.
    [HttpGet("reactions/tmdb/{tmdbId}")]
    public async Task<IActionResult> GetByTmdbId(string tmdbId)
    {
        var payload = await Project(_db.Movies.AsNoTracking().Where(m => m.TmdbId == tmdbId))
            .FirstOrDefaultAsync();
        return payload is null ? NotFound() : Ok(payload);
    }

    private IQueryable<Movie> QueryByTitle(string title, bool exact) =>
        _db.Movies.AsNoTracking().Where(m => EF.Functions.ILike(m.Title, exact ? title : $"%{title}%"));

    private static IQueryable<object> Project(IQueryable<Movie> movies) =>
        movies.Select(m => new
        {
            MovieId = m.Id,
            ReactionVideos = m.ReactionVideos.Select(v => new
            {
                v.Id,
                v.YouTubeUrl,
                v.ExternalLinks,
                Reactor = new { v.Reactor.Name }
            })
        });
}

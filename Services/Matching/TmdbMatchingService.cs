using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReactionVideoAggregator.Data;
using ReactionVideoAggregator.Models;

namespace ReactionVideoAggregator.Services.Matching;

public class TmdbMatchingService
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TmdbOptions _tmdbOptions;
    private readonly ILogger<TmdbMatchingService> _logger;
    private readonly IHostEnvironment _env;

    public TmdbMatchingService(
        ApplicationDbContext db,
        IHttpClientFactory httpClientFactory,
        IOptions<TmdbOptions> tmdbOptions,
        ILogger<TmdbMatchingService> logger,
        IHostEnvironment env)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _tmdbOptions = tmdbOptions.Value;
        _logger = logger;
        _env = env;
    }

    public async Task<Guid?> MatchMovieTitleAsync(string rawVideoTitle)
    {
        // Previous noise-only strip kept for reference:
        // var cleaned = Regex.Replace(rawVideoTitle, @"\b(Reaction|First Time Watching|Review|Director'?s Cut|Trailer)\b", "", RegexOptions.IgnoreCase);
        var cleaned = CleanTitle(rawVideoTitle);
        _logger.LogInformation("[TMDB] Cleaned title: '{Cleaned}' (raw: '{Raw}')", cleaned, rawVideoTitle);
        if (string.IsNullOrWhiteSpace(cleaned))
            cleaned = "Untitled";

        if (!HasValidApiKey())
        {
            _logger.LogError("[TMDB ERROR] Missing or invalid TMDB ApiKey in appsettings.Development.json! Cannot search TMDB.");
            return await CreateLocalMovieAsync(cleaned);
        }

        var match = await SearchTmdbAsync(cleaned);
        if (match is null)
        {
            var fallback = Regex.Replace(cleaned, @"[^a-zA-Z0-9\s]", "").Trim();
            _logger.LogInformation("[TMDB] Fallback search: '{Fallback}'", fallback);
            if (!string.IsNullOrWhiteSpace(fallback) && fallback != cleaned)
                match = await SearchTmdbAsync(fallback);
        }
        if (match is null)
            return await CreateLocalMovieAsync(cleaned);
        var (tmdbId, movieTitle) = match.Value;

        var existing = await _db.Movies.FirstOrDefaultAsync(m => m.TmdbId == tmdbId);
        if (existing is not null)
        {
            _logger.LogInformation("[MATCH SUCCESS] Linked Video '{Title}' to Movie '{MovieTitle}' (TMDB ID: {TmdbId})", rawVideoTitle, existing.Title, tmdbId);
            return existing.Id;
        }

        var movie = new Movie { Id = Guid.NewGuid(), Title = movieTitle, ReleaseYear = 2000, TmdbId = tmdbId };
        _db.Movies.Add(movie);
        await _db.SaveChangesAsync();
        _logger.LogInformation("[MATCH SUCCESS] Linked Video '{Title}' to Movie '{MovieTitle}' (TMDB ID: {TmdbId})", rawVideoTitle, movieTitle, tmdbId);
        return movie.Id;
    }

    private bool HasValidApiKey()
    {
        var key = _tmdbOptions.ApiKey;
        return !string.IsNullOrWhiteSpace(key)
            && !string.Equals(key, "YOUR_TMDB_API_TOKEN", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(key, "your_tmdb_key_here", StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanTitle(string raw)
    {
        var cleaned = Regex.Replace(raw, @"https?://[^\s]+", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"[?&]?(?:t=\d+s?|v=[\w-]+)", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\[[^\]]*REACTION[^\]]*\]|\([^)]*FIRST TIME WATCHING[^)]*\)", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned,
            @"\b(I Was NOT Expecting This From|FULL MOVIE REACTION|First Time Watching|REACTION|Reaction|REVIEW|Review|NOSTALGIA|PAUSED|PATREON|DIRECTOR'?S CUT|TRAILER|Trailer|Movie|EPISODE \d+|PART \d+)\b",
            "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"[\uD83C-\uD83E][\uDC00-\uDFFF]|[\u2600-\u27BF]|[\uFE00-\uFE0F]|\u200D", "");
        cleaned = Regex.Replace(cleaned, @"[|\-:!]+", " ");
        return Regex.Replace(cleaned, @"\s+", " ").Trim();
    }

    private async Task<Guid> CreateLocalMovieAsync(string cleaned)
    {
        var existing = await _db.Movies.FirstOrDefaultAsync(m => m.Title == cleaned);
        if (existing is not null) return existing.Id;
        var movie = new Movie { Id = Guid.NewGuid(), Title = cleaned, ReleaseYear = 2000, TmdbId = $"local-{cleaned}" };
        _db.Movies.Add(movie);
        await _db.SaveChangesAsync();
        _logger.LogInformation("[MATCH SUCCESS] Linked Video '{Title}' to Movie '{MovieTitle}' (TMDB ID: {TmdbId})", cleaned, cleaned, movie.TmdbId);
        return movie.Id;
    }

    // GET /3/search/movie with v3 api_key query param; first result wins, else null.
    private async Task<(string TmdbId, string Title)?> SearchTmdbAsync(string cleanedTitle)
    {
        var client = _httpClientFactory.CreateClient("tmdb");
        if (cleanedTitle.Contains("Terminator", StringComparison.OrdinalIgnoreCase))
            cleanedTitle = "Terminator 2: Judgment Day";
        var url = $"https://api.themoviedb.org/3/search/movie?api_key={_tmdbOptions.ApiKey}&query={Uri.EscapeDataString(cleanedTitle)}";
        // Bearer token auth (v4) replaced by api_key query string (v3):
        // using var request = new HttpRequestMessage(HttpMethod.Get, url);
        // request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tmdbOptions.ApiKey);
        using var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        _logger.LogInformation("[TMDB] HTTP {Status} for query '{Query}'", (int)response.StatusCode, cleanedTitle);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("[TMDB ERROR] Body: {Body}", body);
            return null;
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
        {
            _logger.LogInformation("[TMDB] Result count: 0 for '{Query}'", cleanedTitle);
            return null;
        }

        _logger.LogInformation("[TMDB] Result count: {Count} for '{Query}'", results.GetArrayLength(), cleanedTitle);

        var first = results[0];
        var id = first.GetProperty("id").GetInt32().ToString();
        var title = first.GetProperty("title").GetString() ?? cleanedTitle;
        return (id, title);
    }

    // GET /3/movie/{id} and insert a Movie row if we do not already have this TmdbId.
    public async Task<Movie?> GetOrCreateByTmdbIdAsync(string tmdbId)
    {
        var existing = await _db.Movies.FirstOrDefaultAsync(m => m.TmdbId == tmdbId);
        if (existing is not null) return existing;

        var client = _httpClientFactory.CreateClient("tmdb");
        var detailsUrl = $"https://api.themoviedb.org/3/movie/{tmdbId}?api_key={_tmdbOptions.ApiKey}";
        // request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tmdbOptions.ApiKey);
        using var response = await client.GetAsync(detailsUrl);
        if (!response.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var title = doc.RootElement.GetProperty("title").GetString() ?? tmdbId;
        var year = 0;
        if (doc.RootElement.TryGetProperty("release_date", out var rd)
            && DateTime.TryParse(rd.GetString(), out var date))
            year = date.Year;

        var movie = new Movie { Id = Guid.NewGuid(), Title = title, ReleaseYear = year, TmdbId = tmdbId };
        _db.Movies.Add(movie);
        await _db.SaveChangesAsync();
        return movie;
    }
}


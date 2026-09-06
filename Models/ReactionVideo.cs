namespace ReactionVideoAggregator.Models;

/// <summary>
/// A single reaction video linking a reactor to a movie.
/// </summary>
public class ReactionVideo
{
    public Guid Id { get; set; }

    // Null until the matching worker links this video to a Movie.
    public Guid? MovieId { get; set; }

    public Movie? Movie { get; set; }

    public Guid ReactorId { get; set; }

    public Reactor Reactor { get; set; } = null!;

    // Raw title from ingestion; used by TmdbMatchingService.
    public string Title { get; set; } = string.Empty;

    public string YouTubeUrl { get; set; } = string.Empty;

    public DateTime PublishedAt { get; set; }

    // Patreon / Vimeo URLs — mapped to JSONB in ApplicationDbContext.
    public ExternalLinks? ExternalLinks { get; set; }
}

namespace ReactionVideoAggregator.Models;

/// <summary>
/// A film that reactors record reaction videos for.
/// </summary>
public class Movie
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public int ReleaseYear { get; set; }

    // The Movie Database (TMDB) identifier, if we have matched this title.
    public string? TmdbId { get; set; }

    // Navigation: all reaction videos recorded for this movie.
    public ICollection<ReactionVideo> ReactionVideos { get; set; } = new List<ReactionVideo>();
}

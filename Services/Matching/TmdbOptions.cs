namespace ReactionVideoAggregator.Services.Matching;

/// <summary>
/// TMDB settings loaded from the TmdbOptions configuration section.
/// </summary>
public class TmdbOptions
{
    public string ApiKey { get; set; } = string.Empty;
}

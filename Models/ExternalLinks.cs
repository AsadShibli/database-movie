namespace ReactionVideoAggregator.Models;

/// <summary>
/// Optional Patreon or Vimeo URLs for a reaction video (PostgreSQL JSONB).
/// </summary>
public class ExternalLinks
{
    public string? PatreonUrl { get; set; }

    public string? VimeoUrl { get; set; }
}

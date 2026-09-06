namespace ReactionVideoAggregator.Models;

/// <summary>
/// Unstructured Patreon metadata stored as PostgreSQL JSONB.
/// </summary>
public class PatreonData
{
    public string? CampaignUrl { get; set; }

    // Tier names or descriptions from the creator's Patreon page.
    public List<string>? Tiers { get; set; }

    // Platform name -> URL (e.g. "twitter", "discord").
    public Dictionary<string, string>? SocialLinks { get; set; }
}

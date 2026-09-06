namespace ReactionVideoAggregator.Models;

/// <summary>
/// A creator who publishes movie reaction videos (e.g. on YouTube).
/// </summary>
public class Reactor
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string YouTubeChannelId { get; set; } = string.Empty;

    // RSS worker only polls reactors that are still active.
    public bool IsActive { get; set; } = true;

    // Last successful YouTube RSS poll (UTC).
    public DateTime? LastRssCheckAt { get; set; }

    // Patreon tiers / social links — mapped to JSONB in ApplicationDbContext.
    public PatreonData? PatreonData { get; set; }

    // Navigation: all reaction videos published by this reactor.
    public ICollection<ReactionVideo> ReactionVideos { get; set; } = new List<ReactionVideo>();
}

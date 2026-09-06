namespace ReactionVideoAggregator.Services.Ingestion;

/// <summary>
/// Normalized metadata extracted from a video platform URL.
/// </summary>
public record IngestionResult(
    string Title,
    string Description,
    Dictionary<string, string> ExternalLinks);

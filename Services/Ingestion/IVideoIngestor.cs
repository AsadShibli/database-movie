namespace ReactionVideoAggregator.Services.Ingestion;

/// <summary>
/// Strategy for extracting video metadata from a specific platform.
/// </summary>
public interface IVideoIngestor
{
    Task<IngestionResult?> ExtractMetadataAsync(string url);
}
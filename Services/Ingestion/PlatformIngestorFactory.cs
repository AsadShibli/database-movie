namespace ReactionVideoAggregator.Services.Ingestion;

/// <summary>
/// Picks the correct <see cref="IVideoIngestor"/> strategy from a video URL.
/// </summary>
public class PlatformIngestorFactory
{
    private readonly IEnumerable<IVideoIngestor> _ingestors;

    public PlatformIngestorFactory(IEnumerable<IVideoIngestor> ingestors)
    {
        _ingestors = ingestors;
    }

    public IVideoIngestor GetIngestor(string url)
    {
        if (ContainsHost(url, "youtube.com") || ContainsHost(url, "youtu.be"))
        {
            return GetRequired<YouTubeIngestor>();
        }

        if (ContainsHost(url, "patreon.com"))
        {
            return GetRequired<PatreonIngestor>();
        }

        throw new NotSupportedException($"No ingestor is registered for URL: {url}");
    }

    private static bool ContainsHost(string url, string host) =>
        url.Contains(host, StringComparison.OrdinalIgnoreCase);

    private TIngestor GetRequired<TIngestor>() where TIngestor : IVideoIngestor =>
        _ingestors.OfType<TIngestor>().FirstOrDefault()
        ?? throw new InvalidOperationException($"{typeof(TIngestor).Name} is not registered.");
}

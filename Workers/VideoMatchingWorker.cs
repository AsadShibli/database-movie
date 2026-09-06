using Microsoft.EntityFrameworkCore;
using ReactionVideoAggregator.Data;
using ReactionVideoAggregator.Services.Matching;

namespace ReactionVideoAggregator.Workers;

/// <summary>
/// Background loop that matches unmatched reaction videos to TMDB movies.
/// </summary>
public class VideoMatchingWorker : BackgroundService
{
    // Hosted services are singletons — resolve scoped services per iteration.
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VideoMatchingWorker> _logger;

    public VideoMatchingWorker(IServiceScopeFactory scopeFactory, ILogger<VideoMatchingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var matcher = scope.ServiceProvider.GetRequiredService<TmdbMatchingService>();

            var batch = await db.ReactionVideos
                .Where(v => v.MovieId == null)
                .Take(10)
                .ToListAsync(stoppingToken);

            foreach (var video in batch)
            {
                _logger.LogInformation(
                    "[WORKER] Processing Video ID: {Id} | Raw Title: '{Title}'",
                    video.Id, video.Title);
                video.MovieId = await matcher.MatchMovieTitleAsync(video.Title);
                await db.SaveChangesAsync(stoppingToken);
            }

            // Per-video SaveChangesAsync above already persists MovieId as non-null.
            // if (batch.Count > 0)
            //     await db.SaveChangesAsync(stoppingToken);

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}

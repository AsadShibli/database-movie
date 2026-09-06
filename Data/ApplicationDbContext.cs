using Microsoft.EntityFrameworkCore;
using ReactionVideoAggregator.Models;

namespace ReactionVideoAggregator.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Movie> Movies => Set<Movie>();
    public DbSet<Reactor> Reactors => Set<Reactor>();
    public DbSet<ReactionVideo> ReactionVideos => Set<ReactionVideo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Unstructured Patreon metadata lives in a PostgreSQL JSONB column.
        modelBuilder.Entity<Reactor>()
            .Property(r => r.PatreonData)
            .HasColumnType("jsonb");

        // Patreon / Vimeo URLs live in a PostgreSQL JSONB column.
        modelBuilder.Entity<ReactionVideo>()
            .Property(v => v.ExternalLinks)
            .HasColumnType("jsonb");

        modelBuilder.Entity<ReactionVideo>()
            .HasOne(v => v.Movie)
            .WithMany(m => m.ReactionVideos)
            .HasForeignKey(v => v.MovieId);

        modelBuilder.Entity<ReactionVideo>()
            .HasOne(v => v.Reactor)
            .WithMany(r => r.ReactionVideos)
            .HasForeignKey(v => v.ReactorId);
    }
}

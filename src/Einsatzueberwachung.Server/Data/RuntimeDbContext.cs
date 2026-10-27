using Microsoft.EntityFrameworkCore;

namespace Einsatzueberwachung.Server.Data;

public sealed class RuntimeDbContext : DbContext
{
    public RuntimeDbContext(DbContextOptions<RuntimeDbContext> options)
        : base(options)
    {
    }

    public DbSet<RuntimeStateEntity> RuntimeStates => Set<RuntimeStateEntity>();
    public DbSet<RadioMessageEntity> RadioMessages => Set<RadioMessageEntity>();
    public DbSet<RadioReplyEntity> RadioReplies => Set<RadioReplyEntity>();
    public DbSet<TrackPointEntity> TrackPoints => Set<TrackPointEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RuntimeStateEntity>(entity =>
        {
            entity.ToTable("runtime_state");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.JsonPayload).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
        });

        // Verlaeufe laufender Suchen Punkt fuer Punkt (TrackPunktSpeicher, ab 1.11.7)
        modelBuilder.Entity<TrackPointEntity>(entity =>
        {
            entity.ToTable("runtime_track_points");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Kind).IsRequired();
            entity.Property(x => x.Key).IsRequired();
            entity.Property(x => x.Json).IsRequired();
            entity.HasIndex(x => new { x.Kind, x.Key }).HasDatabaseName("IX_runtime_track_points_Kind_Key");
        });

        modelBuilder.Entity<RadioMessageEntity>(entity =>
        {
            entity.ToTable("radio_messages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Text).IsRequired();
            entity.Property(x => x.SourceTeamId).IsRequired();
            entity.Property(x => x.SourceTeamName).IsRequired();
            entity.Property(x => x.CreatedBy).IsRequired();
            entity.Property(x => x.TimestampUtc).IsRequired();
            entity.HasMany(x => x.Replies)
                .WithOne(x => x.Message)
                .HasForeignKey(x => x.MessageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RadioReplyEntity>(entity =>
        {
            entity.ToTable("radio_replies");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Text).IsRequired();
            entity.Property(x => x.SourceTeamId).IsRequired();
            entity.Property(x => x.SourceTeamName).IsRequired();
            entity.Property(x => x.CreatedBy).IsRequired();
            entity.Property(x => x.TimestampUtc).IsRequired();
        });
    }
}

public sealed class RuntimeStateEntity
{
    public int Id { get; set; }
    public string JsonPayload { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class TrackPointEntity
{
    public long Id { get; set; }
    /// <summary>„collar“ oder „phone“</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>Halsband-ID bzw. Team-ID</summary>
    public string Key { get; set; } = string.Empty;
    public long TimestampTicks { get; set; }
    public string Json { get; set; } = string.Empty;
}

public sealed class RadioMessageEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime TimestampUtc { get; set; }
    public string Text { get; set; } = string.Empty;
    public string SourceTeamId { get; set; } = string.Empty;
    public string SourceTeamName { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;

    public List<RadioReplyEntity> Replies { get; set; } = new();
}

public sealed class RadioReplyEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string MessageId { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; }
    public string Text { get; set; } = string.Empty;
    public string SourceTeamId { get; set; } = string.Empty;
    public string SourceTeamName { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;

    public RadioMessageEntity? Message { get; set; }
}

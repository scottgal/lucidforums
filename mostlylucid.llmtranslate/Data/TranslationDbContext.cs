using Microsoft.EntityFrameworkCore;
using mostlylucid.llmtranslate.Models;

namespace mostlylucid.llmtranslate.Data;

/// <summary>
/// Database context for translation tables
/// Can be used standalone or integrated into an existing DbContext
/// </summary>
public class TranslationDbContext : DbContext, ITranslationDbContext
{
    private readonly string? _schema;

    public TranslationDbContext(DbContextOptions<TranslationDbContext> options, string? schema = null)
        : base(options)
    {
        _schema = schema;
    }

    public DbSet<TranslationString> TranslationStrings => Set<TranslationString>();
    public DbSet<Translation> Translations => Set<Translation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply schema if specified (PostgreSQL)
        if (!string.IsNullOrEmpty(_schema))
        {
            modelBuilder.HasDefaultSchema(_schema);
        }

        modelBuilder.Entity<TranslationString>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Key).HasMaxLength(200).IsRequired();
            e.Property(x => x.DefaultText).IsRequired();
            e.Property(x => x.Context).HasMaxLength(500);
            e.Property(x => x.Category).HasMaxLength(100);

            e.ToTable("TranslationStrings");
        });

        modelBuilder.Entity<Translation>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TranslationStringId, x.LanguageCode }).IsUnique();
            e.Property(x => x.LanguageCode).HasMaxLength(10).IsRequired();
            e.Property(x => x.TranslatedText).IsRequired();
            e.Property(x => x.AiModel).HasMaxLength(100);

            e.HasOne(x => x.TranslationString)
                .WithMany(ts => ts.Translations)
                .HasForeignKey(x => x.TranslationStringId)
                .OnDelete(DeleteBehavior.Cascade);

            e.ToTable("Translations");
        });
    }
}

/// <summary>
/// Interface for accessing translation database tables
/// This allows the library to work with an existing DbContext
/// </summary>
public interface ITranslationDbContext
{
    DbSet<TranslationString> TranslationStrings { get; }
    DbSet<Translation> Translations { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

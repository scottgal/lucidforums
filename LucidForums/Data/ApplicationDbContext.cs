using LucidForums.Models.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LucidForums.Data;

public class ApplicationDbContext : IdentityDbContext<User>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Forum> Forums => Set<Forum>();
    public DbSet<ForumThread> Threads => Set<ForumThread>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Charter> Charters => Set<Charter>();
    public DbSet<ForumUser> ForumUsers => Set<ForumUser>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Forum
        modelBuilder.Entity<Forum>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasOne(x => x.Charter)
                .WithMany()
                .HasForeignKey(x => x.CharterId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.CreatedBy)
                .WithMany()
                .HasForeignKey(x => x.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Thread
        modelBuilder.Entity<ForumThread>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(280).IsRequired();
            e.HasOne(x => x.Forum)
                .WithMany(f => f.Threads)
                .HasForeignKey(x => x.ForumId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.CreatedBy)
                .WithMany(u => u.ThreadsCreated)
                .HasForeignKey(x => x.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.RootMessage)
                .WithOne()
                .HasForeignKey<ForumThread>(x => x.RootMessageId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            // Map tags as a PostgreSQL text[] array (supported by Npgsql)
            try
            {
                e.Property(x => x.Tags).HasColumnType("text[]");
            }
            catch
            {
                // If the provider doesn't support arrays, leave as default; another provider-specific
                // configuration or value converter can be added in the future.
            }
        });

        // Message (Post)
        modelBuilder.Entity<Message>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Thread)
                .WithMany(t => t.Messages)
                .HasForeignKey(x => x.ForumThreadId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Parent)
                .WithMany(p => p!.Replies)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.CreatedBy)
                .WithMany(u => u.MessagesCreated)
                .HasForeignKey(x => x.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);
            try
            {
                e.Property(x => x.Path).HasColumnType("ltree");
                e.HasIndex(x => x.Path).HasMethod("gist");
            }
            catch
            {
                // Non-PostgreSQL providers may not support ltree/gist; skip provider-specific config.
            }
        });

        // ForumUser (membership)
        modelBuilder.Entity<ForumUser>(e =>
        {
            e.HasKey(x => new { x.ForumId, x.UserId });
            e.HasOne(x => x.Forum)
                .WithMany()
                .HasForeignKey(x => x.ForumId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User)
                .WithMany(u => u.ForumMemberships)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Role).HasMaxLength(32).HasDefaultValue("member");
        });

        // Charter arrays (Rules/Behaviors) are supported by Npgsql as text[]
        modelBuilder.Entity<Charter>(e =>
        {
            e.HasKey(x => x.Id);

            // Seed sample data
            var c1Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var c2Id = Guid.Parse("22222222-2222-2222-2222-222222222222");
            e.HasData(
                new Charter
                {
                    Id = c1Id,
                    Name = "General Community Charter",
                    Purpose = "Foster respectful, constructive discussions on a wide range of topics.",
                    Rules = new List<string> { "Be respectful", "No hate speech", "No spam or advertising" },
                    Behaviors = new List<string> { "Assume good intent", "Use evidence and cite sources", "Welcome newcomers" }
                },
                new Charter
                {
                    Id = c2Id,
                    Name = "Makers & Builders Charter",
                    Purpose = "Share projects, get feedback, and help others build things.",
                    Rules = new List<string> { "Show your work", "Be constructive in feedback", "Tag NSFW content appropriately" },
                    Behaviors = new List<string> { "Encourage collaboration", "Offer actionable suggestions", "Celebrate progress" }
                }
            );
        });

        // AppSettings single-row configuration
        modelBuilder.Entity<AppSettings>(e =>
        {
            e.HasKey(x => x.Id);
        });

        // Enable PostgreSQL ltree extension if using Npgsql
        try
        {
            modelBuilder.HasPostgresExtension("ltree");
        }
        catch
        {
            // No-op when not using Npgsql provider (e.g., design-time or SQLite fallback)
        }
    }
}
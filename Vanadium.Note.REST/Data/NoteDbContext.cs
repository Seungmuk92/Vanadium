using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Models;

namespace Vanadium.Note.REST.Data;

public class NoteDbContext(DbContextOptions<NoteDbContext> options) : DbContext(options)
{
    public DbSet<NoteItem> Notes => Set<NoteItem>();
    public DbSet<FileAttachment> FileAttachments => Set<FileAttachment>();
    public DbSet<LabelCategory> LabelCategories => Set<LabelCategory>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<NoteLabel> NoteLabels => Set<NoteLabel>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NoteLabel>()
            .HasKey(nl => new { nl.NoteId, nl.LabelId });

        modelBuilder.Entity<NoteLabel>()
            .HasOne(nl => nl.Note)
            .WithMany(n => n.NoteLabels)
            .HasForeignKey(nl => nl.NoteId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<NoteLabel>()
            .HasOne(nl => nl.Label)
            .WithMany()
            .HasForeignKey(nl => nl.LabelId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Label>()
            .HasOne(l => l.Category)
            .WithMany(c => c.Labels)
            .HasForeignKey(l => l.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<NoteItem>()
            .HasOne(n => n.ParentNote)
            .WithMany(n => n.ChildNotes)
            .HasForeignKey(n => n.ParentNoteId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<NoteItem>()
            .HasIndex(n => new { n.Title, n.ContentText })
            .HasMethod("GIN")
            .HasOperators("gin_trgm_ops", "gin_trgm_ops");

        // Recycle Bin: hide soft-deleted notes from every query by default.
        // Recycle Bin-aware paths (recycle bin listing, restore, purge, orphan-file scans,
        // account wipe) must opt out explicitly via IgnoreQueryFilters().
        modelBuilder.Entity<NoteItem>()
            .HasQueryFilter(n => n.DeletedAt == null);

        // Matching filter on the join table: NoteLabel has a required navigation
        // to the filtered NoteItem, so it must be filtered the same way.
        modelBuilder.Entity<NoteLabel>()
            .HasQueryFilter(nl => nl.Note.DeletedAt == null);

        modelBuilder.Entity<NoteItem>()
            .HasIndex(n => n.DeletedAt)
            .HasFilter("\"DeletedAt\" IS NOT NULL");

        // Archive: deliberately NOT part of the global query filter. Archive visibility
        // is not uniform (hidden on Home/Board/children/mentions, visible in search,
        // single-note GET, and the archive page), so read paths exclude archived notes
        // with explicit Where(n => n.ArchivedAt == null) predicates instead. This also
        // keeps every existing IgnoreQueryFilters() opt-out scoped to the recycle bin
        // and lets file-cleanup scans and account wipe see archived content unchanged.
        modelBuilder.Entity<NoteItem>()
            .HasIndex(n => n.ArchivedAt)
            .HasFilter("\"ArchivedAt\" IS NOT NULL");

        // Share tokens are looked up on the anonymous read path and must be unique.
        // Filtered so the many notes with a NULL token don't collide on the unique index.
        modelBuilder.Entity<NoteItem>()
            .HasIndex(n => n.ShareToken)
            .IsUnique()
            .HasFilter("\"ShareToken\" IS NOT NULL");

        modelBuilder.Entity<ApiToken>()
            .HasIndex(t => t.TokenHash)
            .IsUnique();
    }
}

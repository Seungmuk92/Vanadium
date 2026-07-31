using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Models;

namespace Vanadium.Note.REST.Data;

public class NoteDbContext(DbContextOptions<NoteDbContext> options) : DbContext(options)
{
    public DbSet<NoteItem> Notes => Set<NoteItem>();
    public DbSet<FileAttachment> FileAttachments => Set<FileAttachment>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();
    public DbSet<PropertyDefinition> PropertyDefinitions => Set<PropertyDefinition>();
    public DbSet<PropertyOption> PropertyOptions => Set<PropertyOption>();
    public DbSet<NotePropertyValue> NotePropertyValues => Set<NotePropertyValue>();
    public DbSet<NotePropertySelectedOption> NotePropertySelectedOptions => Set<NotePropertySelectedOption>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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

        // Orphan-file reference scan (FileCleanupService.IsReferencedInAnyNoteAsync) probes
        // for /api/files/{guid} and /api/images/{guid} substrings that live in HTML *attribute*
        // values of Content. StripHtml drops attribute text, so those references never reach
        // ContentText and its trigram index cannot serve the scan. A separate gin_trgm_ops
        // index on Content lets the per-file substring (I)LIKE probe use the index instead of
        // a full corpus scan (issue #219).
        modelBuilder.Entity<NoteItem>()
            .HasIndex(n => n.Content)
            .HasMethod("GIN")
            .HasOperators("gin_trgm_ops");

        // Recycle Bin: hide soft-deleted notes from every query by default.
        // Recycle Bin-aware paths (recycle bin listing, restore, purge, orphan-file scans,
        // account wipe) must opt out explicitly via IgnoreQueryFilters().
        modelBuilder.Entity<NoteItem>()
            .HasQueryFilter(n => n.DeletedAt == null);

        modelBuilder.Entity<NoteItem>()
            .HasIndex(n => n.DeletedAt)
            .HasFilter("\"DeletedAt\" IS NOT NULL");

        // Archive: deliberately NOT part of the global query filter. Archive visibility
        // is not uniform (hidden on Home/children/mentions, visible in search,
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

        ConfigureProperties(modelBuilder);
    }

    /// <summary>Note Properties (issue #343): the EAV value store — composite PKs, matching
    /// soft-delete query filters, DB cascades. See
    /// docs/plannings/note-property/note-properties-feature.md §4.4–4.5.</summary>
    private static void ConfigureProperties(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotePropertyValue>()
            .HasKey(v => new { v.NoteId, v.DefinitionId });

        modelBuilder.Entity<NotePropertyValue>()
            .HasOne(v => v.Note)
            .WithMany(n => n.PropertyValues)
            .HasForeignKey(v => v.NoteId)
            .OnDelete(DeleteBehavior.Cascade);          // note hard-delete wipes its values

        modelBuilder.Entity<NotePropertyValue>()
            .HasOne(v => v.Definition)
            .WithMany()
            .HasForeignKey(v => v.DefinitionId)
            .OnDelete(DeleteBehavior.Cascade);          // definition delete wipes all values (FR-7)

        // INV-P3 at the DB level: (DefinitionId, SelectedOptionId) must match an option
        // of the same definition. Requires the alternate key below on PropertyOption.
        modelBuilder.Entity<NotePropertyValue>()
            .HasOne(v => v.SelectedOption)
            .WithMany()
            .HasForeignKey(v => new { v.DefinitionId, v.SelectedOptionId })
            .HasPrincipalKey(o => new { o.DefinitionId, o.Id })
            .OnDelete(DeleteBehavior.Cascade);          // option delete removes Select value rows (FR-8)

        modelBuilder.Entity<NotePropertySelectedOption>()
            .HasKey(s => new { s.NoteId, s.DefinitionId, s.OptionId });

        modelBuilder.Entity<NotePropertySelectedOption>()
            .HasOne(s => s.Value)
            .WithMany(v => v.SelectedOptions)
            .HasForeignKey(s => new { s.NoteId, s.DefinitionId })
            .OnDelete(DeleteBehavior.Cascade);          // clearing a value clears its selections

        modelBuilder.Entity<NotePropertySelectedOption>()
            .HasOne(s => s.Option)
            .WithMany()
            .HasForeignKey(s => new { s.DefinitionId, s.OptionId })
            .HasPrincipalKey(o => new { o.DefinitionId, o.Id })
            .OnDelete(DeleteBehavior.Cascade);          // option delete removes selections (FR-8)

        modelBuilder.Entity<PropertyOption>()
            .HasOne(o => o.Definition)
            .WithMany(d => d.Options)
            .HasForeignKey(o => o.DefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        // INV-P4: soft-delete parity filters so default queries never see recycle-bin
        // values. Definition-level scans must use IgnoreQueryFilters().
        modelBuilder.Entity<NotePropertyValue>()
            .HasQueryFilter(v => v.Note.DeletedAt == null);
        modelBuilder.Entity<NotePropertySelectedOption>()
            .HasQueryFilter(s => s.Value.Note.DeletedAt == null);

        // One composite index per filter/sort shape: "for definition D, compare/order <typed column>".
        modelBuilder.Entity<NotePropertyValue>()
            .HasIndex(v => new { v.DefinitionId, v.NumberValue });
        modelBuilder.Entity<NotePropertyValue>()
            .HasIndex(v => new { v.DefinitionId, v.DateValue });
        modelBuilder.Entity<NotePropertyValue>()
            .HasIndex(v => new { v.DefinitionId, v.TextValue });
        modelBuilder.Entity<NotePropertyValue>()
            .HasIndex(v => new { v.DefinitionId, v.SelectedOptionId });
        modelBuilder.Entity<NotePropertyValue>()
            .HasIndex(v => new { v.DefinitionId, v.BoolValue });

        // Option usage counts and option-delete fan-out.
        modelBuilder.Entity<NotePropertySelectedOption>()
            .HasIndex(s => s.OptionId);
    }
}

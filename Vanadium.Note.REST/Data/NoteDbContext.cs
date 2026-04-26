using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Models;

namespace Vanadium.Note.REST.Data;

public class NoteDbContext(DbContextOptions<NoteDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<NoteItem> Notes => Set<NoteItem>();
    public DbSet<FileAttachment> FileAttachments => Set<FileAttachment>();
    public DbSet<LabelCategory> LabelCategories => Set<LabelCategory>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<NoteLabel> NoteLabels => Set<NoteLabel>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();

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

        modelBuilder.Entity<UserSettings>()
            .HasIndex(s => s.Username)
            .IsUnique();
    }
}

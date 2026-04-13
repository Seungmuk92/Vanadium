using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Models;

namespace Vanadium.Note.REST.Data;

public class NoteDbContext(DbContextOptions<NoteDbContext> options) : DbContext(options)
{
    public DbSet<NoteItem> Notes => Set<NoteItem>();
    public DbSet<FileAttachment> FileAttachments => Set<FileAttachment>();
}

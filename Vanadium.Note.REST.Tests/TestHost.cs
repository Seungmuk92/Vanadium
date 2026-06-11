using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Vanadium.Note.REST.Data;
using Vanadium.Note.REST.Models;
using Vanadium.Note.REST.Services;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Per-test host: an in-memory SQLite database (relational enough for the
/// query-filter and soft-delete/archive logic under test) plus the real
/// service classes wired with null loggers. PostgreSQL-only features
/// (trigram ILIKE search) are out of unit scope by design.
/// </summary>
public sealed class TestHost : IDisposable
{
    private readonly SqliteConnection _connection;

    public NoteDbContext Db { get; }
    public NoteService Notes { get; }
    public LabelService Labels { get; }
    public AccountService Account { get; }
    public FileCleanupService FileCleanup { get; }

    /// <summary>Content root used by FileCleanupService ("uploads" lives below it).</summary>
    public string ContentRoot { get; }

    public TestHost()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<NoteDbContext>()
            .UseSqlite(_connection)
            .Options;

        Db = new NoteDbContext(options);
        Db.Database.EnsureCreated();

        ContentRoot = Path.Combine(Path.GetTempPath(), $"vanadium-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(ContentRoot, "uploads"));

        FileCleanup = new FileCleanupService(
            Db, new TestWebHostEnvironment(ContentRoot), NullLogger<FileCleanupService>.Instance);
        Notes = new NoteService(Db, FileCleanup, NullLogger<NoteService>.Instance);
        Labels = new LabelService(Db, NullLogger<LabelService>.Instance);
        Account = new AccountService(Db, NullLogger<AccountService>.Instance);
    }

    public async Task<User> CreateUserAsync(string username = "tester")
    {
        var user = new User { Id = Guid.NewGuid(), Username = username, PasswordHash = "x" };
        Db.Users.Add(user);
        await Db.SaveChangesAsync();
        return user;
    }

    public async Task<NoteItem> CreateNoteAsync(
        Guid userId, string title = "Note", Guid? parentId = null, string content = "")
    {
        var note = await Notes.Create(userId, new NoteItem
        {
            Title = title,
            Content = content,
            ParentNoteId = parentId
        });
        return note;
    }

    /// <summary>Re-reads a note bypassing all filters (recycle bin included).</summary>
    public async Task<NoteItem?> FindAsync(Guid id) =>
        await Db.Notes.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(n => n.Id == id);

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
        try { Directory.Delete(ContentRoot, recursive: true); } catch { /* best effort */ }
    }

    private sealed class TestWebHostEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = contentRoot;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Vanadium.Note.REST.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRoot;
        public string EnvironmentName { get; set; } = "Test";
    }
}

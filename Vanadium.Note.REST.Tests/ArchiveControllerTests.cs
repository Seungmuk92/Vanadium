using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vanadium.Note.REST.Controllers;
using Vanadium.Note.REST.Models;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Status-code mapping tests for the archive endpoints (spec §7, T-9/T-10/T-14).
/// </summary>
public class ArchiveControllerTests
{
    private static NotesController CreateNotesController(TestHost h, Guid userId)
    {
        var controller = new NotesController(
            h.Notes, h.Labels, h.Db, NullLogger<NotesController>.Instance);
        controller.ControllerContext = CreateContext(userId);
        return controller;
    }

    private static LabelsController CreateLabelsController(TestHost h, Guid userId)
    {
        var controller = new LabelsController(
            h.Labels, h.Db, NullLogger<LabelsController>.Instance);
        controller.ControllerContext = CreateContext(userId);
        return controller;
    }

    private static ControllerContext CreateContext(Guid userId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvcCore();
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test")),
            RequestServices = services.BuildServiceProvider()
        };
        return new ControllerContext { HttpContext = httpContext };
    }

    [Fact]
    public async Task Put_OnArchivedNote_Returns403()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();
        var note = await h.CreateNoteAsync(user.Id, "Read-only");
        await h.Notes.Archive(user.Id, note.Id);
        var controller = CreateNotesController(h, user.Id);

        var result = await controller.Update(note.Id,
            new NoteItem { Title = "Changed", Content = "", UpdatedAt = default });

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
    }

    [Fact]
    public async Task AddAndRemoveLabel_OnArchivedNote_Return403()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();
        var note = await h.CreateNoteAsync(user.Id, "Read-only");
        var label = await h.Labels.CreateLabelAsync(user.Id, "todo", null);
        await h.Labels.AddLabelToNoteAsync(user.Id, note.Id, label.Id);
        await h.Notes.Archive(user.Id, note.Id);
        var controller = CreateLabelsController(h, user.Id);

        var addResult = await controller.AddLabel(note.Id, new AddLabelRequest(label.Id));
        var addObject = Assert.IsType<ObjectResult>(addResult);
        Assert.Equal(StatusCodes.Status403Forbidden, addObject.StatusCode);

        var removeResult = await controller.RemoveLabel(note.Id, label.Id);
        var removeObject = Assert.IsType<ObjectResult>(removeResult);
        Assert.Equal(StatusCodes.Status403Forbidden, removeObject.StatusCode);
    }

    [Fact]
    public async Task Create_UnderArchivedParent_Returns400()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();
        var parent = await h.CreateNoteAsync(user.Id, "Archived parent");
        await h.Notes.Archive(user.Id, parent.Id);
        var controller = CreateNotesController(h, user.Id);

        var result = await controller.Create(
            new NoteItem { Title = "Orphan", Content = "", ParentNoteId = parent.Id });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Reparent_OntoArchivedNote_Returns400()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();
        var archivedParent = await h.CreateNoteAsync(user.Id, "Archived parent");
        var note = await h.CreateNoteAsync(user.Id, "Movable");
        await h.Notes.Archive(user.Id, archivedParent.Id);
        var controller = CreateNotesController(h, user.Id);

        var result = await controller.Update(note.Id, new NoteItem
        {
            Title = "Movable",
            Content = "",
            ParentNoteId = archivedParent.Id,
            UpdatedAt = default
        });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Archive_OnMissingOrBinnedNote_Returns404()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();
        var binned = await h.CreateNoteAsync(user.Id, "Binned");
        await h.Notes.Delete(user.Id, binned.Id);
        var controller = CreateNotesController(h, user.Id);

        Assert.IsType<NotFoundResult>(await controller.Archive(Guid.NewGuid()));
        Assert.IsType<NotFoundResult>(await controller.Archive(binned.Id));
    }

    [Fact]
    public async Task Unarchive_OnActiveNote_Returns404()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();
        var note = await h.CreateNoteAsync(user.Id, "Active");
        var controller = CreateNotesController(h, user.Id);

        Assert.IsType<NotFoundResult>(await controller.Unarchive(note.Id));
    }

    [Fact]
    public async Task DeletePermanent_OnArchivedNotDeletedNote_Returns409()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();
        var note = await h.CreateNoteAsync(user.Id, "Archived");
        await h.Notes.Archive(user.Id, note.Id);
        var controller = CreateNotesController(h, user.Id);

        var result = await controller.DeletePermanent(note.Id);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task GetArchive_ReturnsPagedRoots()
    {
        using var h = new TestHost();
        var user = await h.CreateUserAsync();
        var note = await h.CreateNoteAsync(user.Id, "Stored away");
        await h.Notes.Archive(user.Id, note.Id);
        var controller = CreateNotesController(h, user.Id);

        var result = await controller.GetArchive();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var paged = Assert.IsType<PagedResult<ArchivedNoteSummary>>(ok.Value);
        Assert.Single(paged.Items);
        Assert.Equal(note.Id, paged.Items[0].Id);
    }
}

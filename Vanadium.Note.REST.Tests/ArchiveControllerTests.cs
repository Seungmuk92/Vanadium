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
    private static NotesController CreateNotesController(TestHost h)
    {
        var controller = new NotesController(
            h.Notes, h.Labels, h.Db, NullLogger<NotesController>.Instance);
        controller.ControllerContext = CreateContext();
        return controller;
    }

    private static LabelsController CreateLabelsController(TestHost h)
    {
        var controller = new LabelsController(
            h.Labels, NullLogger<LabelsController>.Instance);
        controller.ControllerContext = CreateContext();
        return controller;
    }

    // A minimal context with RequestServices so ControllerBase.Problem() can resolve
    // ProblemDetailsFactory. There is no user identity in the single-user model.
    private static ControllerContext CreateContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvcCore();
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, AuthController.OwnerName)], "Test")),
            RequestServices = services.BuildServiceProvider()
        };
        return new ControllerContext { HttpContext = httpContext };
    }

    [Fact]
    public async Task Put_OnArchivedNote_Returns403()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Read-only");
        await h.Notes.Archive(note.Id);
        var controller = CreateNotesController(h);

        var result = await controller.Update(note.Id,
            new NoteItem { Title = "Changed", Content = "", UpdatedAt = default }, force: false, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
    }

    [Fact]
    public async Task AddAndRemoveLabel_OnArchivedNote_Return403()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Read-only");
        var label = await h.Labels.CreateLabelAsync("todo", null);
        await h.Labels.AddLabelToNoteAsync(note.Id, label.Id);
        await h.Notes.Archive(note.Id);
        var controller = CreateLabelsController(h);

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
        var parent = await h.CreateNoteAsync("Archived parent");
        await h.Notes.Archive(parent.Id);
        var controller = CreateNotesController(h);

        var result = await controller.Create(
            new NoteItem { Title = "Orphan", Content = "", ParentNoteId = parent.Id }, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
    }

    [Fact]
    public async Task Reparent_OntoArchivedNote_Returns400()
    {
        using var h = new TestHost();
        var archivedParent = await h.CreateNoteAsync("Archived parent");
        var note = await h.CreateNoteAsync("Movable");
        await h.Notes.Archive(archivedParent.Id);
        var controller = CreateNotesController(h);

        var result = await controller.Update(note.Id, new NoteItem
        {
            Title = "Movable",
            Content = "",
            ParentNoteId = archivedParent.Id,
            UpdatedAt = default
        }, force: false, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
    }

    [Fact]
    public async Task Archive_OnMissingOrBinnedNote_Returns404()
    {
        using var h = new TestHost();
        var binned = await h.CreateNoteAsync("Binned");
        await h.Notes.Delete(binned.Id);
        var controller = CreateNotesController(h);

        Assert.IsType<NotFoundResult>(await controller.Archive(Guid.NewGuid(), CancellationToken.None));
        Assert.IsType<NotFoundResult>(await controller.Archive(binned.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Unarchive_OnActiveNote_Returns404()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Active");
        var controller = CreateNotesController(h);

        Assert.IsType<NotFoundResult>(await controller.Unarchive(note.Id, CancellationToken.None));
    }

    [Fact]
    public async Task DeletePermanent_OnArchivedNotDeletedNote_Returns409()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Archived");
        await h.Notes.Archive(note.Id);
        var controller = CreateNotesController(h);

        var result = await controller.DeletePermanent(note.Id, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
    }

    [Fact]
    public async Task GetArchive_ReturnsPagedRoots()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Stored away");
        await h.Notes.Archive(note.Id);
        var controller = CreateNotesController(h);

        var result = await controller.GetArchive();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var paged = Assert.IsType<PagedResult<ArchivedNoteSummary>>(ok.Value);
        Assert.Single(paged.Items);
        Assert.Equal(note.Id, paged.Items[0].Id);
    }
}

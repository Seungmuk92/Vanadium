using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using Vanadium.Note.Web.Pages;
using Vanadium.Note.Web.Services;
using Xunit;

namespace Vanadium.Note.Web.Tests.Components;

/// <summary>
/// The Board groups notes by a Select property's options (issue #373 review): labels are gone, so
/// columns come from <c>PropertyDefinition.Options</c> and a note's column is its Select value.
/// Guards the rules that make the page usable rather than the drag plumbing (which needs a real
/// browser): only Select definitions can be grouped by, each note lands in its option's column, a
/// note with no value for the grouping property is still visible in the "No value" column, and that
/// column's cards can be dragged out to assign a value (#375).
/// </summary>
public sealed class BoardGroupingTests : TestContext
{
    private const string StatusId = "11111111-1111-1111-1111-111111111111";
    private const string TodoId = "22222222-2222-2222-2222-222222222222";
    private const string DoingId = "33333333-3333-3333-3333-333333333333";
    private const string UnsetNoteId = "aaaaaaaa-0000-0000-0000-000000000003";

    // "type" is the numeric PropertyType (2 = Select, 3 = MultiSelect) — the wire form the client mirrors.
    private const string DefinitionsJson = """
        [
          { "id": "11111111-1111-1111-1111-111111111111", "name": "Status", "type": 2, "sortOrder": 0,
            "options": [
              { "id": "22222222-2222-2222-2222-222222222222", "name": "Todo",  "sortOrder": 0 },
              { "id": "33333333-3333-3333-3333-333333333333", "name": "Doing", "sortOrder": 1 }
            ] },
          { "id": "44444444-4444-4444-4444-444444444444", "name": "Tags", "type": 3, "sortOrder": 1,
            "options": [] }
        ]
        """;

    private const string SummariesJson = """
        [
          { "id": "aaaaaaaa-0000-0000-0000-000000000001", "title": "Todo note",  "updatedAt": "2026-07-30T00:00:00Z",
            "properties": [ { "definitionId": "11111111-1111-1111-1111-111111111111", "name": "Status", "type": 2,
                              "optionId": "22222222-2222-2222-2222-222222222222" } ] },
          { "id": "aaaaaaaa-0000-0000-0000-000000000002", "title": "Doing note", "updatedAt": "2026-07-30T00:00:00Z",
            "properties": [ { "definitionId": "11111111-1111-1111-1111-111111111111", "name": "Status", "type": 2,
                              "optionId": "33333333-3333-3333-3333-333333333333" } ] },
          { "id": "aaaaaaaa-0000-0000-0000-000000000003", "title": "Unset note", "updatedAt": "2026-07-30T00:00:00Z",
            "properties": [] }
        ]
        """;

    /// <summary>Renders the board against a fake API; <paramref name="setValueStatus"/> is what the
    /// property-value PUT answers, so a move can be exercised as success, failure, or 403.</summary>
    private (IRenderedComponent<Board> Board, BoardApiHandler Api) RenderBoard(
        HttpStatusCode setValueStatus = HttpStatusCode.OK)
    {
        Services.AddMudServices();
        Services.AddLogging();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var handler = new BoardApiHandler(setValueStatus);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        Services.AddScoped(_ => new PropertyService(http, NullLogger<PropertyService>.Instance));
        Services.AddScoped(_ => new NoteService(http, NullLogger<NoteService>.Instance));

        return (RenderComponent<Board>(), handler);
    }

    [Fact]
    public void GroupBy_OffersSelectDefinitionsOnly()
    {
        var (cut, _) = RenderBoard();

        cut.WaitForAssertion(() =>
        {
            var buttons = cut.FindAll(".board-group-btn").Select(b => b.TextContent.Trim()).ToList();
            // "Tags" is MultiSelect: a note could sit in several columns at once, so it is not groupable.
            Assert.Equal(["Status"], buttons);
        });
    }

    [Fact]
    public void Columns_ComeFromOptions_AndCardsLandInTheirOptionColumn()
    {
        var (cut, _) = RenderBoard();

        cut.WaitForAssertion(() =>
        {
            var optionColumns = cut.FindAll(".board-column[data-option-id]");
            Assert.Equal(2, optionColumns.Count);

            var todo = optionColumns.Single(c => c.GetAttribute("data-option-id") == TodoId);
            Assert.Equal("Todo", todo.QuerySelector(".board-column-title")!.TextContent.Trim());
            Assert.Equal("Todo note", todo.QuerySelector(".board-card-title")!.TextContent.Trim());

            var doing = optionColumns.Single(c => c.GetAttribute("data-option-id") == DoingId);
            Assert.Equal("Doing", doing.QuerySelector(".board-column-title")!.TextContent.Trim());
            Assert.Equal("Doing note", doing.QuerySelector(".board-card-title")!.TextContent.Trim());
        });
    }

    [Fact]
    public void NoteWithoutValue_ShowsInNoValueColumn_WhichIsNotADropTarget()
    {
        var (cut, _) = RenderBoard();

        cut.WaitForAssertion(() =>
        {
            var unclassified = cut.Find(".board-column-unclassified");
            Assert.Equal("No value", unclassified.QuerySelector(".board-column-title")!.TextContent.Trim());
            Assert.Equal("Unset note", unclassified.QuerySelector(".board-card-title")!.TextContent.Trim());
            // No data-option-id ⇒ board-drag-drop.js never resolves it as a column, so dropping a
            // card INTO it cannot clear a value — the reverse move stays out of scope (#272, #375).
            Assert.False(unclassified.HasAttribute("data-option-id"));
            Assert.DoesNotContain(unclassified.QuerySelectorAll(".board-card"),
                c => c.HasAttribute("data-option-id"));
        });
    }

    [Fact]
    public void NoValueCards_AreDragSources_AndOfferTheTouchMoveSheet()
    {
        var (cut, _) = RenderBoard();

        cut.WaitForAssertion(() =>
        {
            var cards = cut.Find(".board-column-unclassified").QuerySelectorAll(".board-card");
            Assert.NotEmpty(cards);
            // Draggable OUT of the column so a drop on an option column assigns that option (#375).
            Assert.All(cards, c => Assert.Equal("true", c.GetAttribute("draggable")));
            // Touch fallback: dragstart/drop never fire on touch, so the kebab sheet is the only
            // way to assign a value there (#273).
            Assert.All(cards, c => Assert.NotNull(c.QuerySelector(".board-card-move")));
        });
    }

    [Fact]
    public async Task NoValueCard_DroppedOnOptionColumn_SetsValueWithOneWriteAndMovesCard()
    {
        var (cut, api) = RenderBoard();
        cut.WaitForElement(".board-column-unclassified");

        // fromOptionId is null: a "No value" card carries no data-option-id.
        await cut.InvokeAsync(() => cut.Instance.OnDropFromJs(UnsetNoteId, null, TodoId));

        // A Select holds exactly one option, so one PUT both empties the old column and fills the
        // new one — no clear-then-set two-step with a partial-failure window (#271).
        var write = Assert.Single(api.Writes);
        Assert.Equal(HttpMethod.Put, write.Method);
        Assert.Equal($"/api/notes/{UnsetNoteId}/properties/{StatusId}", write.Path);
        Assert.Contains(TodoId, write.Body, StringComparison.OrdinalIgnoreCase);

        cut.WaitForAssertion(() =>
        {
            var todo = cut.Find($".board-column[data-option-id='{TodoId}']");
            Assert.Contains(todo.QuerySelectorAll(".board-card-title"),
                t => t.TextContent.Trim() == "Unset note");
            Assert.Equal("2", todo.QuerySelector(".board-column-count")!.TextContent.Trim());
            // It was the only unclassified note, so the whole virtual column is gone.
            Assert.Empty(cut.FindAll(".board-column-unclassified"));
        });
    }

    [Fact]
    public async Task NoValueCard_MoveFailure_RollsBackToNoValueColumn()
    {
        var (cut, _) = RenderBoard(HttpStatusCode.InternalServerError);
        cut.WaitForElement(".board-column-unclassified");

        await cut.InvokeAsync(() => cut.Instance.OnDropFromJs(UnsetNoteId, null, TodoId));

        cut.WaitForAssertion(() =>
        {
            var unclassified = cut.Find(".board-column-unclassified");
            Assert.Equal("Unset note", unclassified.QuerySelector(".board-card-title")!.TextContent.Trim());
            Assert.DoesNotContain(
                cut.Find($".board-column[data-option-id='{TodoId}']").QuerySelectorAll(".board-card-title"),
                t => t.TextContent.Trim() == "Unset note");
        });

        var snackbar = Services.GetRequiredService<ISnackbar>();
        Assert.Contains(snackbar.ShownSnackbars,
            s => s.Severity == Severity.Error && s.Message?.Contains("Failed to move note") == true);
    }

    [Fact]
    public async Task NoValueCard_MoveOfArchivedNote_RollsBackWithReadOnlyMessage()
    {
        var (cut, _) = RenderBoard(HttpStatusCode.Forbidden);
        cut.WaitForElement(".board-column-unclassified");

        await cut.InvokeAsync(() => cut.Instance.OnDropFromJs(UnsetNoteId, null, TodoId));

        cut.WaitForAssertion(() =>
        {
            var unclassified = cut.Find(".board-column-unclassified");
            Assert.Equal("Unset note", unclassified.QuerySelector(".board-card-title")!.TextContent.Trim());
        });

        var snackbar = Services.GetRequiredService<ISnackbar>();
        Assert.Contains(snackbar.ShownSnackbars,
            s => s.Severity == Severity.Error && s.Message?.Contains("archived") == true);
    }

    /// <summary>A recorded non-GET request (the property-value writes the board issues).</summary>
    private sealed record RecordedWrite(HttpMethod Method, string Path, string Body);

    /// <summary>Serves the two GETs the board issues, keyed by path, and records/answers the
    /// property-value PUT so a move can be asserted end to end.</summary>
    private sealed class BoardApiHandler(HttpStatusCode setValueStatus) : HttpMessageHandler
    {
        public List<RecordedWrite> Writes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (request.Method != HttpMethod.Get)
            {
                var body = request.Content is null
                    ? ""
                    : await request.Content.ReadAsStringAsync(cancellationToken);
                Writes.Add(new RecordedWrite(request.Method, path, body));
                if (setValueStatus != HttpStatusCode.OK)
                    return new HttpResponseMessage(setValueStatus);
                // Echo a value row back; the board only checks success, but SetValueAsync
                // deserializes the response and fails on a null body.
                return Json($$"""
                    { "definitionId": "{{StatusId}}", "name": "Status", "type": 2, "optionId": "{{TodoId}}" }
                    """);
            }

            return Json(path switch
            {
                "/api/properties" => DefinitionsJson,
                "/api/notes/summaries" => SummariesJson,
                _ => "[]"
            });
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
    }
}

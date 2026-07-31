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
/// Guards the three rules that make the page usable rather than the drag plumbing (which needs a
/// real browser): only Select definitions can be grouped by, each note lands in its option's column,
/// and a note with no value for the grouping property is still visible in the "No value" column.
/// </summary>
public sealed class BoardGroupingTests : TestContext
{
    private const string TodoId = "22222222-2222-2222-2222-222222222222";
    private const string DoingId = "33333333-3333-3333-3333-333333333333";

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

    private IRenderedComponent<Board> RenderBoard()
    {
        Services.AddMudServices();
        Services.AddLogging();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var http = new HttpClient(new BoardApiHandler()) { BaseAddress = new Uri("http://localhost/") };
        Services.AddScoped(_ => new PropertyService(http, NullLogger<PropertyService>.Instance));
        Services.AddScoped(_ => new NoteService(http, NullLogger<NoteService>.Instance));

        return RenderComponent<Board>();
    }

    [Fact]
    public void GroupBy_OffersSelectDefinitionsOnly()
    {
        var cut = RenderBoard();

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
        var cut = RenderBoard();

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
        var cut = RenderBoard();

        cut.WaitForAssertion(() =>
        {
            var unclassified = cut.Find(".board-column-unclassified");
            Assert.Equal("No value", unclassified.QuerySelector(".board-column-title")!.TextContent.Trim());
            Assert.Equal("Unset note", unclassified.QuerySelector(".board-card-title")!.TextContent.Trim());
            // No data-option-id ⇒ board-drag-drop.js never resolves it as a column (#272).
            Assert.False(unclassified.HasAttribute("data-option-id"));
            Assert.DoesNotContain(unclassified.QuerySelectorAll(".board-card"), c => c.HasAttribute("draggable"));
        });
    }

    /// <summary>Serves the two GETs the board issues, keyed by path.</summary>
    private sealed class BoardApiHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var json = path switch
            {
                "/api/properties" => DefinitionsJson,
                "/api/notes/summaries" => SummariesJson,
                _ => "[]"
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}

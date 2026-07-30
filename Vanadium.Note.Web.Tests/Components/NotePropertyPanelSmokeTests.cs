using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using Vanadium.Note.Web.Components;
using Vanadium.Note.Web.Models;
using Vanadium.Note.Web.Services;
using Xunit;

namespace Vanadium.Note.Web.Tests.Components;

/// <summary>
/// Regression guard for the "Add property does nothing" report on PR #372 (issue #343): the panel's
/// "Add property" control must render and stay enabled even when no property definitions exist yet —
/// previously the menu was disabled when the definition list was empty, so the button was a silent
/// dead end. Renders the real component with MudBlazor services and a stubbed empty definitions
/// response.
/// </summary>
public sealed class NotePropertyPanelSmokeTests : TestContext
{
    [Fact]
    public void AddProperty_Renders_AndIsNotDisabled_WithNoDefinitions()
    {
        Services.AddMudServices();
        Services.AddLogging();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var http = new HttpClient(new EmptyDefinitionsHandler()) { BaseAddress = new Uri("http://localhost/") };
        Services.AddScoped(_ => new PropertyService(http, NullLogger<PropertyService>.Instance));

        // MudMenu renders a MudPopover, which requires a MudPopoverProvider in the same render tree.
        var noteId = Guid.NewGuid();
        var cut = Render(builder =>
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<NotePropertyPanel>(1);
            builder.AddComponentParameter(2, nameof(NotePropertyPanel.NoteId), noteId);
            builder.AddComponentParameter(3, nameof(NotePropertyPanel.Values), new List<NotePropertyValue>());
            builder.AddComponentParameter(4, nameof(NotePropertyPanel.ReadOnly), false);
            builder.CloseComponent();
        });

        // The activator button is present …
        var addButton = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Add property"));
        Assert.NotNull(addButton);
        // … and NOT disabled (the regression: it was disabled whenever no definitions existed).
        Assert.False(addButton!.HasAttribute("disabled"));
    }

    private sealed class EmptyDefinitionsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
            });
    }
}

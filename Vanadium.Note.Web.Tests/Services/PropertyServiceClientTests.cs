using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Vanadium.Note.Web.Models;
using Vanadium.Note.Web.Services;
using Xunit;

namespace Vanadium.Note.Web.Tests.Services;

/// <summary>
/// Smoke coverage for the property-aware note list client (issue #343): property filters and the
/// property sort must be serialized into the request URL, and the definitions cache must be
/// invalidated after a mutation so a stale definition list never lingers.
/// </summary>
public sealed class PropertyServiceClientTests
{
    [Fact]
    public async Task GetAllAsync_SerializesPropertyFilters_AndPropertySort()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, "{\"items\":[],\"totalCount\":0,\"page\":1,\"pageSize\":30}");
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var service = new NoteService(client, NullLogger<NoteService>.Instance);

        var defId = Guid.NewGuid();
        await service.GetAllAsync(
            sortBy: $"prop:{defId}",
            propertyFilters: [new PropertyFilter { DefinitionId = defId, Op = PropertyFilterOp.Gte, Value = "5" }]);

        Assert.NotNull(handler.RequestUri);
        Assert.Contains($"sortBy=prop:{defId}", handler.RequestUri!);
        Assert.Contains($"pf={defId}:gte:5", handler.RequestUri!);
    }

    [Fact]
    public async Task CreateDefinition_InvalidatesCache()
    {
        // First GET populates the cache; the create response then invalidates it, so the next GET
        // must hit the network again (two GETs total across the sequence).
        var handler = new SequenceHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var service = new PropertyService(client, NullLogger<PropertyService>.Instance);

        await service.GetDefinitionsAsync();                 // GET #1 (network)
        await service.GetDefinitionsAsync();                 // served from cache (no network)
        Assert.Equal(1, handler.GetCount);

        await service.CreateDefinitionAsync(new CreatePropertyDefinitionRequest("P", PropertyType.Text));
        await service.GetDefinitionsAsync();                 // GET #2 (cache invalidated)
        Assert.Equal(2, handler.GetCount);
    }

    private sealed class CapturingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = Uri.UnescapeDataString(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        public int GetCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
                GetCount++;
            var body = request.Method == HttpMethod.Get
                ? "[]"
                : "{\"id\":\"" + Guid.NewGuid() + "\",\"name\":\"P\",\"type\":0,\"sortOrder\":0,\"options\":[]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using Vanadium.Note.Web.Services;

namespace Vanadium.Note.Web
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("#app");
            builder.RootComponents.Add<HeadOutlet>("head::after");

            var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7711";
            builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(apiBaseUrl) });
            builder.Services.AddScoped<NoteService>();
            builder.Services.AddMudServices();

            await builder.Build().RunAsync();
        }
    }
}

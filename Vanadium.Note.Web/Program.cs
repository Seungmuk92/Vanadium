using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using Vanadium.Note.Web.Auth;
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

            builder.Services.AddScoped<TokenStore>();
            builder.Services.AddScoped<JwtAuthenticationStateProvider>();
            builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
                sp.GetRequiredService<JwtAuthenticationStateProvider>());
            builder.Services.AddAuthorizationCore();

            builder.Services.AddScoped<AuthService>();

            builder.Services.AddScoped(sp =>
            {
                var tokenStore = sp.GetRequiredService<TokenStore>();
                var authProvider = sp.GetRequiredService<JwtAuthenticationStateProvider>();
                var navigation = sp.GetRequiredService<NavigationManager>();
                var handler = new AuthTokenHandler(tokenStore, authProvider, navigation)
                {
                    InnerHandler = new HttpClientHandler()
                };
                return new HttpClient(handler) { BaseAddress = new Uri(apiBaseUrl) };
            });

            builder.Services.AddScoped<NoteService>();
            builder.Services.AddScoped<LabelService>();
            builder.Services.AddScoped<SettingsService>();
            builder.Services.AddScoped<ApiTokenService>();
            builder.Services.AddScoped<ThemeService>();
            builder.Services.AddScoped<KeyboardShortcutService>();
            builder.Services.AddScoped<QuickNavService>();
            builder.Services.AddScoped<DraftStore>();
            builder.Services.AddScoped<NetworkStatusService>();
            builder.Services.AddScoped<PendingSaveStore>();
            builder.Services.AddScoped<NoteClaimStore>();
            builder.Services.AddScoped<OfflineSaveQueue>();
            builder.Services.AddScoped<ConfirmService>();
            builder.Services.AddMudServices();

            var host = builder.Build();

            // Resolve connectivity BEFORE the first render (#211). Initialising this from
            // MainLayout's first OnAfterRenderAsync left a window — behind several interop
            // round-trips — in which the service still reported its optimistic "online"
            // default, so a save failing in that window was misclassified as a generic
            // error and never parked in the offline queue.
            await host.Services.GetRequiredService<NetworkStatusService>().InitAsync();

            await host.RunAsync();
        }
    }
}

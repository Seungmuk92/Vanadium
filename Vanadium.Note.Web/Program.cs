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
            builder.Services.AddScoped<ThemeService>();
            builder.Services.AddScoped<KeyboardShortcutService>();
            builder.Services.AddMudServices();

            await builder.Build().RunAsync();
        }
    }
}

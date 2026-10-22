using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Jorvis.Web;
using Jorvis.Web.Services;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["ApiBaseUrl"];
if (string.IsNullOrWhiteSpace(apiBaseUrl))
{
    apiBaseUrl = builder.HostEnvironment.BaseAddress;
}

builder.Services.AddScoped(sp => new HttpClient(
    new AuthenticationHandler(sp.GetRequiredService<NavigationManager>()) { InnerHandler = new HttpClientHandler() })
{
    BaseAddress = new Uri(apiBaseUrl)
});
builder.Services.AddScoped<AuthApiClient>();
builder.Services.AddScoped<ProjectApiClient>();
builder.Services.AddMudServices();
builder.Services.AddScoped<Jorvis.Web.Theming.ThemeState>();

await builder.Build().RunAsync();

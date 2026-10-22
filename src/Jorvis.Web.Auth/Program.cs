using Jorvis.Web.Auth;
using Jorvis.Web.Auth.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<AuthApi>();
builder.Services.AddScoped<PasskeyBrowser>();
builder.Services.AddMudServices();
builder.Services.AddScoped<Jorvis.Web.Theming.ThemeState>();

await builder.Build().RunAsync();

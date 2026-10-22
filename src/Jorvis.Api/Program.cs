using Jorvis.Api;
using Jorvis.Api.Auth;
using Jorvis.Infrastructure.Data;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

const string WebCorsPolicy = "WebClient";

// Origins allowed to call the API from another origin (the local dev servers). In production the web app and API share
// one origin behind nginx, so none are needed.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["https://localhost:7290", "http://localhost:5200"];

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.AddDbContext<JorvisDatabaseContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Database")));

builder.Services.AddHealthChecks();
builder.Services.AddJorvisAuth(builder.Configuration, allowedOrigins);

builder.Services.AddCors(options =>
{
    options.AddPolicy(WebCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

// The API is only reachable through nginx (published on loopback only), so trust its forwarded client IP and scheme.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// Bring the schema up to date on every start. (The tests run on SQLite and build the schema from the model instead.)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<JorvisDatabaseContext>();
    if (db.Database.IsNpgsql())
        db.Database.Migrate();
}

if (await AdminCommands.TryRunAsync(app, args) is { } exitCode)
    return exitCode;

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors(WebCorsPolicy);
app.UseCsrfProtection(allowedOrigins);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapAuthEndpoints();
app.MapProjectEndpoints();
app.MapLaneEndpoints();
app.MapTaskEndpoints();

app.Run();
return 0;

public partial class Program;

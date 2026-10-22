using System.Net.Http.Json;
using Jorvis.Api.Auth;
using Jorvis.Infrastructure.Data;
using Jorvis.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jorvis.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    // An in-memory SQLite database lives only as long as its connection, so keep one open per factory.
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    /// <summary>Extra configuration for a test, set before the first client is created.</summary>
    public Dictionary<string, string?> Settings { get; } = [];

    /// <summary>Extra service overrides for a test, set before the first client is created.</summary>
    public Action<IServiceCollection>? ConfigureTestServices { get; set; }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        _connection.Open();
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<JorvisDatabaseContext>().Database.EnsureCreated();
        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection.Dispose();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // Tests make many sign-ins from one address; the rate limit has its own test.
        Settings.TryAdd("Auth:RateLimitPerMinute", "1000");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Settings));
        builder.ConfigureServices(services =>
        {
            ConfigureTestServices?.Invoke(services);

            // EF registers provider configuration through an internal generic type, so match by name.
            var providerRegistrations = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<JorvisDatabaseContext>)
                    || d.ServiceType.FullName?.Contains("IDbContextOptionsConfiguration", StringComparison.Ordinal) == true)
                .ToList();
            foreach (var descriptor in providerRegistrations)
                services.Remove(descriptor);

            services.AddDbContext<JorvisDatabaseContext>(options => options.UseSqlite(_connection));
        });
    }

    /// <summary>Creates an invite directly, the same way the admin CLI does, and returns its secret token.</summary>
    public async Task<string> CreateInviteAsync(string? email = null, TimeSpan? lifetime = null, InvitePurpose purpose = InvitePurpose.Join)
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<InviteService>()
            .CreateAsync(email, lifetime ?? TimeSpan.FromHours(1), CancellationToken.None, purpose);
    }

    /// <summary>Joins with a new passkey through the real two-step flow, which also signs the client in.</summary>
    public async Task<TestAuthenticator> SignInAsync(HttpClient client, string email = "owner@example.com", string username = "owner")
    {
        var ct = TestContext.Current.CancellationToken;
        var authenticator = new TestAuthenticator();
        var token = await CreateInviteAsync();

        var options = await client.PostAsJsonAsync("/api/auth/join/start", new JoinOptionsRequest(token, email, username), ct);
        options.EnsureSuccessStatusCode();
        var credential = authenticator.Create(await options.Content.ReadAsStringAsync(ct));

        var joined = await client.PostAsJsonAsync("/api/auth/join/finish", new { token, email, username, credential }, ct);
        joined.EnsureSuccessStatusCode();
        return authenticator;
    }
}

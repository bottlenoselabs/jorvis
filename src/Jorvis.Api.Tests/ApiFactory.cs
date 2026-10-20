using Jorvis.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jorvis.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            // EF registers provider configuration through an internal generic type, so match by name.
            var providerRegistrations = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                    || d.ServiceType.FullName?.Contains("IDbContextOptionsConfiguration", StringComparison.Ordinal) == true)
                .ToList();
            foreach (var descriptor in providerRegistrations)
                services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }
}

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Tests.IntegrationTests;

namespace BetaGiaiPhong.Tests.WebTests;

public class CustomWebApplicationFactory(TestDatabaseFixture fixture) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<BetaDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<BetaDbContext>(options =>
                options.UseSqlServer(fixture.ConnectionString));
        });

        builder.UseEnvironment("Development");
    }
}

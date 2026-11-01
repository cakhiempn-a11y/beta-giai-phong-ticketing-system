using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Tests.IntegrationTests;
using BetaGiaiPhong.Web.Services.Workers;

namespace BetaGiaiPhong.Tests.WebTests;

public class CustomWebApplicationFactory(TestDatabaseFixture fixture) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BetaDb"] = fixture.ConnectionString,
                ["BGP_QR_AES_KEY_B64"] = TestDatabaseFixture.TestQrAesKeyB64,
                ["BGP_MOCK_HMAC_KEY_B64"] = TestDatabaseFixture.TestHmacKeyB64,
                ["BGP_ALLOW_DEMO_SEED"] = "true",
                ["BGP_DEMO_PASSWORD"] = "DemoPassword123!"
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<BetaDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<BetaDbContext>(options =>
                options.UseSqlServer(fixture.ConnectionString));

            // Isolate background worker in Web integration tests to avoid race conditions with test state
            var workerDescriptor = services.SingleOrDefault(d => d.ImplementationType == typeof(ReservationExpiryWorker));
            if (workerDescriptor != null)
            {
                services.Remove(workerDescriptor);
            }
        });

        builder.UseEnvironment("Development");
    }
}

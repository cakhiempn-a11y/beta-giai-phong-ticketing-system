using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Data.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BetaGiaiPhong.Data;

public static class ServiceRegistration
{
    // Register in an ASP.NET Core application via builder.Services.AddBetaCinemaData(connection).
    // RetryOnFailure is intentionally OFF: explicit transactions require an execution
    // strategy that retries the WHOLE business operation, not isolated SQL statements.
    public static IServiceCollection AddBetaCinemaData(this IServiceCollection services, string connection)
    {
        services.AddDbContext<BetaDbContext>(options => options.UseSqlServer(connection));
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<QrTokenProtector>();
        services.AddSingleton(sp =>
        {
            var config = sp.GetService<IConfiguration>();
            var opts = MovieContentSyncOptions.FromEnvironment();
            if (string.IsNullOrWhiteSpace(opts.TmdbReadToken) && config is not null)
            {
                opts.TmdbReadToken = config["BGP_TMDB_READ_TOKEN"];
            }
            if (string.IsNullOrWhiteSpace(opts.MediaCacheDirectory) && config is not null)
            {
                opts.MediaCacheDirectory = config["BGP_MEDIA_CACHE_DIR"];
            }
            return opts;
        });
        services.AddScoped<ShowtimePublishingService>();
        services.AddScoped<SeatReservationService>();
        services.AddScoped<OrderLifecycleService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<TicketScanningService>();
        services.AddScoped<MovieManagementService>();
        services.AddScoped<ManagementQueryService>();
        services.AddScoped<MovieContentSyncService>();
        return services;
    }
}

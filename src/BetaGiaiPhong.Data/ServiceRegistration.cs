using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Data.Services;
using Microsoft.EntityFrameworkCore;
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
        services.AddScoped<ShowtimePublishingService>();
        services.AddScoped<SeatReservationService>();
        services.AddScoped<OrderLifecycleService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<TicketScanningService>();
        services.AddScoped<MovieManagementService>();
        services.AddScoped<ManagementQueryService>();
        return services;
    }
}

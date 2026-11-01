using BetaGiaiPhong.Data.Services;

namespace BetaGiaiPhong.Web.Services.Workers;

public sealed class ReservationExpiryWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<ReservationExpiryWorker> logger) : BackgroundService
{
    private readonly SemaphoreSlim _executionLock = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var isTesting = environment.IsEnvironment("Testing");
        var isExplicitlyEnabled = configuration.GetValue<bool>("BGP_ENABLE_EXPIRY_WORKER", false);

        if (isTesting && !isExplicitlyEnabled)
        {
            logger.LogInformation("ReservationExpiryWorker is disabled in Testing environment.");
            return;
        }

        if (!isExplicitlyEnabled && !isTesting)
        {
            logger.LogInformation("ReservationExpiryWorker is disabled via configuration (BGP_ENABLE_EXPIRY_WORKER=false).");
            return;
        }

        var intervalSec = configuration.GetValue<int>("BGP_EXPIRY_INTERVAL_SECONDS", 30);
        if (intervalSec < 1) intervalSec = 30;

        var batchSize = configuration.GetValue<int>("BGP_EXPIRY_BATCH_SIZE", 100);
        if (batchSize is < 1 or > 1000) batchSize = 100;

        logger.LogInformation(
            "ReservationExpiryWorker started (Interval: {IntervalSec}s, BatchSize: {BatchSize}).",
            intervalSec, batchSize);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSec));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);

                // Ensure single execution per tick, prevent overlapping cycles
                if (!await _executionLock.WaitAsync(0, stoppingToken))
                {
                    logger.LogWarning("Previous reservation expiry cycle is still running. Skipping current tick.");
                    continue;
                }

                try
                {
                    await ProcessExpiredOrdersAsync(batchSize, stoppingToken);
                }
                finally
                {
                    _executionLock.Release();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An unexpected error occurred during reservation expiry cycle.");
            }
        }

        logger.LogInformation("ReservationExpiryWorker has stopped.");
    }

    public async Task<int> ProcessExpiredOrdersAsync(int batchSize, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var lifecycleService = scope.ServiceProvider.GetRequiredService<OrderLifecycleService>();

        var count = await lifecycleService.ExpireDueAsync(batchSize, ct);
        if (count > 0)
        {
            logger.LogInformation("ReservationExpiryWorker swept and expired {Count} overdue reservation(s).", count);
        }
        return count;
    }
}

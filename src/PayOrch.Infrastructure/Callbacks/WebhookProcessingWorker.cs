using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PayOrch.Application.Common;
using PayOrch.Application.Orders;
using PayOrch.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
namespace PayOrch.Infrastructure.Callbacks;
using System.Net.Http;

/// <summary>
/// Continuously claims batches of already-verified webhook_events rows
/// (SELECT ... FOR UPDATE SKIP LOCKED — see EfUnitOfWork.ClaimPendingWebhookEventsAsync)
/// and runs them through WebhookProcessingService. Safe to scale out to
/// multiple instances: the claim step guarantees each event is only ever
/// picked up by one worker at a time, and a crashed worker's claim expires
/// after LeaseTimeout so another instance can pick the event back up.
/// </summary>
public class WebhookProcessingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WebhookProcessingWorker> _logger;
    private const int BatchSize = 50;
    private static readonly TimeSpan LeaseTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    public WebhookProcessingWorker(IServiceScopeFactory scopeFactory, ILogger<WebhookProcessingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int processedCount;
            try
            {
                processedCount = await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Webhook processing batch failed");
                processedCount = 0;
            }

            // Back off only when there was nothing to do — under load this
            // loop should be claiming a full batch back-to-back with no delay.
            if (processedCount == 0)
                await Task.Delay(PollInterval, stoppingToken);
        }
    }
    private async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        IReadOnlyList<WebhookEvent> claimed;
        using (var claimScope = _scopeFactory.CreateScope())
        {
            var claimUow = claimScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            claimed = await claimUow.ClaimPendingWebhookEventsAsync(BatchSize, LeaseTimeout, ct);
        }

        if (claimed.Count == 0) return 0;

        // Each event gets its own DbContext scope so one failure can never
        // leave a partially-tracked entity graph that corrupts the next
        // event's SaveChanges in the same batch.
        foreach (var evt in claimed)
        {
            using var eventScope = _scopeFactory.CreateScope();
            var processor = eventScope.ServiceProvider.GetRequiredService<WebhookProcessingService>();
            try
            {
                await processor.ProcessOneAsync(evt, ct);
            }
            catch (Exception ex)
            {
                // Leave processed_at null so the lease simply expires and
                // another pass (this worker or another instance) retries it
                // after LeaseTimeout — no event is ever silently dropped.
                _logger.LogError(ex, "Failed to process webhook event {EventId}", evt.Id);
            }
        }

        return claimed.Count;
    }
}

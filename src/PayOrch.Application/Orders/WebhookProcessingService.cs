using System.Text.Json;
using PayOrch.Application.Common;
using PayOrch.Application.Psp;
using PayOrch.Domain.Entities;

namespace PayOrch.Application.Orders;

/// <summary>
/// Phase 2 of webhook handling — everything that used to run inline in the
/// request. Called by WebhookProcessingWorker once it has claimed a batch of
/// already-verified webhook_events rows via SELECT ... FOR UPDATE SKIP
/// LOCKED. Safe to run from multiple worker instances concurrently: each
/// event is only ever claimed by one worker at a time, and the ledger write
/// inside ApplyLedgerEntryAsync takes its own row lock on the wallet.
/// </summary>
public class WebhookProcessingService
{
    private readonly IUnitOfWork _uow;
    private readonly IPspAdapterResolver _pspResolver;
    private readonly IOrderLifecycleNotifier _notifier;

    public WebhookProcessingService(IUnitOfWork uow, IPspAdapterResolver pspResolver, IOrderLifecycleNotifier notifier)
    {
        _uow = uow;
        _pspResolver = pspResolver;
        _notifier = notifier;
    }

    public async Task ProcessOneAsync(WebhookEvent evt, CancellationToken ct = default)
    {
        if (evt.PspOrderId is null)
        {
            await _uow.MarkWebhookEventProcessedAsync(evt.Id, null, ct);
            await _uow.SaveChangesAsync(ct);
            return;
        }

        var order = await _uow.GetOrderByPspOrderIdAsync(evt.PspOrderId, ct);
        if (order is null)
        {
            await _uow.MarkWebhookEventProcessedAsync(evt.Id, null, ct);
            await _uow.SaveChangesAsync(ct);
            return;
        }

        // Already terminal — defends against out-of-order/duplicate delivery
        // (two events for the same order claimed by different workers).
        if (order.Status is OrderStatus.Succeeded or OrderStatus.Failed)
        {
            await _uow.MarkWebhookEventProcessedAsync(evt.Id, order.Id, ct);
            await _uow.SaveChangesAsync(ct);
            return;
        }

        order.PayerVpaMasked = evt.PayerVpaMasked ?? order.PayerVpaMasked;
        order.PspPaymentId = evt.PspPaymentId ?? order.PspPaymentId;

        if (evt.Outcome == PspEventOutcome.Success)
        {
            order.Status = OrderStatus.Succeeded;
            order.CompletedAt = DateTimeOffset.UtcNow;
            await _uow.UpdateOrderAsync(order, ct);

            var wallet = await _uow.GetOrCreateWalletAsync(order.TenantId, ct);
            await _uow.ApplyLedgerEntryAsync(wallet.Id, LedgerDirection.Credit, order.Amount, "deposit", order.Id, ct);

            if (order.SellerId is Guid sellerId)
            {
                var adapter = _pspResolver.GetByCode(evt.PspCode);
                await TryCreateSellerTransferAsync(order, sellerId, adapter, ct);
            }

            if (!await _uow.CallbackRelayExistsAsync(order.Id, "success", ct))
                await _uow.EnqueueCallbackRelayAsync(await BuildRelayAsync(order, "success", ct), ct);
        }
        else if (evt.Outcome == PspEventOutcome.Failed)
        {
            order.Status = OrderStatus.Failed;
            order.CompletedAt = DateTimeOffset.UtcNow;
            await _uow.UpdateOrderAsync(order, ct);

            if (!await _uow.CallbackRelayExistsAsync(order.Id, "failed", ct))
                await _uow.EnqueueCallbackRelayAsync(await BuildRelayAsync(order, "failed", ct), ct);
        }

        await _uow.MarkWebhookEventProcessedAsync(evt.Id, order.Id, ct);
        await _uow.SaveChangesAsync(ct);

        // Fire the live dashboard update only after the DB commit above, so
        // a connected Admin/Sub-Admin screen never shows a status the
        // database hasn't actually persisted yet.
        if (order.Status is OrderStatus.Succeeded or OrderStatus.Failed)
        {
            await _notifier.NotifyOrderStatusChangedAsync(
                order.TenantId, order.Id, order.Status.ToString(), order.Amount, order.Currency, ct);
        }
    }

    private async Task TryCreateSellerTransferAsync(Order order, Guid sellerId, IPspAdapter adapter, CancellationToken ct)
    {
        var seller = await _uow.GetSellerByIdAsync(sellerId, ct);
        if (seller is null || seller.TenantId != order.TenantId || seller.Status != SellerOnboardingStatus.Active || seller.PspLinkedAccountId is null)
        {
            // Seller isn't activated on the PSP yet — the full amount stays
            // in the platform wallet until an Admin resolves onboarding.
            // A reconciliation job should sweep these and retry.
            return;
        }

        if (order.PspPaymentId is null) return;

        if (await _uow.SellerTransferExistsAsync(order.Id, sellerId, ct)) return;

        var sellerAmount = Math.Round(order.Amount * (1 - seller.CommissionPercent / 100m), 2);
        var commission = order.Amount - sellerAmount;

        var transferResult = await adapter.CreateTransferAsync(
            new PspTransferRequest(order.PspPaymentId, seller.PspLinkedAccountId, sellerAmount, commission, order.Currency), ct);

        await _uow.AddSellerTransferAsync(new SellerTransfer
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            SellerId = sellerId,
            PspTransferId = transferResult.PspTransferId,
            SellerAmount = sellerAmount,
            PlatformCommission = commission,
            Status = transferResult.Success ? TransferStatus.Processed : TransferStatus.Failed,
            CreatedAt = DateTimeOffset.UtcNow,
            SettledAt = transferResult.Success ? DateTimeOffset.UtcNow : null
        }, ct);
    }

    private async Task<CallbackRelay> BuildRelayAsync(Order order, string outcome, CancellationToken ct)
    {
        var tenant = await _uow.GetTenantByIdAsync(order.TenantId, ct);

        var payload = JsonSerializer.Serialize(new
        {
            orderId = order.Id,
            merchantOrderRef = order.MerchantOrderRef,
            amount = order.Amount,
            currency = order.Currency,
            status = outcome,
            completedAt = order.CompletedAt
        });

        return new CallbackRelay
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            TenantId = order.TenantId,
            TargetUrl = tenant?.CallbackUrl ?? string.Empty,
            PayloadJson = payload,
            NextAttemptAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}

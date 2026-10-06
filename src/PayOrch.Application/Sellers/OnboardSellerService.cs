using PayOrch.Application.Common;
using PayOrch.Application.Psp;
using PayOrch.Domain.Entities;

namespace PayOrch.Application.Sellers;

public record OnboardSellerCommand(
    Guid TenantId,
    Guid AdminUserId,
    string BusinessName,
    string ContactPhone,
    string ContactEmail,
    string? UpiCollectionApp,   // informational only — see Seller.UpiCollectionApp
    decimal CommissionPercent
);

public record OnboardSellerResponse(bool Success, string? ErrorMessage, Guid? SellerId, string? SellerCode, SellerOnboardingStatus? Status);

/// <summary>
/// Admin-driven onboarding (per BhandaarBox's chosen flow: Admin adds each
/// local merchant manually — no seller self-registration in Phase 1).
/// Immediately requests a Razorpay Linked Account so the seller can start
/// receiving split transfers as soon as Razorpay activates it (Razorpay's
/// own KYC review, not something BhandaarBox performs or can skip).
/// </summary>
public class OnboardSellerService
{
    private readonly IUnitOfWork _uow;
    private readonly IPspAdapterResolver _pspResolver;

    public OnboardSellerService(IUnitOfWork uow, IPspAdapterResolver pspResolver)
    {
        _uow = uow;
        _pspResolver = pspResolver;
    }

    public async Task<OnboardSellerResponse> HandleAsync(OnboardSellerCommand cmd, CancellationToken ct = default)
    {
        var tenant = await _uow.GetTenantByIdAsync(cmd.TenantId, ct);
        if (tenant is null)
            return new OnboardSellerResponse(false, "Unknown tenant.", null, null, null);
        if (tenant.Status != TenantStatus.Active || !tenant.IsActive)
            return new OnboardSellerResponse(false, "The website must be active before sellers can be onboarded.", null, null, null);
        if (cmd.CommissionPercent is < 0 or > 100)
            return new OnboardSellerResponse(false, "Commission must be between 0 and 100.", null, null, null);

        var sellerCode = await _uow.GenerateNextSellerCodeAsync(cmd.TenantId, ct);

        var seller = new Seller
        {
            Id = Guid.NewGuid(),
            TenantId = cmd.TenantId,
            OnboardedBy = cmd.AdminUserId,
            SellerCode = sellerCode,
            BusinessName = cmd.BusinessName,
            ContactPhone = cmd.ContactPhone,
            ContactEmail = cmd.ContactEmail,
            UpiCollectionApp = cmd.UpiCollectionApp,
            PspCode = "",
            CommissionPercent = cmd.CommissionPercent,
            Status = SellerOnboardingStatus.PendingReview,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            var provider = await _uow.GetDefaultPaymentProviderAsync(cmd.TenantId, ct);
            if (provider is null)
            {
                seller.StatusReason = "No active payment provider is configured for this website.";
                await _uow.AddSellerAsync(seller, ct);
                await _uow.SaveChangesAsync(ct);
                return new OnboardSellerResponse(true, seller.StatusReason, seller.Id, seller.SellerCode, seller.Status);
            }
            seller.PspCode = provider.ProviderCode;
            var adapter = _pspResolver.GetByCode(provider.ProviderCode);
            var linkResult = await adapter.CreateLinkedAccountAsync(
                new PspLinkedAccountRequest(cmd.BusinessName, cmd.ContactEmail, cmd.ContactPhone, seller.Id.ToString()), ct);

            if (linkResult.Success)
            {
                seller.PspLinkedAccountId = linkResult.PspLinkedAccountId;
                seller.Status = linkResult.Status == "activated" ? SellerOnboardingStatus.Active : SellerOnboardingStatus.SubmittedToPsp;
            }
            else seller.StatusReason = linkResult.ErrorMessage;

            await _uow.AddSellerAsync(seller, ct);
            await _uow.SaveChangesAsync(ct);
            return new OnboardSellerResponse(true, linkResult.Success ? null : $"Seller saved, but PSP onboarding needs attention: {linkResult.ErrorMessage}", seller.Id, seller.SellerCode, seller.Status);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            seller.StatusReason = $"PSP onboarding unavailable: {ex.Message}";
            await _uow.AddSellerAsync(seller, ct);
            await _uow.SaveChangesAsync(ct);
            return new OnboardSellerResponse(true, seller.StatusReason, seller.Id, seller.SellerCode, seller.Status);
        }

    }
}

using PayOrch.Application.Common;
using PayOrch.Domain.Entities;

namespace PayOrch.Application.Sellers;

/// <summary>Sub-Admin projection: no business name, no contact details, no PSP linked-account id.</summary>
public record SellerSummaryDto(Guid Id, string Label, SellerOnboardingStatus Status, decimal CommissionPercent);

/// <summary>Admin/Super Admin projection: full record, including the real business name.</summary>
public record SellerFullDto(Guid Id, string SellerCode, string BusinessName, string ContactPhone, string? ContactEmail,
    string? PspLinkedAccountId, SellerOnboardingStatus Status, decimal CommissionPercent);

/// <summary>
/// Mirrors the DB-layer masking (v_sellers_subadmin) at the application
/// layer too — defense in depth, and the shape the API actually returns.
/// A Sub-Admin NEVER receives a SellerFullDto, regardless of which query
/// path is hit.
/// </summary>
public class ListSellersService
{
    private readonly IUnitOfWork _uow;

    public ListSellersService(IUnitOfWork uow) => _uow = uow;

    public async Task<object> HandleAsync(CallerContext caller, Guid tenantId, CancellationToken ct = default)
    {
        var sellers = await _uow.ListSellersForTenantAsync(tenantId, ct);

        if (caller.Role == UserRole.SubAdmin)
        {
            return sellers.Select(s => new SellerSummaryDto(s.Id, $"Merchant {s.SellerCode}", s.Status, s.CommissionPercent)).ToList();
        }

        return sellers.Select(s => new SellerFullDto(
            s.Id, s.SellerCode, s.BusinessName, s.ContactPhone, s.ContactEmail, s.PspLinkedAccountId, s.Status, s.CommissionPercent)).ToList();
    }
}

namespace PayOrch.Domain.Entities;

public class Wallet
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>
    /// Derived balance snapshot for fast reads. The source of truth is
    /// always the ledger — never mutate this field directly; recompute
    /// it inside the same transaction that inserts a LedgerEntry.
    /// </summary>
    public decimal Balance { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum LedgerDirection
{
    Credit = 1,
    Debit = 2
}

public class LedgerEntry
{
    public long Id { get; set; }
    public Guid WalletId { get; set; }
    public LedgerDirection Direction { get; set; }
    public decimal Amount { get; set; }
    public string RefType { get; set; } = default!;   // deposit | commission | payout | payout_reversal
    public Guid RefId { get; set; }
    public decimal BalanceAfter { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

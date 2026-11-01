namespace SolanaTracker.Infrastructure.Persistence;

public sealed class TransactionEntity
{
    public required string Signature { get; set; }
    public ulong Slot { get; set; }
    public DateTimeOffset? BlockTime { get; set; }
    public bool Succeeded { get; set; }
    // JSON text is stored as jsonb; Error is SQL NULL on success.
    public string? Error { get; set; }
    public required string SourceData { get; set; }
    public DateTimeOffset IngestedAt { get; set; }
    public ICollection<TransferEntity> Transfers { get; set; } = new List<TransferEntity>();
}

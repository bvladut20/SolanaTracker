namespace SolanaTracker.Infrastructure.Persistence;

public sealed class TransferEntity
{
    public required string Signature { get; set; }
    public int InstructionIndex { get; set; }
    public required string Sender { get; set; }
    public required string Recipient { get; set; }
    public ulong Lamports { get; set; }
    public TransactionEntity Transaction { get; set; } = null!;
}

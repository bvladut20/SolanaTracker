namespace SolanaTracker.Domain;

// A completed transfer. Failed transaction outcomes remain source transaction metadata,
// not completed transfer records. Amounts stay in integer lamports to avoid rounding.
public sealed record SolTransfer(
    string Signature,
    ulong Slot,
    int InstructionIndex,
    string Sender,
    string Recipient,
    ulong Lamports,
    long? BlockTime);

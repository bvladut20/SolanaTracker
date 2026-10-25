using System.Text.Json;
using SolanaTracker.Domain;

namespace SolanaTracker.Application;

public interface ITransferExtractor
{
    // Input is getTransaction's result, not its JSON-RPC envelope. The caller must
    // request finalized data; the transaction result does not prove commitment.
    // Malformed/unsupported input throws JsonException instead of reporting zero transfers.
    IReadOnlyList<SolTransfer> Extract(JsonElement transaction, IReadOnlySet<string> monitoredWallets);
}

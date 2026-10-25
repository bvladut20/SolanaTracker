using Microsoft.Extensions.Options;
using SolanaTracker.Application;
using SolanaTracker.Infrastructure.Solana;

namespace SolanaTracker.ConsoleApp;

public sealed class Explorer(
    IOptions<ExplorerOptions> configuredOptions,
    SolanaRpcClient rpc,
    ITransferExtractor extractor)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var options = configuredOptions.Value;
        Console.WriteLine("Solana RPC explorer — one pass, finalized commitment");
        Console.WriteLine($"Wallet: {options.WalletAddress}");
        Console.WriteLine();
        Console.WriteLine("1. getSlot: the highest finalized slot reported by this node");
        Console.WriteLine($"Finalized slot: {await rpc.GetSlotAsync(cancellationToken)}");
        Console.WriteLine("A slot is an opportunity to produce a block; some slots are skipped.");
        Console.WriteLine();
        Console.WriteLine($"2. getSignaturesForAddress: up to {options.SignatureLimit} transaction IDs, newest first");
        var signatures = await rpc.GetSignaturesForAddressAsync(options.WalletAddress, options.SignatureLimit, cancellationToken);
        ConsoleOutput.PrintSignatures(signatures);
        if (signatures.Count == 0)
        {
            Console.WriteLine("No finalized transactions returned for this address. Try a wallet with Devnet activity.");
            return;
        }
    
        Console.WriteLine();
        Console.WriteLine("3. getTransaction: inspect the newest listed transaction using jsonParsed encoding");
        var signature = signatures[0].Signature;
        var transaction = await rpc.GetTransactionAsync(signature, cancellationToken);
        ConsoleOutput.PrintTransaction(signature, transaction);
        if (transaction is { } result)
        {
            var wallets = new HashSet<string>(StringComparer.Ordinal) { options.WalletAddress };
            ConsoleOutput.PrintTransfers(extractor.Extract(result, wallets));
        }
    }
}

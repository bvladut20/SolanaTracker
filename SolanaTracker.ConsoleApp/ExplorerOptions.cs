namespace SolanaTracker.ConsoleApp;

// Settings only: providers load values; the host registers binding and validation.
public sealed class ExplorerOptions
{
    public const string SectionName = "Explorer";

    public string RpcEndpoint { get; set; } = "https://api.devnet.solana.com";
    public string WalletAddress { get; set; } = "";
    public int SignatureLimit { get; set; } = 5;
}

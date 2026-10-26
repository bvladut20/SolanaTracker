# SolanaTracker

A Solana indexer learning project in C# and .NET 10. Currently a console exploration tool that runs once and exits.

## What it does

- Connects to a configurable Solana JSON-RPC endpoint, defaulting to Devnet.
- Shows the highest finalized slot and lists recent transaction signatures for one configured wallet.
- Retrieves **only the newest listed transaction**, then prints its outcome and top-level instructions.
- Extracts successful, parsed, top-level System Program instructions of type `transfer` involving the wallet.
- Reports each transfer's signature, slot, original instruction index, sender, recipient, integer lamports, and optional block time.

Finalized does not mean successful: failed transactions produce no completed transfers. A wallet reference does not necessarily indicate a transfer, so the newest transaction may contain none that are supported.

## What it does not do

- Extract inner transfers, token transfers, account creation, `transferWithSeed`, or other instruction types.
- Process every listed transaction or paginate wallet history.
- Save transactions, transfers, or progress to a database.
- Run continuously, retry automatically, or recover missed activity after restarting.
- Provide a REST API.

This is not yet a complete or reliable wallet transfer history.

## How to run

Install the **.NET 10 SDK** and use a **public wallet address with Devnet activity**. No private key or seed phrase is needed.

From the directory containing this README and `SolanaTracker.sln`:

```sh
dotnet run --project SolanaTracker.ConsoleApp -- YOUR_DEVNET_WALLET_ADDRESS
```

Alternatively, set the wallet in `SolanaTracker.ConsoleApp/appsettings.json`:

```json
{
  "Explorer": {
    "RpcEndpoint": "https://api.devnet.solana.com",
    "WalletAddress": "YOUR_DEVNET_WALLET_ADDRESS",
    "SignatureLimit": 5
  }
}
```

Then run without the wallet argument. `SignatureLimit` controls how many signatures are listed (1–1000), not how many transactions are inspected. The wallet must have activity on the selected network.

In **Rider**, open `SolanaTracker.sln`, select **SolanaTracker.ConsoleApp** as the run project, and use the configured wallet or supply it as a program argument. Settings are loaded beside the executable, so Rider's working directory does not matter.

# SolanaTracker

## Overview

SolanaTracker is a .NET 10 console application for single-pass inspection of finalized Solana transactions and extraction of SOL transfers involving a configured wallet.

The application lists recent transaction signatures, retrieves the newest listed transaction, and reports its outcome, top-level instructions, and supported transfers. Transfer records include the signature, slot, original instruction index, sender, recipient, integer lamport amount, and optional block time.

Extraction supports successful, parsed, top-level System Program instructions of type `transfer`. Failed transactions produce no completed transfer records.

## Prerequisites

- .NET 10 SDK.
- Access to a Solana JSON-RPC endpoint; Devnet is the default.
- A public wallet address with activity on the selected network. No private key is required.
- PostgreSQL for the database diagnostic. Docker Desktop with Compose can provide a local instance.

## Configuration

Settings are loaded from `SolanaTracker.ConsoleApp/appsettings.json` using the .NET Options pattern:

```json
{
  "Explorer": {
    "RpcEndpoint": "https://api.devnet.solana.com",
    "WalletAddress": "YOUR_DEVNET_WALLET_ADDRESS",
    "SignatureLimit": 5
  },
  "Database": {
    "ConnectionString": "Host=localhost;Port=55432;Database=solana_tracker;Username=solana_tracker;Password=solana_tracker_dev;Timeout=5;Command Timeout=5",
    "CheckConnection": false
  }
}
```

| Setting | Purpose |
| --- | --- |
| `Explorer:RpcEndpoint` | Solana JSON-RPC URL; must use HTTP or HTTPS. |
| `Explorer:WalletAddress` | Wallet whose transactions are inspected. Required for RPC execution. |
| `Explorer:SignatureLimit` | Number of signatures requested, from 1 to 1,000. Only the newest listed transaction is retrieved. |
| `Database:ConnectionString` | PostgreSQL connection parameters. Required for the database diagnostic. |
| `Database:CheckConnection` | Run the database diagnostic instead of Solana inspection. Defaults to `false`. |

Command-line settings override environment variables, which override JSON configuration. Environment variables use double underscores for section separators, for example `Database__ConnectionString` or `Explorer__SignatureLimit`.

Configuration is loaded beside the executable, independently of the working directory. Startup validates the settings required by the selected execution mode. RPC execution does not require PostgreSQL.

The included database credentials are for local development only. Supply deployment credentials through environment configuration or an appropriate secret provider.

## Running

Run commands from the directory containing this README and `SolanaTracker.sln`:

```sh
dotnet run --project SolanaTracker.ConsoleApp
```

Override the configured wallet with a positional argument:

```sh
dotnet run --project SolanaTracker.ConsoleApp -- YOUR_DEVNET_WALLET_ADDRESS
```

Alternatively, use named settings:

```sh
dotnet run --project SolanaTracker.ConsoleApp -- --Explorer:WalletAddress YOUR_DEVNET_WALLET_ADDRESS --Explorer:SignatureLimit 10
```

Use either a single positional wallet argument or named settings. The application runs once and exits. In Rider, open `SolanaTracker.sln` and select `SolanaTracker.ConsoleApp` as the run project.

## Database

Database access uses Npgsql and direct SQL. The current database operation is a read-only connectivity diagnostic; no application schema or persistence operations are implemented.

### Local instance

```sh
docker compose up -d --wait
docker compose ps
```

Compose runs PostgreSQL 18 at `127.0.0.1:55432` with a readiness health check. Data is stored in the Docker-managed `postgres_data` volume, mounted at `/var/lib/postgresql`.

| Parameter | Local value |
| --- | --- |
| Host | `localhost` |
| Port | `55432` |
| Database | `solana_tracker` |
| Username | `solana_tracker` |
| Password | `solana_tracker_dev` |

These parameters can also be used in Rider's Database tool window. A separately managed PostgreSQL instance can be used by overriding `Database:ConnectionString`.

Docker initializes credentials and the database when the data volume is empty. Editing the Compose environment does not update credentials in an existing database.

### Connectivity diagnostic

```sh
dotnet run --project SolanaTracker.ConsoleApp -- --Database:CheckConnection true
```

The diagnostic authenticates and queries the database name and server version. It makes no Solana requests and writes no schema or application data. Success returns exit code 0; failure returns exit code 1. This execution mode is temporary pending integration of database access into ingestion.

For interactive SQL access:

```sh
docker compose exec postgres psql -U solana_tracker -d solana_tracker
```

### Shutdown

Stop the service:

```sh
docker compose stop postgres
```

Remove the containers and network while retaining the data volume:

```sh
docker compose down
```

Restart with `docker compose up -d --wait`. Removing the named volume deletes its database data.

## Testing

```sh
dotnet build SolanaTracker.sln
dotnet test SolanaTracker.sln
```

The xUnit suite runs offline using saved JSON fixtures and fake HTTP responses. Coverage includes transfer extraction, transaction outcomes, malformed input, configuration binding and validation, configuration-provider precedence, and console workflow orchestration.

Database configuration tests do not require PostgreSQL. Verify real connectivity separately with the database diagnostic. Database schema and persistence integration tests are not yet implemented.

## Current limitations

- Only the newest listed transaction is retrieved; history pagination is not implemented.
- Inner transfers, token transfers, account creation, `transferWithSeed`, and other instruction types are excluded.
- Transactions, transfers, and ingestion progress are not persisted.
- Continuous polling, automatic retries, restart recovery, and a REST API are not implemented.

The application does not provide a complete wallet transfer history.

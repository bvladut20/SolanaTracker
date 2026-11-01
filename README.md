# SolanaTracker

## Overview

SolanaTracker is a .NET 10 console application for single-pass inspection of finalized Solana transactions and extraction of SOL transfers involving a configured wallet.

The application lists recent transaction signatures, retrieves the newest listed transaction, and reports its outcome, top-level instructions, and supported transfers. Transfer records include the signature, slot, original instruction index, sender, recipient, integer lamport amount, and optional block time.

Extraction supports successful, parsed, top-level System Program instructions of type `transfer`. Failed transactions produce no completed transfer records.

## Prerequisites

- .NET 10 SDK.
- Access to a Solana JSON-RPC endpoint; Devnet is the default.
- A public wallet address with activity on the selected network. No private key is required.
- PostgreSQL for migrations, integration tests, and the database diagnostic. Docker Desktop with Compose can provide a local instance.

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
    "CheckConnection": false,
    "ApplyMigrationsOnStartup": true
  }
}
```

| Setting | Purpose |
| --- | --- |
| `Explorer:RpcEndpoint` | Solana JSON-RPC URL; must use HTTP or HTTPS. |
| `Explorer:WalletAddress` | Wallet whose transactions are inspected. Required for RPC execution. |
| `Explorer:SignatureLimit` | Number of signatures requested, from 1 to 1,000. Only the newest listed transaction is retrieved. |
| `Database:ConnectionString` | PostgreSQL connection parameters. Required for migrations and the database diagnostic. |
| `Database:CheckConnection` | Run the database diagnostic instead of Solana inspection. Defaults to `false`. |
| `Database:ApplyMigrationsOnStartup` | Apply pending EF migrations before normal execution. Enabled in the included configuration; defaults to `false` when omitted. Ignored in diagnostic mode. |

For personal overrides, edit `SolanaTracker.ConsoleApp/appsettings.local.json`. It is optional, ignored by Git, copied beside the executable when built, and excluded from publish output. Include only the settings you want to override, for example:

```json
{
  "Explorer": {
    "SignatureLimit": 10
  }
}
```

The local file overrides `appsettings.json` and any environment-specific JSON settings. Command-line settings override environment variables, which override the local file. Environment variables use double underscores for section separators, for example `Database__ConnectionString` or `Explorer__SignatureLimit`.

Configuration is loaded beside the executable, independently of the working directory. Startup validates the settings required by the selected execution mode. With startup migrations enabled, PostgreSQL must be available before RPC execution begins. With migrations disabled, RPC execution remains independent of PostgreSQL.

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

The persistence schema is defined by EF Core entities and Fluent API mappings in `SolanaTracker.Infrastructure`, using the Npgsql PostgreSQL provider. The initial migration creates:

- `transactions`: signature primary key, slot, nullable block time, execution outcome and error, source JSON, and ingestion time.
- `transfers`: composite primary key `(signature, instruction_index)`, transaction foreign key, sender, recipient, and lamports; sender and recipient indexes support wallet queries.

Slots and lamports use `numeric(20,0)` with unsigned 64-bit range checks and exact C# conversions. Transaction outcomes and errors must agree. Deleting a transaction with dependent transfers is restricted. The database model assumes one Solana network per database. The console does not yet write fetched transactions or extracted transfers.

### Startup migrations

With `Database:CheckConnection=false` and `Database:ApplyMigrationsOnStartup=true`, the host applies pending migrations before running the explorer. EF creates the target database if missing and the login has database-creation permission. Otherwise, provision the database separately. Schema changes require suitable DDL privileges. Migration failure aborts startup before Solana requests.

Set `Database:ApplyMigrationsOnStartup=false` to manage migrations separately. No automatic schema check or update runs in that mode. The diagnostic remains read-only regardless of this flag.

### EF tooling

Restore the pinned local tool once after checkout:

```sh
dotnet tool restore
```

Generate migrations after changing the entities or mappings, then review the generated operations:

```sh
dotnet ef migrations add MigrationName --project SolanaTracker.Infrastructure --startup-project SolanaTracker.ConsoleApp --context IndexerDbContext --output-dir Persistence/Migrations
```

Apply pending migrations explicitly when startup migration is disabled:

```sh
dotnet ef database update --project SolanaTracker.Infrastructure --startup-project SolanaTracker.ConsoleApp --context IndexerDbContext
```

These commands use `Database:ConnectionString` from configuration. Override the target through `Database__ConnectionString` or arguments after `--`. The design-time factory builds configuration without starting hosted services or executing RPC calls. Keep applied migrations unchanged; add a new migration for later schema changes. `EnsureCreated` is not used.

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
dotnet test SolanaTracker.Tests
```

Unit tests run offline using saved JSON fixtures and fake HTTP responses. Coverage includes transfer extraction, transaction outcomes, malformed input, configuration binding and validation, configuration-provider precedence, and console workflow orchestration.

Run integration tests against real PostgreSQL:

```sh
docker compose up -d --wait
dotnet test SolanaTracker.IntegrationTests
# Both projects:
dotnet test SolanaTracker.sln
```

Integration tests load `SolanaTracker.IntegrationTests/testsettings.json`, which defaults to the Compose server on port `55432`. To use your own local PostgreSQL instance, put its connection string in `SolanaTracker.IntegrationTests/testsettings.local.json` (ignored by Git):

```json
{
  "Database": {
    "ConnectionString": "Host=localhost;Port=5432;Username=YOUR_USER;Password=YOUR_PASSWORD;Timeout=5;Command Timeout=10"
  }
}
```

Run `dotnet test SolanaTracker.IntegrationTests`, or run `SchemaTests` in Rider. Both settings files are copied beside the test assembly, so the working directory does not matter. The local file overrides shared settings; `TEST_POSTGRES_CONNECTION_STRING` overrides both, including in CI. Test settings are separate from the application's `appsettings.json`.

The login must be allowed to create databases and connect to the `postgres` administrative database. Each test creates and removes only a uniquely named `solana_tests_*` database, independently of the application database. Tests fail if PostgreSQL is unavailable; they are not silently skipped.

Coverage includes missing-database creation, initial migration, safe reruns with existing data, exact unsigned-value round trips, keys, foreign keys, range and outcome constraints, wallet indexes, startup migration completion, rollback on failure, and read-only diagnostic behaviour. Integration tests make no Solana requests.

## Current limitations

- Only the newest listed transaction is retrieved; history pagination is not implemented.
- Inner transfers, token transfers, account creation, `transferWithSeed`, and other instruction types are excluded.
- Transactions, transfers, and ingestion progress are not persisted.
- Continuous polling, automatic retries, restart recovery, and a REST API are not implemented.

The application does not provide a complete wallet transfer history.

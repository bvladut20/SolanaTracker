using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using SolanaTracker.ConsoleApp;
using SolanaTracker.Infrastructure.Persistence;
using Xunit;

namespace SolanaTracker.IntegrationTests;

public sealed class SchemaTests
{
    [Fact]
    public async Task Migration_creates_missing_database_and_rerun_preserves_existing_rows()
    {
        await using var database = new TestDatabase();
        Assert.False(await database.ExistsAsync());
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        Assert.True(await database.ExistsAsync());
        Assert.Single(await context.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        context.Transactions.Add(Transaction("existing"));
        await context.SaveChangesAsync();

        await context.Database.MigrateAsync();
        Assert.Single(await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, await context.Transactions.CountAsync());
    }

    [Fact]
    public async Task Mappings_round_trip_full_unsigned_ranges_json_metadata_and_distinct_instruction_indexes()
    {
        await using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        var transaction = Transaction("success");
        transaction.Slot = ulong.MaxValue;
        transaction.BlockTime = DateTimeOffset.FromUnixTimeSeconds(1763747336);
        transaction.SourceData = "{\"slot\":18446744073709551615,\"meta\":{\"err\":null}}";
        transaction.Transfers.Add(Transfer("success", 0, 0));
        transaction.Transfers.Add(Transfer("success", 2, ulong.MaxValue));
        var failed = Transaction("failed");
        failed.Succeeded = false;
        failed.Error = "{\"InstructionError\":[0,\"InvalidArgument\"]}";
        context.AddRange(transaction, failed);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var stored = await context.Transactions.Include(t => t.Transfers).SingleAsync(t => t.Signature == "success");
        Assert.Equal(ulong.MaxValue, stored.Slot);
        Assert.Equal(transaction.BlockTime, stored.BlockTime);
        Assert.True(stored.IngestedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Equal(new ulong[] { 0, ulong.MaxValue }, stored.Transfers.OrderBy(t => t.InstructionIndex).Select(t => t.Lamports));
        Assert.Equal(new[] { 0, 2 }, stored.Transfers.OrderBy(t => t.InstructionIndex).Select(t => t.InstructionIndex));
        using var source = JsonDocument.Parse(stored.SourceData);
        Assert.Equal(ulong.MaxValue, source.RootElement.GetProperty("slot").GetUInt64());
        var storedFailure = await context.Transactions.Include(t => t.Transfers).SingleAsync(t => t.Signature == "failed");
        Assert.False(storedFailure.Succeeded);
        Assert.NotNull(storedFailure.Error);
        Assert.Null(storedFailure.BlockTime);
        Assert.Empty(storedFailure.Transfers);
    }

    [Theory]
    [InlineData("INSERT INTO transactions (signature,slot,succeeded,source_data) VALUES ('source',1,true,'{}')", "23505")]
    [InlineData("INSERT INTO transfers (signature,instruction_index,sender,recipient,lamports) VALUES ('source',0,'a','b',1)", "23505")]
    [InlineData("INSERT INTO transfers (signature,instruction_index,sender,recipient,lamports) VALUES ('missing',0,'a','b',1)", "23503")]
    [InlineData("INSERT INTO transfers (signature,instruction_index,sender,recipient,lamports) VALUES ('source',-1,'a','b',1)", "23514")]
    [InlineData("INSERT INTO transfers (signature,instruction_index,sender,recipient,lamports) VALUES ('source',1,'a','b',-1)", "23514")]
    [InlineData("INSERT INTO transfers (signature,instruction_index,sender,recipient,lamports) VALUES ('source',1,'a','b',18446744073709551616)", "23514")]
    [InlineData("INSERT INTO transactions (signature,slot,succeeded,source_data) VALUES ('bad',-1,true,'{}')", "23514")]
    [InlineData("INSERT INTO transactions (signature,slot,succeeded,source_data) VALUES ('bad',18446744073709551616,true,'{}')", "23514")]
    [InlineData("INSERT INTO transactions (signature,slot,succeeded,error,source_data) VALUES ('bad',1,true,'\"error\"','{}')", "23514")]
    [InlineData("INSERT INTO transactions (signature,slot,succeeded,source_data) VALUES ('bad',1,false,'{}')", "23514")]
    [InlineData("INSERT INTO transactions (signature,slot,succeeded,error,source_data) VALUES ('bad',1,false,'null','{}')", "23514")]
    [InlineData("INSERT INTO transactions (signature,slot,succeeded,source_data) VALUES ('bad',1,true,'not-json')", "22P02")]
    public async Task Database_enforces_keys_references_ranges_and_transaction_outcomes(string sql, string expectedCode)
    {
        await using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        var source = Transaction("source");
        source.Transfers.Add(Transfer("source", 0, 1));
        context.Add(source);
        await context.SaveChangesAsync();
        // Execute literal SQL directly: JSON braces are data, not EF format placeholders.
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(expectedCode, error.SqlState);
    }

    [Fact]
    public async Task Wallet_query_indexes_are_created()
    {
        await using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        var indexes = await context.Database.SqlQueryRaw<string>(
            "SELECT indexname AS \"Value\" FROM pg_indexes WHERE tablename = 'transfers'").ToListAsync();
        Assert.Contains("ix_transfers_sender", indexes);
        Assert.Contains("ix_transfers_recipient", indexes);
    }

    [Fact]
    public async Task Enabled_startup_migrations_finish_before_host_start_returns()
    {
        await using var database = new TestDatabase();
        using var host = BuildHost(database, migrate: true);
        await host.StartAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IndexerDbContext>();
        Assert.Single(await context.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        await host.StopAsync();
    }

    [Fact]
    public async Task Migration_failure_aborts_startup_and_rolls_back_schema_changes()
    {
        await using var database = new TestDatabase();
        await database.CreateEmptyAsync();
        await using var context = database.CreateContext();
        // The migration creates transactions before transfers. This conflict forces
        // a failure after its first table creation, exercising transactional rollback.
        await context.Database.ExecuteSqlRawAsync("CREATE TABLE transfers (conflict integer)");
        using var host = BuildHost(database, migrate: true);
        await Assert.ThrowsAsync<PostgresException>(() => host.StartAsync());
        var tableExists = await context.Database.SqlQueryRaw<bool>(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='public' AND table_name='transactions') AS \"Value\"")
            .SingleAsync();
        Assert.False(tableExists);
        Assert.Empty(await context.Database.GetAppliedMigrationsAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Disabled_migrations_or_diagnostic_mode_do_not_create_database(bool migrate, bool check)
    {
        await using var database = new TestDatabase();
        using var host = BuildHost(database, migrate, check);
        await host.StartAsync();
        Assert.False(await database.ExistsAsync());
        Assert.DoesNotContain(host.Services.GetServices<IHostedService>(), s => s is DatabaseMigrationService);
        await host.StopAsync();
    }

    [Fact]
    public async Task Design_time_factory_does_not_run_startup_migrations()
    {
        await using var database = new TestDatabase();
        await using var context = new IndexerDbContextFactory().CreateDbContext([
            "--Database:ConnectionString", database.ConnectionString,
            "--Database:CheckConnection", "false",
            "--Database:ApplyMigrationsOnStartup", "true"]);
        Assert.Equal(database.ConnectionString, context.Database.GetConnectionString());
        Assert.False(await database.ExistsAsync());
    }

    private static IHost BuildHost(TestDatabase database, bool migrate, bool check = false)
    {
        var builder = ExplorerHost.CreateBuilder([
            "--Database:ConnectionString", database.ConnectionString,
            "--Database:ApplyMigrationsOnStartup", migrate.ToString(),
            "--Database:CheckConnection", check.ToString(),
            "--Explorer:WalletAddress", "public-wallet"]);
        builder.Logging.ClearProviders();
        return builder.Build();
    }

    private static TransactionEntity Transaction(string signature) => new()
    {
        Signature = signature, Slot = 1, Succeeded = true, SourceData = "{}"
    };

    private static TransferEntity Transfer(string signature, int index, ulong lamports) => new()
    {
        Signature = signature, InstructionIndex = index, Sender = "a", Recipient = "b", Lamports = lamports
    };
}

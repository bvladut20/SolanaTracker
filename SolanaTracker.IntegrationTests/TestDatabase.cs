using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using SolanaTracker.Infrastructure.Persistence;

namespace SolanaTracker.IntegrationTests;

/// <summary>
/// Each instance owns a randomly named database, never the configured application
/// database. Test settings select the server; TEST_POSTGRES_CONNECTION_STRING overrides them.
/// The server login must be allowed to create databases. Disposal removes only this instance's database.
/// </summary>
internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly string _name = "solana_tests_" + Guid.NewGuid().ToString("N");
    private readonly string _adminConnectionString;
    public string ConnectionString { get; }

    public TestDatabase()
    {
        using var configuration = new ConfigurationManager();
        configuration.SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("testsettings.json", optional: false)
            .AddJsonFile("testsettings.local.json", optional: true);
        var connectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING")
            ?? configuration["Database:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Configure Database:ConnectionString in test settings or set TEST_POSTGRES_CONNECTION_STRING.");
        var settings = new NpgsqlConnectionStringBuilder(connectionString);
        settings.Database = "postgres";
        settings.Pooling = false;
        _adminConnectionString = settings.ConnectionString;
        settings.Database = _name;
        ConnectionString = settings.ConnectionString;
    }

    public IndexerDbContext CreateContext() => new(new DbContextOptionsBuilder<IndexerDbContext>()
        .UseNpgsql(ConnectionString).Options);

    public async Task CreateEmptyAsync()
    {
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{_name}\"", connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<bool> ExistsAsync()
    {
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = $1)", connection);
        command.Parameters.AddWithValue(_name);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        // The identifier is generated here, not supplied by application configuration.
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}

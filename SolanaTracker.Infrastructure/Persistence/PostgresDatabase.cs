using Npgsql;

namespace SolanaTracker.Infrastructure.Persistence;

public sealed record DatabaseConnectionInfo(string DatabaseName, string ServerVersion);

public sealed class PostgresDatabase(NpgsqlDataSource dataSource)
{
    /// <summary>
    /// Opens an authenticated connection and reads the database name and server version.
    /// Does not create or modify schema or data. Connection/query failures propagate to
    /// the caller; cancellation stops the check. The shared data source owns the pool.
    /// </summary>
    public async Task<DatabaseConnectionInfo> CheckConnectionAsync(CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT current_database(), current_setting('server_version')");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new DatabaseConnectionInfo(reader.GetString(0), reader.GetString(1));
    }
}

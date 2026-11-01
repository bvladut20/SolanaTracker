using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SolanaTracker.Infrastructure.Persistence;

namespace SolanaTracker.ConsoleApp;

/// <summary>
/// EF tooling uses application configuration without starting hosted services or RPC calls.
/// The returned context owns its connections independently of the temporary host.
/// </summary>
public sealed class IndexerDbContextFactory : IDesignTimeDbContextFactory<IndexerDbContext>
{
    public IndexerDbContext CreateDbContext(string[] args)
    {
        using var host = ExplorerHost.CreateBuilder(args).Build();
        var connectionString = host.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString;
        return new IndexerDbContext(new DbContextOptionsBuilder<IndexerDbContext>()
            .UseNpgsql(connectionString).Options);
    }
}

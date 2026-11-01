using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SolanaTracker.Infrastructure.Persistence;

namespace SolanaTracker.ConsoleApp;

/// <summary>
/// Applies pending migrations before host startup completes. Failure prevents the
/// caller from starting ingestion. A scope keeps DbContext separate from singleton services.
/// </summary>
public sealed class DatabaseMigrationService(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IndexerDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

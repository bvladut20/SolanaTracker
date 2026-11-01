using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using SolanaTracker.ConsoleApp;
using SolanaTracker.Infrastructure.Persistence;
using SolanaTracker.Infrastructure.Solana;

try
{
    var builder = ExplorerHost.CreateBuilder(args);
    var checkDatabase = builder.Configuration.GetValue<bool>("Database:CheckConnection");
    using var host = builder.Build();
    await host.StartAsync(); // Validate settings for the selected command before any network request.
    try
    {
        var cancellationToken = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
        // TODO: Temporary connectivity diagnostic; expected to be retired when database access
        // becomes part of ingestion.
        if (checkDatabase)
        {
            var info = await host.Services.GetRequiredService<PostgresDatabase>().CheckConnectionAsync(cancellationToken);
            Console.WriteLine($"PostgreSQL connection successful: database '{info.DatabaseName}', server {info.ServerVersion}.");
            Console.WriteLine("Read-only check completed. No schema or transfer data was written.");
        }
        else
        {
            await host.Services.GetRequiredService<Explorer>().RunAsync(cancellationToken);
        }
    }
    finally
    {
        await host.StopAsync();
    }
}
catch (NpgsqlException)
{
    // Driver error details may include connection credentials. Keep console errors
    // generic; the connection string can be checked locally without printing it.
    Console.Error.WriteLine("PostgreSQL operation failed. Check database availability, credentials, permissions, and migration logs.");
    Environment.ExitCode = 1;
}
catch (Exception ex) when (ex is ArgumentException or IOException or JsonException
    or HttpRequestException or OperationCanceledException or SolanaRpcException
    or OptionsValidationException or InvalidOperationException)
{
    Console.Error.WriteLine($"Explorer stopped: {ex.Message}");
    Console.Error.WriteLine("No progress is saved. Fix the configuration or try again later.");
    Environment.ExitCode = 1;
}

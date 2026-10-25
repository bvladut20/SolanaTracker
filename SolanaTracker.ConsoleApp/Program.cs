using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SolanaTracker.ConsoleApp;
using SolanaTracker.Infrastructure.Solana;

try
{
    using var host = ExplorerHost.CreateBuilder(args).Build();
    await host.StartAsync(); // ValidateOnStart runs before any RPC request.
    try
    {
        var cancellationToken = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
        await host.Services.GetRequiredService<Explorer>().RunAsync(cancellationToken);
    }
    finally
    {
        await host.StopAsync();
    }
}
catch (Exception ex) when (ex is ArgumentException or IOException or JsonException
    or HttpRequestException or OperationCanceledException or SolanaRpcException
    or OptionsValidationException or InvalidOperationException)
{
    Console.Error.WriteLine($"Explorer stopped: {ex.Message}");
    Console.Error.WriteLine("No progress is saved. Fix the configuration or try again later.");
    Environment.ExitCode = 1;
}

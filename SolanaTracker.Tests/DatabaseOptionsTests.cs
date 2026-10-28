using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using SolanaTracker.ConsoleApp;
using Xunit;

namespace SolanaTracker.Tests;

public sealed class DatabaseOptionsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "solana-database-options-" + Guid.NewGuid());

    public DatabaseOptionsTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Database_check_binds_options_without_requiring_a_wallet_or_opening_connections_at_startup()
    {
        // An unreachable host proves startup validates configuration without opening a connection.
        WriteSettings("Host=unreachable.invalid;Database=example;Username=example");
        using var host = Builder().Build();
        await host.StartAsync();
        var options = host.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        Assert.True(options.CheckConnection);
        Assert.Contains("Database=example", options.ConnectionString);
        var source = host.Services.GetRequiredService<NpgsqlDataSource>();
        Assert.Same(source, host.Services.GetRequiredService<NpgsqlDataSource>());
        await host.StopAsync();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Host=localhost;Username=example")]
    [InlineData("Host=localhost;Database=example")]
    [InlineData("Database=example;Username=example")]
    [InlineData("Host=localhost;Port=not-a-port;Password=secret")]
    [InlineData("UnknownOption=secret")]
    public async Task Invalid_database_configuration_fails_at_startup_without_exposing_its_value(string connectionString)
    {
        WriteSettings(connectionString);
        using var host = Builder().Build();
        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        Assert.Contains("Database:ConnectionString", error.Message);
        Assert.DoesNotContain("secret", error.Message);
    }

    [Fact]
    public async Task Rpc_exploration_does_not_require_database_configuration()
    {
        File.WriteAllText(Path.Combine(_directory, "appsettings.json"),
            "{\"Explorer\":{\"WalletAddress\":\"public-wallet\"}}");
        using var host = Builder(databaseCheck: false).Build();
        await host.StartAsync();
        Assert.NotNull(host.Services.GetRequiredService<Explorer>());
        await host.StopAsync();
    }

    [Fact]
    public async Task Command_line_connection_string_overrides_json()
    {
        WriteSettings("Host=localhost;Database=original;Username=example");
        var builder = ExplorerHost.CreateBuilder([
            "--Database:CheckConnection", "true",
            "--Database:ConnectionString", "Host=localhost;Database=overridden;Username=example"], _directory);
        builder.Logging.ClearProviders();
        using var host = builder.Build();
        await host.StartAsync();
        Assert.Contains("Database=overridden", host.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString);
        await host.StopAsync();
    }

    private Microsoft.Extensions.Hosting.HostApplicationBuilder Builder(bool databaseCheck = true)
    {
        var builder = ExplorerHost.CreateBuilder(
            databaseCheck ? ["--Database:CheckConnection", "true"] : [], _directory);
        builder.Logging.ClearProviders();
        return builder;
    }

    private void WriteSettings(string connectionString) => File.WriteAllText(
        Path.Combine(_directory, "appsettings.json"),
        JsonSerializer.Serialize(new { Database = new { ConnectionString = connectionString } }));

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}

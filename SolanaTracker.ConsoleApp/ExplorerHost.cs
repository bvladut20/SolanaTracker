using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SolanaTracker.Application;
using SolanaTracker.Infrastructure.Solana;
using SolanaTracker.Infrastructure.Persistence;
using Npgsql;
using Microsoft.EntityFrameworkCore;

namespace SolanaTracker.ConsoleApp;

public static class ExplorerHost
{
    // The optional content root lets tests exercise real configuration providers in
    // an isolated directory. At runtime, settings are loaded beside the executable.
    public static HostApplicationBuilder CreateBuilder(string[] args, string? contentRootPath = null)
    {
        var normalizedArgs = NormalizeArguments(args);
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = normalizedArgs,
            ContentRootPath = contentRootPath ?? AppContext.BaseDirectory
        });

        // Keep environment and command-line overrides above machine-local JSON settings.
        builder.Configuration.AddJsonFile("appsettings.local.json", optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(normalizedArgs);

        var checkDatabase = builder.Configuration.GetValue<bool>("Database:CheckConnection");
        var applyMigrations = !checkDatabase && builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup");
        var explorerOptions = builder.Services.AddOptions<ExplorerOptions>()
            .Bind(builder.Configuration.GetSection(ExplorerOptions.SectionName))
            .PostConfigure(options => options.WalletAddress = options.WalletAddress?.Trim() ?? "")
            .Validate(options => !string.IsNullOrWhiteSpace(options.WalletAddress),
                "Explorer:WalletAddress is required. Configure it or pass a wallet address as the first program argument.")
            .Validate(options => Uri.TryCreate(options.RpcEndpoint, UriKind.Absolute, out var endpoint)
                && (endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps),
                "Explorer:RpcEndpoint must be an absolute HTTP or HTTPS URL.")
            .Validate(options => options.SignatureLimit is >= 1 and <= 1000,
                "Explorer:SignatureLimit must be between 1 and 1000.");

        var databaseOptions = builder.Services.AddOptions<DatabaseOptions>()
            .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
            .Validate(options => IsValidConnectionString(options.ConnectionString),
                "Database:ConnectionString must specify a valid PostgreSQL host, database, and username.");

        // The diagnostic needs no wallet. Normal execution requires database settings
        // only when startup migrations are enabled.
        if (checkDatabase || applyMigrations)
            databaseOptions.ValidateOnStart();
        if (!checkDatabase)
            explorerOptions.ValidateOnStart();

        builder.Services.AddSingleton(services => NpgsqlDataSource.Create(
            services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));
        builder.Services.AddSingleton<PostgresDatabase>();
        builder.Services.AddDbContext<IndexerDbContext>((services, options) =>
            options.UseNpgsql(services.GetRequiredService<NpgsqlDataSource>()));
        if (applyMigrations)
            builder.Services.AddHostedService<DatabaseMigrationService>();

        // Factory-created singletons are disposed by the host at the end of this pass.
        builder.Services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        builder.Services.AddSingleton(services => new SolanaRpcClient(
            services.GetRequiredService<HttpClient>(),
            services.GetRequiredService<IOptions<ExplorerOptions>>().Value.RpcEndpoint));
        builder.Services.AddSingleton<ITransferExtractor, SystemTransferExtractor>();
        builder.Services.AddTransient<Explorer>();
        return builder;
    }

    private static bool IsValidConnectionString(string connectionString)
    {
        try
        {
            var settings = new NpgsqlConnectionStringBuilder(connectionString);
            return !string.IsNullOrWhiteSpace(settings.Host)
                && !string.IsNullOrWhiteSpace(settings.Database)
                && !string.IsNullOrWhiteSpace(settings.Username);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string[] NormalizeArguments(string[] args)
    {
        // A single positional argument sets the wallet. The standard command-line
        // provider handles named overrides such as --Explorer:SignatureLimit 10.
        if (args.Length == 0 || args[0].StartsWith('-'))
            return args;
        if (args.Length != 1)
            throw new ArgumentException("Pass one wallet address, or use named --Explorer:Setting value arguments.");
        return ["--Explorer:WalletAddress", args[0]];
    }
}

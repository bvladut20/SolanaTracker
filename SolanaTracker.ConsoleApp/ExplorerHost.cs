using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SolanaTracker.Application;
using SolanaTracker.Infrastructure.Solana;

namespace SolanaTracker.ConsoleApp;

public static class ExplorerHost
{
    // The optional content root lets tests exercise real configuration providers in
    // an isolated directory. Production loads settings beside the executable.
    public static HostApplicationBuilder CreateBuilder(string[] args, string? contentRootPath = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = NormalizeArguments(args),
            ContentRootPath = contentRootPath ?? AppContext.BaseDirectory
        });

        builder.Services.AddOptions<ExplorerOptions>()
            .Bind(builder.Configuration.GetSection(ExplorerOptions.SectionName))
            .PostConfigure(options => options.WalletAddress = options.WalletAddress?.Trim() ?? "")
            .Validate(options => !string.IsNullOrWhiteSpace(options.WalletAddress),
                "Explorer:WalletAddress is required. Configure it or pass a wallet address as the first program argument.")
            .Validate(options => Uri.TryCreate(options.RpcEndpoint, UriKind.Absolute, out var endpoint)
                && (endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps),
                "Explorer:RpcEndpoint must be an absolute HTTP or HTTPS URL.")
            .Validate(options => options.SignatureLimit is >= 1 and <= 1000,
                "Explorer:SignatureLimit must be between 1 and 1000.")
            .ValidateOnStart();

        // Factory-created singletons are disposed by the host at the end of this pass.
        builder.Services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        builder.Services.AddSingleton(services => new SolanaRpcClient(
            services.GetRequiredService<HttpClient>(),
            services.GetRequiredService<IOptions<ExplorerOptions>>().Value.RpcEndpoint));
        builder.Services.AddSingleton<ITransferExtractor, SystemTransferExtractor>();
        builder.Services.AddTransient<Explorer>();
        return builder;
    }

    private static string[] NormalizeArguments(string[] args)
    {
        // Preserve the original `app WALLET` convenience while letting the standard
        // command-line provider handle named overrides such as --Explorer:SignatureLimit 10.
        if (args.Length == 0 || args[0].StartsWith('-'))
            return args;
        if (args.Length != 1)
            throw new ArgumentException("Pass one wallet address, or use named --Explorer:Setting value arguments.");
        return ["--Explorer:WalletAddress", args[0]];
    }
}

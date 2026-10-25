using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SolanaTracker.ConsoleApp;
using Xunit;

namespace SolanaTracker.Tests;

// Exercise actual hosting, binding, provider precedence, and startup validation.
// Every test uses an isolated content root; no RPC network access is needed.
public sealed class ExplorerOptionsTests : IDisposable
{
    private const string Wallet = "9B5XszUGdMaxCZ7uSQhPzdks5ZQSmWxrmzCSvtJ6Ns6g";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "solana-options-" + Guid.NewGuid());

    public ExplorerOptionsTests()
    {
        Directory.CreateDirectory(_directory);
        WriteSettings(new { RpcEndpoint = "https://example.test/rpc", WalletAddress = Wallet, SignatureLimit = 5 });
    }

    [Fact]
    public async Task Binds_json_section_and_resolves_explorer_through_dependency_injection()
    {
        using var host = Builder().Build();
        await host.StartAsync();
        var options = host.Services.GetRequiredService<IOptions<ExplorerOptions>>().Value;
        Assert.Equal("https://example.test/rpc", options.RpcEndpoint);
        Assert.Equal(Wallet, options.WalletAddress);
        Assert.Equal(5, options.SignatureLimit);
        Assert.NotNull(host.Services.GetRequiredService<Explorer>());
        await host.StopAsync();
    }

    [Fact]
    public async Task Positional_wallet_override_is_preserved_and_trimmed()
    {
        using var host = Builder(["  another-wallet  "]).Build();
        await host.StartAsync();
        Assert.Equal("another-wallet", host.Services.GetRequiredService<IOptions<ExplorerOptions>>().Value.WalletAddress);
        await host.StopAsync();
    }

    [Fact]
    public async Task Named_command_line_settings_override_json()
    {
        using var host = Builder([
            "--Explorer:WalletAddress", "command-line-wallet",
            "--Explorer:RpcEndpoint", "http://localhost:8899",
            "--Explorer:SignatureLimit", "10"]).Build();
        await host.StartAsync();
        var options = host.Services.GetRequiredService<IOptions<ExplorerOptions>>().Value;
        Assert.Equal("command-line-wallet", options.WalletAddress);
        Assert.Equal("http://localhost:8899", options.RpcEndpoint);
        Assert.Equal(10, options.SignatureLimit);
        await host.StopAsync();
    }

    [Fact]
    public async Task Environment_overrides_json_and_command_line_overrides_environment()
    {
        // Only changes this test process; tests within this class run sequentially.
        const string variable = "Explorer__WalletAddress";
        var previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, "environment-wallet");
        try
        {
            using var host = Builder().Build();
            await host.StartAsync();
            Assert.Equal("environment-wallet", host.Services.GetRequiredService<IOptions<ExplorerOptions>>().Value.WalletAddress);
            await host.StopAsync();

            using var overridden = Builder(["--Explorer:WalletAddress", "command-line-wallet"]).Build();
            await overridden.StartAsync();
            Assert.Equal("command-line-wallet", overridden.Services.GetRequiredService<IOptions<ExplorerOptions>>().Value.WalletAddress);
            await overridden.StopAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Fact]
    public async Task Defaults_apply_to_omitted_optional_settings()
    {
        WriteSettings(new { WalletAddress = Wallet });
        using var host = Builder().Build();
        await host.StartAsync();
        var options = host.Services.GetRequiredService<IOptions<ExplorerOptions>>().Value;
        Assert.Equal("https://api.devnet.solana.com", options.RpcEndpoint);
        Assert.Equal(5, options.SignatureLimit);
        await host.StopAsync();
    }

    [Theory]
    [InlineData("Explorer:WalletAddress", "")]
    [InlineData("Explorer:WalletAddress", "   ")]
    [InlineData("Explorer:RpcEndpoint", "not-a-url")]
    [InlineData("Explorer:RpcEndpoint", "ftp://example.test")]
    [InlineData("Explorer:SignatureLimit", "0")]
    [InlineData("Explorer:SignatureLimit", "1001")]
    public async Task Invalid_options_fail_at_host_start_before_any_http_request(string key, string value)
    {
        var builder = Builder(["--" + key, value]);
        var handler = new FixtureRpcHandler();
        builder.Services.AddSingleton(_ => new HttpClient(handler));
        using var host = builder.Build();
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        Assert.Contains(key, exception.Message);
        Assert.Equal(0, handler.RequestCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    public async Task Page_size_boundaries_are_valid(int limit)
    {
        using var host = Builder(["--Explorer:SignatureLimit", limit.ToString()]).Build();
        await host.StartAsync();
        Assert.Equal(limit, host.Services.GetRequiredService<IOptions<ExplorerOptions>>().Value.SignatureLimit);
        await host.StopAsync();
    }

    [Fact]
    public async Task Missing_settings_do_not_silently_start_without_a_wallet()
    {
        File.Delete(Path.Combine(_directory, "appsettings.json"));
        using var host = Builder().Build();
        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public void Multiple_positional_arguments_still_report_usage_error()
    {
        Assert.Throws<ArgumentException>(() => Builder(["wallet-one", "wallet-two"]));
    }

    [Fact]
    public async Task Non_numeric_page_size_is_a_binding_error()
    {
        using var host = Builder(["--Explorer:SignatureLimit", "abc"]).Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task Malformed_json_is_reported_as_a_configuration_error()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, "appsettings.json"), "{bad-json");
        Assert.Throws<InvalidDataException>(() => Builder());
    }

    [Fact]
    public async Task Configured_console_workflow_calls_all_three_rpc_methods_using_bound_settings()
    {
        var builder = Builder(["--Explorer:SignatureLimit", "3"]);
        var handler = new FixtureRpcHandler();
        builder.Services.AddSingleton(_ => new HttpClient(handler));
        using var host = builder.Build();
        await host.StartAsync();
        await host.Services.GetRequiredService<Explorer>().RunAsync();
        await host.StopAsync();
        Assert.Equal(new[] { "getSlot", "getSignaturesForAddress", "getTransaction" }, handler.Methods);
        Assert.Equal(Wallet, handler.RequestedWallet);
        Assert.Equal(3, handler.RequestedLimit);
        Assert.All(handler.Endpoints, endpoint => Assert.Equal("https://example.test/rpc", endpoint));
    }

    private HostApplicationBuilder Builder(string[]? args = null)
    {
        var builder = ExplorerHost.CreateBuilder(args ?? [], _directory);
        builder.Logging.ClearProviders();
        return builder;
    }

    private void WriteSettings(object explorer) => File.WriteAllText(
        Path.Combine(_directory, "appsettings.json"), JsonSerializer.Serialize(new { Explorer = explorer }));

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class FixtureRpcHandler : HttpMessageHandler
    {
        public List<string> Methods { get; } = [];
        public List<string> Endpoints { get; } = [];
        public int RequestCount => Methods.Count;
        public string? RequestedWallet { get; private set; }
        public int RequestedLimit { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var root = json.RootElement;
            var method = root.GetProperty("method").GetString()!;
            Methods.Add(method);
            Endpoints.Add(request.RequestUri!.ToString());
            var parameters = root.GetProperty("params");
            Assert.Equal("finalized", parameters[method == "getSlot" ? 0 : 1].GetProperty("commitment").GetString());
            var fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "successful-transfer.json"));
            using var transaction = JsonDocument.Parse(fixture);
            var signature = transaction.RootElement.GetProperty("transaction").GetProperty("signatures")[0].GetString();
            string result;
            switch (method)
            {
                case "getSlot": result = "423108383"; break;
                case "getSignaturesForAddress":
                    RequestedWallet = parameters[0].GetString();
                    RequestedLimit = parameters[1].GetProperty("limit").GetInt32();
                    result = JsonSerializer.Serialize(new[] { new { signature, slot = 423108383, err = (object?)null, confirmationStatus = "finalized" } });
                    break;
                case "getTransaction":
                    Assert.Equal(signature, parameters[0].GetString());
                    Assert.Equal("jsonParsed", parameters[1].GetProperty("encoding").GetString());
                    result = fixture;
                    break;
                default: throw new InvalidOperationException("Unexpected RPC method: " + method);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"jsonrpc\":\"2.0\",\"id\":{root.GetProperty("id")},\"result\":{result}}}", Encoding.UTF8, "application/json")
            };
        }
    }
}

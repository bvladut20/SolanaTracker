using System.Net.Http.Json;
using System.Text.Json;

namespace SolanaTracker.Infrastructure.Solana;

public sealed record SignatureInfo(string Signature, ulong Slot, JsonElement? Err, string? ConfirmationStatus);
public sealed class SolanaRpcException(string message) : Exception(message);

// Networking only: no console output, database access, or polling loop.
public sealed class SolanaRpcClient(HttpClient httpClient, string endpoint)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private long _requestId;

    public async Task<ulong> GetSlotAsync(CancellationToken cancellationToken = default)
    {
        var result = await CallAsync("getSlot", [new { commitment = "finalized" }], cancellationToken);
        return result.GetUInt64();
    }

    public async Task<IReadOnlyList<SignatureInfo>> GetSignaturesForAddressAsync(
        string walletAddress, int limit, CancellationToken cancellationToken = default)
    {
        var result = await CallAsync("getSignaturesForAddress",
            [walletAddress, new { commitment = "finalized", limit }], cancellationToken);
        
        return result.Deserialize<List<SignatureInfo>>(JsonOptions)
            ?? throw new SolanaRpcException("getSignaturesForAddress returned null instead of a list.");
    }

    public async Task<JsonElement?> GetTransactionAsync(
        string signature, CancellationToken cancellationToken = default)
    {
        var result = await CallAsync("getTransaction",
            [signature, new { commitment = "finalized", encoding = "jsonParsed", maxSupportedTransactionVersion = 1 }],
            cancellationToken);
        return result.ValueKind == JsonValueKind.Null ? null : result;
    }

    private async Task<JsonElement> CallAsync(string method, object[] parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _requestId);
        var request = new { jsonrpc = "2.0", id, method, @params = parameters };
        
        using var response = await httpClient.PostAsJsonAsync(endpoint, request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var retryAfter = response.Headers.RetryAfter?.ToString();
            throw new SolanaRpcException($"{method}: HTTP {(int)response.StatusCode} ({response.ReasonPhrase})."
                + (retryAfter is null ? "" : $" Retry-After: {retryAfter}."));
        }
        
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        
        var root = document.RootElement;
        
        if (!root.TryGetProperty("id", out var responseId) || !responseId.TryGetInt64(out var receivedId) || receivedId != id)
            throw new SolanaRpcException($"{method}: response ID did not match the request.");
        
        // JSON-RPC failures can arrive with HTTP 200, so check the envelope too.
        if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            throw new SolanaRpcException($"{method}: JSON-RPC error {error.GetRawText()}");
        
        if (!root.TryGetProperty("result", out var result))
            throw new SolanaRpcException($"{method}: response has no result.");
        
        // Clone so the returned JSON survives disposal of the document.
        return result.Clone();
    }
}

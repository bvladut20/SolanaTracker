using System.Text.Json;
using SolanaTracker.Application;
using SolanaTracker.Domain;

namespace SolanaTracker.Infrastructure.Solana;

public sealed class SystemTransferExtractor : ITransferExtractor
{
    private const string SystemProgramId = "11111111111111111111111111111111";

    // TODO: Deserialise into typed classes
    public IReadOnlyList<SolTransfer> Extract(JsonElement transaction, IReadOnlySet<string> monitoredWallets)
    {
        ArgumentNullException.ThrowIfNull(monitoredWallets);
        
        var version = Required(transaction, "version");
        var supported = version.ValueKind == JsonValueKind.String && version.GetString() == "legacy"
            || version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var number) && number is 0 or 1;
        if (!supported)
            throw new JsonException("Unsupported transaction version. Expected legacy, 0, or 1.");

        var slot = Unsigned(Required(transaction, "slot"), "slot");
        var body = Required(transaction, "transaction");
        var signatures = Required(body, "signatures");
        if (signatures.ValueKind != JsonValueKind.Array || signatures.GetArrayLength() == 0)
            throw new JsonException("Transaction must have at least one signature.");
        var signature = Text(signatures[0], "signature");
        var instructions = Required(Required(body, "message"), "instructions");
        if (instructions.ValueKind != JsonValueKind.Array)
            throw new JsonException("Transaction instructions must be an array.");

        // Missing/null metadata is unknown, not proof of success.
        var error = Required(Required(transaction, "meta"), "err");
        if (error.ValueKind is not (JsonValueKind.Null or JsonValueKind.Object or JsonValueKind.String))
            throw new JsonException("meta.err must be null or a transaction error object/string.");
        if (error.ValueKind != JsonValueKind.Null)
            return [];

        long? blockTime = null;
        if (transaction.TryGetProperty("blockTime", out var time) && time.ValueKind != JsonValueKind.Null)
        {
            if (time.ValueKind != JsonValueKind.Number || !time.TryGetInt64(out var seconds))
                throw new JsonException("blockTime must be an integer or null.");
            blockTime = seconds;
        }

        var transfers = new List<SolTransfer>();
        var index = 0;
        foreach (var instruction in instructions.EnumerateArray())
        {
            var instructionIndex = index++;
            var programId = Text(Required(instruction, "programId"), "programId");
            if (programId != SystemProgramId)
                continue;

            // With jsonParsed, known System Program instructions should be decoded.
            // Without parsed data we cannot safely decide whether this was a transfer.
            var parsed = Required(instruction, "parsed");
            var type = Text(Required(parsed, "type"), "parsed.type");
            if (type != "transfer")
                continue; // createAccount, transferWithSeed, nonce operations, etc. are outside scope.

            var info = Required(parsed, "info");
            var sender = Text(Required(info, "source"), "source");
            var recipient = Text(Required(info, "destination"), "destination");
            var lamports = Unsigned(Required(info, "lamports"), "lamports");
            // Addresses are case-sensitive. Count a self-transfer or two monitored endpoints once.
            if (monitoredWallets.Any(wallet => string.Equals(wallet, sender, StringComparison.Ordinal)
                || string.Equals(wallet, recipient, StringComparison.Ordinal)))
                transfers.Add(new SolTransfer(signature, slot, instructionIndex, sender, recipient, lamports, blockTime));
        }
        return transfers;
    }

    private static JsonElement Required(JsonElement parent, string property)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(property, out var value))
            throw new JsonException($"Missing or invalid object containing '{property}'.");
        return value;
    }

    private static string Text(JsonElement value, string field)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new JsonException($"{field} must be a non-empty string.");
        return value.GetString()!;
    }

    private static ulong Unsigned(JsonElement value, string field)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt64(out var number))
            throw new JsonException($"{field} must be an unsigned 64-bit integer.");
        return number;
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using SolanaTracker.Domain;
using SolanaTracker.Infrastructure.Solana;
using Xunit;

namespace SolanaTracker.Tests;

public sealed class TransferExtractorTests
{
    private const string Sender = "9B5XszUGdMaxCZ7uSQhPzdks5ZQSmWxrmzCSvtJ6Ns6g";
    private const string Recipient = "D1UPrG5kHPhEvwAoYpFymQkHsNNzx8jWooeAvKiPfgFQ";
    private readonly SystemTransferExtractor _extractor = new();

    [Theory]
    [InlineData(Sender)]
    [InlineData(Recipient)]
    public void Extracts_transfer_when_either_endpoint_is_monitored(string wallet)
    {
        var transfer = Assert.Single(Extract(Load(), wallet));
        Assert.Equal(new SolTransfer(
            "4ReKprwf3WdLHRrzp4ctPWNBsQDPL3VZz3zMmoZfcGJMJCHh5Vq937mPdyxhCbw54wNnA6hZ7KfNpQdpt13yY7A9",
            423108383, 0, Sender, Recipient, 1_000_000_000, 1763747336), transfer);
    }

    [Fact]
    public void Preserves_instruction_indexes_and_ignores_custom_vote_create_and_inner_instructions()
    {
        var transfers = Extract(Load("mixed-instructions.json"), Sender);
        Assert.Equal([1, 4], transfers.Select(t => t.InstructionIndex));
        Assert.Equal(new ulong[] { 1_000_000_000, 250_000_001 }, transfers.Select(t => t.Lamports));
    }

    [Fact]
    public void Both_monitored_endpoints_and_self_transfers_do_not_duplicate_records()
    {
        var json = Load();
        Assert.Single(Extract(json, Sender, Recipient));
        Info(json)["destination"] = Sender;
        Assert.Single(Extract(json, Sender));
    }

    [Fact]
    public void Wallet_matching_is_case_sensitive_even_if_caller_uses_insensitive_set()
    {
        var wallets = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Sender.ToLowerInvariant() };
        Assert.Empty(_extractor.Extract(JsonSerializer.SerializeToElement(Load()), wallets));
    }

    [Fact]
    public void Unrelated_or_empty_monitored_wallets_produce_no_records()
    {
        Assert.Empty(Extract(Load(), "unrelated"));
        Assert.Empty(Extract(Load()));
    }

    [Fact]
    public void Failed_transaction_produces_no_completed_transfers_even_if_earlier_instruction_looks_valid()
    {
        var json = Load("mixed-instructions.json");
        json["meta"]!["err"] = JsonNode.Parse("{\"InstructionError\":[4,\"InvalidArgument\"]}");
        Assert.Empty(Extract(json, Sender));
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("0")]
    [InlineData("1")]
    public void Supports_existing_client_versions(string version)
    {
        var json = Load();
        json["version"] = version == "legacy" ? JsonValue.Create(version) : JsonValue.Create(int.Parse(version));
        Assert.Single(Extract(json, Sender));
    }

    [Fact]
    public void Null_or_absent_block_time_is_allowed()
    {
        var json = Load();
        json["blockTime"] = null;
        Assert.Null(Assert.Single(Extract(json, Sender)).BlockTime);
        json.AsObject().Remove("blockTime");
        Assert.Null(Assert.Single(Extract(json, Sender)).BlockTime);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(9_007_199_254_740_993UL)]
    [InlineData(ulong.MaxValue)]
    public void Lamports_remain_exact_unsigned_integers(ulong lamports)
    {
        var json = Load();
        Info(json)["lamports"] = lamports;
        Assert.Equal(lamports, Assert.Single(Extract(json, Sender)).Lamports);
    }

    [Fact]
    public void Repeated_extraction_is_deterministic_and_does_not_change_source()
    {
        var json = Load("mixed-instructions.json");
        var original = json.ToJsonString();
        Assert.Equal(Extract(json, Sender).ToArray(), Extract(json, Sender).ToArray());
        Assert.Equal(original, json.ToJsonString());
    }

    [Fact]
    public void Inner_only_transfer_produces_no_records()
    {
        var json = Load("mixed-instructions.json");
        json["transaction"]!["message"]!["instructions"] = new JsonArray();
        Assert.Empty(Extract(json, Sender));
    }

    [Fact]
    public void Transfer_with_seed_is_explicitly_outside_initial_scope()
    {
        var json = Load();
        Instruction(json)["parsed"]!["type"] = "transferWithSeed";
        Assert.Empty(Extract(json, Sender));
    }

    [Fact]
    public void Program_id_not_display_name_determines_whether_instruction_is_supported()
    {
        var json = Load();
        Instruction(json)["programId"] = "another-program";
        Assert.Empty(Extract(json, Sender));
    }

    [Theory]
    [InlineData("meta-null")]
    [InlineData("meta-missing")]
    [InlineData("err-missing")]
    [InlineData("err-invalid")]
    [InlineData("version-unknown")]
    [InlineData("version-missing")]
    [InlineData("signature-empty")]
    [InlineData("slot-negative")]
    [InlineData("instructions-missing")]
    [InlineData("instructions-not-array")]
    [InlineData("program-id-missing")]
    [InlineData("system-not-parsed")]
    [InlineData("type-missing")]
    [InlineData("info-missing")]
    [InlineData("sender-missing")]
    [InlineData("recipient-empty")]
    [InlineData("lamports-negative")]
    [InlineData("lamports-fractional")]
    [InlineData("lamports-overflow")]
    [InlineData("lamports-string")]
    [InlineData("block-time-invalid")]
    public void Malformed_or_unsupported_input_is_an_error_not_empty_history(string scenario)
    {
        var json = Load();
        switch (scenario)
        {
            case "meta-null": json["meta"] = null; break;
            case "meta-missing": json.AsObject().Remove("meta"); break;
            case "err-missing": json["meta"]!.AsObject().Remove("err"); break;
            case "err-invalid": json["meta"]!["err"] = false; break;
            case "version-unknown": json["version"] = 2; break;
            case "version-missing": json.AsObject().Remove("version"); break;
            case "signature-empty": json["transaction"]!["signatures"] = new JsonArray(); break;
            case "slot-negative": json["slot"] = -1; break;
            case "instructions-missing": json["transaction"]!["message"]!.AsObject().Remove("instructions"); break;
            case "instructions-not-array": json["transaction"]!["message"]!["instructions"] = new JsonObject(); break;
            case "program-id-missing": Instruction(json).AsObject().Remove("programId"); break;
            case "system-not-parsed": Instruction(json).AsObject().Remove("parsed"); break;
            case "type-missing": Instruction(json)["parsed"]!.AsObject().Remove("type"); break;
            case "info-missing": Instruction(json)["parsed"]!.AsObject().Remove("info"); break;
            case "sender-missing": Info(json).AsObject().Remove("source"); break;
            case "recipient-empty": Info(json)["destination"] = ""; break;
            case "lamports-negative": Info(json)["lamports"] = -1; break;
            case "lamports-fractional": Info(json)["lamports"] = 0.5; break;
            case "lamports-overflow": Info(json)["lamports"] = JsonNode.Parse("18446744073709551616"); break;
            case "lamports-string": Info(json)["lamports"] = "1000"; break;
            case "block-time-invalid": json["blockTime"] = "yesterday"; break;
        }
        Assert.Throws<JsonException>(() => Extract(json, Sender));
    }

    [Fact]
    public void Unavailable_transaction_is_not_reported_as_successful_empty_history()
    {
        Assert.Throws<JsonException>(() => _extractor.Extract(
            JsonSerializer.SerializeToElement<object?>(null), new HashSet<string> { Sender }));
    }

    [Fact]
    public void Malformed_later_transfer_does_not_return_partial_results()
    {
        var json = Load("mixed-instructions.json");
        json["transaction"]!["message"]!["instructions"]![4]!["parsed"]!["info"]!.AsObject().Remove("lamports");
        Assert.Throws<JsonException>(() => Extract(json, Sender));
    }

    private IReadOnlyList<SolTransfer> Extract(JsonNode json, params string[] wallets) =>
        _extractor.Extract(JsonSerializer.SerializeToElement(json), new HashSet<string>(wallets, StringComparer.Ordinal));

    private static JsonNode Load(string file = "successful-transfer.json") =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", file)))!;

    private static JsonNode Instruction(JsonNode json) => json["transaction"]!["message"]!["instructions"]![0]!;
    private static JsonNode Info(JsonNode json) => Instruction(json)["parsed"]!["info"]!;
}

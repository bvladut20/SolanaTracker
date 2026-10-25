using SolanaTracker.Infrastructure.Solana;
using SolanaTracker.Domain;
using System.Globalization;
using System.Text.Json;

namespace SolanaTracker.ConsoleApp;

public static class ConsoleOutput
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    public static void PrintTransfers(IReadOnlyList<SolTransfer> transfers)
    {
        Console.WriteLine();
        Console.WriteLine($"Completed supported transfers involving the monitored wallet: {transfers.Count}");
        foreach (var transfer in transfers)
        {
            Console.WriteLine($"Signature: {transfer.Signature}; slot: {transfer.Slot}; instruction [{transfer.InstructionIndex}]");
            Console.WriteLine($"  Sender: {transfer.Sender}");
            Console.WriteLine($"  Recipient: {transfer.Recipient}");
            var sol = (transfer.Lamports / 1_000_000_000m).ToString("0.#########", CultureInfo.InvariantCulture);
            Console.WriteLine($"  Amount: {transfer.Lamports} lamports ({sol} SOL)");
            Console.WriteLine($"  Block time (Unix seconds): {transfer.BlockTime?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
        }
        Console.WriteLine("Only parsed top-level System Program 'transfer' instructions are supported.");
    }

    public static void PrintSignatures(IReadOnlyList<SignatureInfo> signatures)
    {
        foreach (var item in signatures)
        {
            Console.WriteLine($"Signature: {item.Signature}");
            Console.WriteLine($"  Slot: {item.Slot}; status: {Outcome(item.Err)}; commitment: {item.ConfirmationStatus ?? "unknown"}");
        }
        Console.WriteLine("These IDs reference the wallet; they are not necessarily SOL transfers.");
    }

    public static void PrintTransaction(string signature, JsonElement? result)
    {
        Console.WriteLine($"Signature: {signature}");
        if (result is not { } transaction)
        {
            Console.WriteLine("Transaction unavailable at the requested commitment. This does not mean it failed.");
            return;
        }
        Console.WriteLine($"Slot: {transaction.GetProperty("slot")}");
        var meta = transaction.GetProperty("meta");
        Console.WriteLine($"Outcome: {(meta.ValueKind == JsonValueKind.Null ? "unknown (metadata unavailable)" : Outcome(meta.GetProperty("err")))}");
        Console.WriteLine("Top-level instructions (zero-based indexes in the original transaction):");
        var instructions = transaction.GetProperty("transaction").GetProperty("message").GetProperty("instructions");
        var index = 0;
        foreach (var instruction in instructions.EnumerateArray())
        {
            Console.WriteLine($"Instruction [{index++}]:");
            Console.WriteLine(JsonSerializer.Serialize(instruction, PrettyJson));
        }
        Console.WriteLine("Parsed instructions show a program/type/info; some instructions remain encoded.");
        Console.WriteLine("A System Program instruction with type 'transfer' describes an ordinary SOL transfer.");
        Console.WriteLine("Instructions in failed transactions do not represent completed transfers.");
        Console.WriteLine("Inner instructions and token transfer extraction are outside our initial scope.");
    }

    private static string Outcome(JsonElement? error) => error switch
    {
        null => "success",
        { ValueKind: JsonValueKind.Null } => "success",
        { } value => $"failed ({value.GetRawText()})"
    };
}

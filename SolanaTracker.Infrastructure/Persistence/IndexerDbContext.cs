using Microsoft.EntityFrameworkCore;

namespace SolanaTracker.Infrastructure.Persistence;

/// <summary>
/// Stores source transactions and derived transfers for a single Solana network.
/// Transfer identity is the transaction signature plus original instruction index.
/// Domain records remain independent of these EF persistence entities.
/// </summary>
public sealed class IndexerDbContext(DbContextOptions<IndexerDbContext> options) : DbContext(options)
{
    public DbSet<TransactionEntity> Transactions => Set<TransactionEntity>();
    public DbSet<TransferEntity> Transfers => Set<TransferEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var transactions = modelBuilder.Entity<TransactionEntity>();
        transactions.ToTable("transactions", table =>
        {
            table.HasCheckConstraint("ck_transactions_slot", "slot BETWEEN 0 AND 18446744073709551615");
            table.HasCheckConstraint("ck_transactions_outcome",
                "(succeeded AND error IS NULL) OR (NOT succeeded AND error IS NOT NULL AND error <> 'null'::jsonb)");
        });
        transactions.HasKey(t => t.Signature).HasName("pk_transactions");
        transactions.Property(t => t.Signature).HasColumnName("signature");
        // PostgreSQL bigint is signed; numeric(20,0) preserves the entire Solana u64 range.
        transactions.Property(t => t.Slot).HasColumnName("slot").HasConversion<decimal>().HasPrecision(20, 0);
        transactions.Property(t => t.BlockTime).HasColumnName("block_time").HasColumnType("timestamp with time zone");
        transactions.Property(t => t.Succeeded).HasColumnName("succeeded");
        transactions.Property(t => t.Error).HasColumnName("error").HasColumnType("jsonb");
        transactions.Property(t => t.SourceData).HasColumnName("source_data").HasColumnType("jsonb");
        transactions.Property(t => t.IngestedAt).HasColumnName("ingested_at")
            .HasColumnType("timestamp with time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");

        var transfers = modelBuilder.Entity<TransferEntity>();
        transfers.ToTable("transfers", table =>
        {
            table.HasCheckConstraint("ck_transfers_instruction_index", "instruction_index >= 0");
            table.HasCheckConstraint("ck_transfers_lamports", "lamports BETWEEN 0 AND 18446744073709551615");
        });
        transfers.HasKey(t => new { t.Signature, t.InstructionIndex }).HasName("pk_transfers");
        transfers.Property(t => t.Signature).HasColumnName("signature");
        transfers.Property(t => t.InstructionIndex).HasColumnName("instruction_index");
        transfers.Property(t => t.Sender).HasColumnName("sender");
        transfers.Property(t => t.Recipient).HasColumnName("recipient");
        transfers.Property(t => t.Lamports).HasColumnName("lamports").HasConversion<decimal>().HasPrecision(20, 0);
        transfers.HasOne(t => t.Transaction).WithMany(t => t.Transfers)
            .HasForeignKey(t => t.Signature).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_transfers_transactions");
        transfers.HasIndex(t => t.Sender).HasDatabaseName("ix_transfers_sender");
        transfers.HasIndex(t => t.Recipient).HasDatabaseName("ix_transfers_recipient");
    }
}

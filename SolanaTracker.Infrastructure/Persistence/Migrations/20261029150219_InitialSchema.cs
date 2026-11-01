using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SolanaTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "transactions",
                columns: table => new
                {
                    signature = table.Column<string>(type: "text", nullable: false),
                    slot = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: false),
                    block_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    error = table.Column<string>(type: "jsonb", nullable: true),
                    source_data = table.Column<string>(type: "jsonb", nullable: false),
                    ingested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transactions", x => x.signature);
                    table.CheckConstraint("ck_transactions_outcome", "(succeeded AND error IS NULL) OR (NOT succeeded AND error IS NOT NULL AND error <> 'null'::jsonb)");
                    table.CheckConstraint("ck_transactions_slot", "slot BETWEEN 0 AND 18446744073709551615");
                });

            migrationBuilder.CreateTable(
                name: "transfers",
                columns: table => new
                {
                    signature = table.Column<string>(type: "text", nullable: false),
                    instruction_index = table.Column<int>(type: "integer", nullable: false),
                    sender = table.Column<string>(type: "text", nullable: false),
                    recipient = table.Column<string>(type: "text", nullable: false),
                    lamports = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfers", x => new { x.signature, x.instruction_index });
                    table.CheckConstraint("ck_transfers_instruction_index", "instruction_index >= 0");
                    table.CheckConstraint("ck_transfers_lamports", "lamports BETWEEN 0 AND 18446744073709551615");
                    table.ForeignKey(
                        name: "fk_transfers_transactions",
                        column: x => x.signature,
                        principalTable: "transactions",
                        principalColumn: "signature",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_transfers_recipient",
                table: "transfers",
                column: "recipient");

            migrationBuilder.CreateIndex(
                name: "ix_transfers_sender",
                table: "transfers",
                column: "sender");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transfers");

            migrationBuilder.DropTable(
                name: "transactions");
        }
    }
}

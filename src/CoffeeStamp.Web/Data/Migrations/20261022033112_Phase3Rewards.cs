using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CoffeeStamp.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase3Rewards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "redemptions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    customer_id = table.Column<int>(type: "integer", nullable: true),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    stamps_used = table.Column<int>(type: "integer", nullable: false),
                    menu_item_id = table.Column<int>(type: "integer", nullable: true),
                    menu_name_snapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    menu_price_snapshot = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    top_up_amount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    staff_id = table.Column<int>(type: "integer", nullable: false),
                    approved_by_staff_id = table.Column<int>(type: "integer", nullable: true),
                    device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_redemptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_redemptions_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_redemptions_menu_items_menu_item_id",
                        column: x => x.menu_item_id,
                        principalTable: "menu_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_redemptions_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_redemptions_staff_staff_id",
                        column: x => x.staff_id,
                        principalTable: "staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stamp_allocations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    consuming_transaction_id = table.Column<long>(type: "bigint", nullable: false),
                    source_transaction_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stamp_allocations", x => x.id);
                    table.ForeignKey(
                        name: "fk_stamp_allocations_stamp_transactions_consuming_transaction_",
                        column: x => x.consuming_transaction_id,
                        principalTable: "stamp_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stamp_allocations_stamp_transactions_source_transaction_id",
                        column: x => x.source_transaction_id,
                        principalTable: "stamp_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stamp_transactions_redemption_id",
                table: "stamp_transactions",
                column: "redemption_id");

            migrationBuilder.CreateIndex(
                name: "ix_redemptions_customer_id_created_at",
                table: "redemptions",
                columns: new[] { "customer_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_redemptions_menu_item_id",
                table: "redemptions",
                column: "menu_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_redemptions_order_id",
                table: "redemptions",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_redemptions_staff_id",
                table: "redemptions",
                column: "staff_id");

            migrationBuilder.CreateIndex(
                name: "ix_stamp_allocations_consuming_transaction_id",
                table: "stamp_allocations",
                column: "consuming_transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_stamp_allocations_source_transaction_id",
                table: "stamp_allocations",
                column: "source_transaction_id");

            migrationBuilder.AddForeignKey(
                name: "fk_stamp_transactions_redemptions_redemption_id",
                table: "stamp_transactions",
                column: "redemption_id",
                principalTable: "redemptions",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_stamp_transactions_redemptions_redemption_id",
                table: "stamp_transactions");

            migrationBuilder.DropTable(
                name: "redemptions");

            migrationBuilder.DropTable(
                name: "stamp_allocations");

            migrationBuilder.DropIndex(
                name: "ix_stamp_transactions_redemption_id",
                table: "stamp_transactions");
        }
    }
}

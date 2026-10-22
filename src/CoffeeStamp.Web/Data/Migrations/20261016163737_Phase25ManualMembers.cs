using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeeStamp.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase25ManualMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_customers_line_user_id",
                table: "customers");

            migrationBuilder.AddColumn<string>(
                name: "adjust_kind",
                table: "stamp_transactions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "approved_by_staff_id",
                table: "stamp_transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "line_user_id",
                table: "customers",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(60)",
                oldMaxLength: 60);

            migrationBuilder.AddColumn<string>(
                name: "consent_source",
                table: "customers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "created_by_staff_id",
                table: "customers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone",
                table: "customers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // แก้มือจาก defaultValue "" ที่ EF สร้างให้ เพราะค่าว่าง parse เป็น CustomerSource ไม่ได้
            // ลูกค้าที่มีอยู่ก่อน migration นี้ทุกคนสมัครผ่าน LINE จึงต้องเป็น "Line"
            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "customers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Line");

            migrationBuilder.CreateIndex(
                name: "ux_stamp_transactions_card_migration",
                table: "stamp_transactions",
                column: "customer_id",
                unique: true,
                filter: "adjust_kind = 'CardMigration'");

            migrationBuilder.CreateIndex(
                name: "ix_customers_created_by_staff_id",
                table: "customers",
                column: "created_by_staff_id");

            migrationBuilder.CreateIndex(
                name: "ix_customers_phone",
                table: "customers",
                column: "phone");

            migrationBuilder.CreateIndex(
                name: "ux_customers_line_user_id",
                table: "customers",
                column: "line_user_id",
                unique: true,
                filter: "line_user_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_customers_staff_created_by_staff_id",
                table: "customers",
                column: "created_by_staff_id",
                principalTable: "staff",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_customers_staff_created_by_staff_id",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "ux_stamp_transactions_card_migration",
                table: "stamp_transactions");

            migrationBuilder.DropIndex(
                name: "ix_customers_created_by_staff_id",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "ix_customers_phone",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "ux_customers_line_user_id",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "adjust_kind",
                table: "stamp_transactions");

            migrationBuilder.DropColumn(
                name: "approved_by_staff_id",
                table: "stamp_transactions");

            migrationBuilder.DropColumn(
                name: "consent_source",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "created_by_staff_id",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "phone",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "source",
                table: "customers");

            migrationBuilder.AlterColumn<string>(
                name: "line_user_id",
                table: "customers",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(60)",
                oldMaxLength: 60,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_customers_line_user_id",
                table: "customers",
                column: "line_user_id",
                unique: true);
        }
    }
}

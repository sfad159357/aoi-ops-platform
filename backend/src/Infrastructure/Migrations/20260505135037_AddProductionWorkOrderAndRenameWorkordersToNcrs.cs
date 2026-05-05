using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AOIOpsPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionWorkOrderAndRenameWorkordersToNcrs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "production_work_order_id",
                table: "lots",
                type: "uniqueidentifier",
                nullable: true);

            // 為什麼用 Rename 而不是 Drop/Create：
            // - workorders 這張表本質是「不良單/異常處置單」，改名是為了對齊 MES 業務語言，
            //   不應該因為命名修正就把既有資料全部刪掉。
            migrationBuilder.RenameTable(name: "workorders", newName: "ncrs");
            migrationBuilder.RenameColumn(name: "workorder_no", table: "ncrs", newName: "ncr_no");

            migrationBuilder.RenameIndex(name: "IX_workorders_lot_id", table: "ncrs", newName: "IX_ncrs_lot_id");
            migrationBuilder.RenameIndex(name: "IX_workorders_lot_no", table: "ncrs", newName: "IX_ncrs_lot_no");
            migrationBuilder.RenameIndex(name: "IX_workorders_operator_code", table: "ncrs", newName: "IX_ncrs_operator_code");
            migrationBuilder.RenameIndex(name: "IX_workorders_panel_id", table: "ncrs", newName: "IX_ncrs_panel_id");
            migrationBuilder.RenameIndex(name: "IX_workorders_panel_no", table: "ncrs", newName: "IX_ncrs_panel_no");
            migrationBuilder.RenameIndex(name: "IX_workorders_station_code", table: "ncrs", newName: "IX_ncrs_station_code");
            migrationBuilder.RenameIndex(name: "IX_workorders_tool_code", table: "ncrs", newName: "IX_ncrs_tool_code");
            migrationBuilder.RenameIndex(name: "IX_workorders_tool_id", table: "ncrs", newName: "IX_ncrs_tool_id");
            migrationBuilder.RenameIndex(name: "IX_workorders_workorder_no", table: "ncrs", newName: "IX_ncrs_ncr_no");

            migrationBuilder.CreateTable(
                name: "production_work_order",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    work_order_no = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    product_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    planned_quantity = table.Column<int>(type: "int", nullable: true),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    planned_start_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    planned_end_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_work_order", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lots_production_work_order_id",
                table: "lots",
                column: "production_work_order_id");

            migrationBuilder.CreateIndex(
                name: "IX_production_work_order_work_order_no",
                table: "production_work_order",
                column: "work_order_no",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_lots_production_work_order_production_work_order_id",
                table: "lots",
                column: "production_work_order_id",
                principalTable: "production_work_order",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_lots_production_work_order_production_work_order_id",
                table: "lots");

            migrationBuilder.DropTable(
                name: "production_work_order");

            migrationBuilder.DropIndex(
                name: "IX_lots_production_work_order_id",
                table: "lots");

            migrationBuilder.DropColumn(
                name: "production_work_order_id",
                table: "lots");

            migrationBuilder.RenameIndex(name: "IX_ncrs_lot_id", table: "ncrs", newName: "IX_workorders_lot_id");
            migrationBuilder.RenameIndex(name: "IX_ncrs_lot_no", table: "ncrs", newName: "IX_workorders_lot_no");
            migrationBuilder.RenameIndex(name: "IX_ncrs_operator_code", table: "ncrs", newName: "IX_workorders_operator_code");
            migrationBuilder.RenameIndex(name: "IX_ncrs_panel_id", table: "ncrs", newName: "IX_workorders_panel_id");
            migrationBuilder.RenameIndex(name: "IX_ncrs_panel_no", table: "ncrs", newName: "IX_workorders_panel_no");
            migrationBuilder.RenameIndex(name: "IX_ncrs_station_code", table: "ncrs", newName: "IX_workorders_station_code");
            migrationBuilder.RenameIndex(name: "IX_ncrs_tool_code", table: "ncrs", newName: "IX_workorders_tool_code");
            migrationBuilder.RenameIndex(name: "IX_ncrs_tool_id", table: "ncrs", newName: "IX_workorders_tool_id");
            migrationBuilder.RenameIndex(name: "IX_ncrs_ncr_no", table: "ncrs", newName: "IX_workorders_workorder_no");

            migrationBuilder.RenameColumn(name: "ncr_no", table: "ncrs", newName: "workorder_no");
            migrationBuilder.RenameTable(name: "ncrs", newName: "workorders");
        }
    }
}

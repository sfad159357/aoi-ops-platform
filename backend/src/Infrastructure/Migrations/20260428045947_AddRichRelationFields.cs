using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AOIOpsPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRichRelationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "defect_code",
                table: "workorders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "line_code",
                table: "workorders",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "operator_code",
                table: "workorders",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "operator_name",
                table: "workorders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "panel_id",
                table: "workorders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "panel_no",
                table: "workorders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "severity",
                table: "workorders",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "station_code",
                table: "workorders",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tool_code",
                table: "workorders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tool_id",
                table: "workorders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lot_no",
                table: "spc_measurements",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "operator_code",
                table: "spc_measurements",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "operator_name",
                table: "spc_measurements",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "operator_name",
                table: "panel_station_log",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tool_code",
                table: "panel_station_log",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "line_code",
                table: "defects",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "operator_code",
                table: "defects",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "operator_name",
                table: "defects",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "station_code",
                table: "defects",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "line_code",
                table: "alarms",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lot_no",
                table: "alarms",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "operator_code",
                table: "alarms",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "operator_name",
                table: "alarms",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "panel_no",
                table: "alarms",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "station_code",
                table: "alarms",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "operators",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    operator_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    operator_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    role = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    shift = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operators", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workorders_operator_code",
                table: "workorders",
                column: "operator_code");

            migrationBuilder.CreateIndex(
                name: "IX_workorders_panel_id",
                table: "workorders",
                column: "panel_id");

            migrationBuilder.CreateIndex(
                name: "IX_workorders_panel_no",
                table: "workorders",
                column: "panel_no");

            migrationBuilder.CreateIndex(
                name: "IX_workorders_station_code",
                table: "workorders",
                column: "station_code");

            migrationBuilder.CreateIndex(
                name: "IX_workorders_tool_code",
                table: "workorders",
                column: "tool_code");

            migrationBuilder.CreateIndex(
                name: "IX_workorders_tool_id",
                table: "workorders",
                column: "tool_id");

            migrationBuilder.CreateIndex(
                name: "IX_defects_operator_code",
                table: "defects",
                column: "operator_code");

            migrationBuilder.CreateIndex(
                name: "IX_defects_station_code",
                table: "defects",
                column: "station_code");

            migrationBuilder.CreateIndex(
                name: "IX_alarms_lot_no",
                table: "alarms",
                column: "lot_no");

            migrationBuilder.CreateIndex(
                name: "IX_alarms_operator_code",
                table: "alarms",
                column: "operator_code");

            migrationBuilder.CreateIndex(
                name: "IX_alarms_panel_no",
                table: "alarms",
                column: "panel_no");

            migrationBuilder.CreateIndex(
                name: "IX_alarms_station_code",
                table: "alarms",
                column: "station_code");

            migrationBuilder.CreateIndex(
                name: "IX_operators_operator_code",
                table: "operators",
                column: "operator_code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_workorders_panels_panel_id",
                table: "workorders",
                column: "panel_id",
                principalTable: "panels",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_workorders_tools_tool_id",
                table: "workorders",
                column: "tool_id",
                principalTable: "tools",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_workorders_panels_panel_id",
                table: "workorders");

            migrationBuilder.DropForeignKey(
                name: "FK_workorders_tools_tool_id",
                table: "workorders");

            migrationBuilder.DropTable(
                name: "operators");

            migrationBuilder.DropIndex(
                name: "IX_workorders_operator_code",
                table: "workorders");

            migrationBuilder.DropIndex(
                name: "IX_workorders_panel_id",
                table: "workorders");

            migrationBuilder.DropIndex(
                name: "IX_workorders_panel_no",
                table: "workorders");

            migrationBuilder.DropIndex(
                name: "IX_workorders_station_code",
                table: "workorders");

            migrationBuilder.DropIndex(
                name: "IX_workorders_tool_code",
                table: "workorders");

            migrationBuilder.DropIndex(
                name: "IX_workorders_tool_id",
                table: "workorders");

            migrationBuilder.DropIndex(
                name: "IX_defects_operator_code",
                table: "defects");

            migrationBuilder.DropIndex(
                name: "IX_defects_station_code",
                table: "defects");

            migrationBuilder.DropIndex(
                name: "IX_alarms_lot_no",
                table: "alarms");

            migrationBuilder.DropIndex(
                name: "IX_alarms_operator_code",
                table: "alarms");

            migrationBuilder.DropIndex(
                name: "IX_alarms_panel_no",
                table: "alarms");

            migrationBuilder.DropIndex(
                name: "IX_alarms_station_code",
                table: "alarms");

            migrationBuilder.DropColumn(
                name: "defect_code",
                table: "workorders");

            migrationBuilder.DropColumn(
                name: "line_code",
                table: "workorders");

            migrationBuilder.DropColumn(
                name: "operator_code",
                table: "workorders");

            migrationBuilder.DropColumn(
                name: "operator_name",
                table: "workorders");

            migrationBuilder.DropColumn(
                name: "panel_id",
                table: "workorders");

            migrationBuilder.DropColumn(
                name: "panel_no",
                table: "workorders");

            migrationBuilder.DropColumn(
                name: "severity",
                table: "workorders");

            migrationBuilder.DropColumn(
                name: "station_code",
                table: "workorders");

            migrationBuilder.DropColumn(
                name: "tool_code",
                table: "workorders");

            migrationBuilder.DropColumn(
                name: "tool_id",
                table: "workorders");

            migrationBuilder.DropColumn(
                name: "lot_no",
                table: "spc_measurements");

            migrationBuilder.DropColumn(
                name: "operator_code",
                table: "spc_measurements");

            migrationBuilder.DropColumn(
                name: "operator_name",
                table: "spc_measurements");

            migrationBuilder.DropColumn(
                name: "operator_name",
                table: "panel_station_log");

            migrationBuilder.DropColumn(
                name: "tool_code",
                table: "panel_station_log");

            migrationBuilder.DropColumn(
                name: "line_code",
                table: "defects");

            migrationBuilder.DropColumn(
                name: "operator_code",
                table: "defects");

            migrationBuilder.DropColumn(
                name: "operator_name",
                table: "defects");

            migrationBuilder.DropColumn(
                name: "station_code",
                table: "defects");

            migrationBuilder.DropColumn(
                name: "line_code",
                table: "alarms");

            migrationBuilder.DropColumn(
                name: "lot_no",
                table: "alarms");

            migrationBuilder.DropColumn(
                name: "operator_code",
                table: "alarms");

            migrationBuilder.DropColumn(
                name: "operator_name",
                table: "alarms");

            migrationBuilder.DropColumn(
                name: "panel_no",
                table: "alarms");

            migrationBuilder.DropColumn(
                name: "station_code",
                table: "alarms");
        }
    }
}

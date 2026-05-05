using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AOIOpsPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionWorkOrderLineCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "line_code",
                table: "production_work_order",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "line_code",
                table: "production_work_order");
        }
    }
}

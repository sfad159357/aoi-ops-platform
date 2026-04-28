using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AOIOpsPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPcbSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    doc_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    source_path = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    uploaded_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    line_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    line_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lines", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    lot_no = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    product_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    quantity = table.Column<int>(type: "int", nullable: true),
                    start_time = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    end_time = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "material_lots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    material_lot_no = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    material_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    material_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    supplier = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_lots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "parameters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    parameter_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    parameter_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    usl = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    lsl = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    target = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parameters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recipes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    recipe_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    recipe_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recipes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    station_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    station_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    seq = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stations", x => x.id);
                    table.UniqueConstraint("AK_stations_station_code", x => x.station_code);
                });

            migrationBuilder.CreateTable(
                name: "document_chunks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    chunk_text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    chunk_index = table.Column<int>(type: "int", nullable: false),
                    embedding_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_chunks", x => x.id);
                    table.ForeignKey(
                        name: "FK_document_chunks_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tools",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    tool_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    tool_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    tool_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    line_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    line_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tools", x => x.id);
                    table.ForeignKey(
                        name: "FK_tools_lines_line_id",
                        column: x => x.line_id,
                        principalTable: "lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "panels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    lot_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    lot_no = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    panel_no = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_panels", x => x.id);
                    table.ForeignKey(
                        name: "FK_panels_lots_lot_id",
                        column: x => x.lot_id,
                        principalTable: "lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workorders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    lot_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    lot_no = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    workorder_no = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    priority = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    source_queue = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workorders", x => x.id);
                    table.ForeignKey(
                        name: "FK_workorders_lots_lot_id",
                        column: x => x.lot_id,
                        principalTable: "lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "panel_material_usage",
                columns: table => new
                {
                    panel_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    material_lot_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    panel_no = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    material_lot_no = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    used_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_panel_material_usage", x => new { x.panel_id, x.material_lot_id });
                    table.ForeignKey(
                        name: "FK_panel_material_usage_material_lots_material_lot_id",
                        column: x => x.material_lot_id,
                        principalTable: "material_lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_panel_material_usage_panels_panel_id",
                        column: x => x.panel_id,
                        principalTable: "panels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "panel_station_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    panel_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    station_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    panel_no = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    entered_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    exited_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    result = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    @operator = table.Column<string>(name: "operator", type: "nvarchar(100)", maxLength: 100, nullable: true),
                    note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_panel_station_log", x => x.id);
                    table.ForeignKey(
                        name: "FK_panel_station_log_panels_panel_id",
                        column: x => x.panel_id,
                        principalTable: "panels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_panel_station_log_stations_station_code",
                        column: x => x.station_code,
                        principalTable: "stations",
                        principalColumn: "station_code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "process_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    tool_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    recipe_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    lot_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    panel_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tool_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    lot_no = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    panel_no = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    run_start_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    run_end_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    temperature = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    pressure = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    yield_rate = table.Column<decimal>(type: "decimal(5,4)", nullable: true),
                    result_status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_process_runs", x => x.id);
                    table.ForeignKey(
                        name: "FK_process_runs_lots_lot_id",
                        column: x => x.lot_id,
                        principalTable: "lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_process_runs_panels_panel_id",
                        column: x => x.panel_id,
                        principalTable: "panels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_process_runs_recipes_recipe_id",
                        column: x => x.recipe_id,
                        principalTable: "recipes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_process_runs_tools_tool_id",
                        column: x => x.tool_id,
                        principalTable: "tools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "spc_measurements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    panel_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tool_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    parameter_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    panel_no = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    tool_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    line_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    station_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    parameter_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    value = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    measured_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    is_violation = table.Column<bool>(type: "bit", nullable: false),
                    violation_codes = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    kafka_event_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spc_measurements", x => x.id);
                    table.ForeignKey(
                        name: "FK_spc_measurements_panels_panel_id",
                        column: x => x.panel_id,
                        principalTable: "panels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_spc_measurements_parameters_parameter_id",
                        column: x => x.parameter_id,
                        principalTable: "parameters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_spc_measurements_tools_tool_id",
                        column: x => x.tool_id,
                        principalTable: "tools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "alarms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    tool_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    process_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tool_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    alarm_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    alarm_level = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    triggered_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    cleared_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alarms", x => x.id);
                    table.ForeignKey(
                        name: "FK_alarms_process_runs_process_run_id",
                        column: x => x.process_run_id,
                        principalTable: "process_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_alarms_tools_tool_id",
                        column: x => x.tool_id,
                        principalTable: "tools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "defects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    tool_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    lot_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    panel_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    process_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tool_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    lot_no = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    panel_no = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    defect_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    defect_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    severity = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    x_coord = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    y_coord = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    detected_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    is_false_alarm = table.Column<bool>(type: "bit", nullable: false),
                    kafka_event_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_defects", x => x.id);
                    table.ForeignKey(
                        name: "FK_defects_lots_lot_id",
                        column: x => x.lot_id,
                        principalTable: "lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_defects_panels_panel_id",
                        column: x => x.panel_id,
                        principalTable: "panels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_defects_process_runs_process_run_id",
                        column: x => x.process_run_id,
                        principalTable: "process_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_defects_tools_tool_id",
                        column: x => x.tool_id,
                        principalTable: "tools",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "copilot_queries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    query_text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    related_alarm_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    related_defect_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    answer_text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    source_refs = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_copilot_queries", x => x.id);
                    table.ForeignKey(
                        name: "FK_copilot_queries_alarms_related_alarm_id",
                        column: x => x.related_alarm_id,
                        principalTable: "alarms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_copilot_queries_defects_related_defect_id",
                        column: x => x.related_defect_id,
                        principalTable: "defects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "defect_images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    defect_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    image_path = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    thumbnail_path = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    width = table.Column<int>(type: "int", nullable: true),
                    height = table.Column<int>(type: "int", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_defect_images", x => x.id);
                    table.ForeignKey(
                        name: "FK_defect_images_defects_defect_id",
                        column: x => x.defect_id,
                        principalTable: "defects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "defect_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    defect_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reviewer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    review_result = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    review_comment = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_defect_reviews", x => x.id);
                    table.ForeignKey(
                        name: "FK_defect_reviews_defects_defect_id",
                        column: x => x.defect_id,
                        principalTable: "defects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alarms_process_run_id",
                table: "alarms",
                column: "process_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_alarms_tool_code",
                table: "alarms",
                column: "tool_code");

            migrationBuilder.CreateIndex(
                name: "IX_alarms_tool_id",
                table: "alarms",
                column: "tool_id");

            migrationBuilder.CreateIndex(
                name: "IX_alarms_triggered_at",
                table: "alarms",
                column: "triggered_at");

            migrationBuilder.CreateIndex(
                name: "IX_copilot_queries_created_at",
                table: "copilot_queries",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_copilot_queries_related_alarm_id",
                table: "copilot_queries",
                column: "related_alarm_id");

            migrationBuilder.CreateIndex(
                name: "IX_copilot_queries_related_defect_id",
                table: "copilot_queries",
                column: "related_defect_id");

            migrationBuilder.CreateIndex(
                name: "IX_defect_images_defect_id",
                table: "defect_images",
                column: "defect_id");

            migrationBuilder.CreateIndex(
                name: "IX_defect_reviews_defect_id",
                table: "defect_reviews",
                column: "defect_id");

            migrationBuilder.CreateIndex(
                name: "IX_defects_detected_at",
                table: "defects",
                column: "detected_at");

            migrationBuilder.CreateIndex(
                name: "IX_defects_lot_id",
                table: "defects",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "IX_defects_lot_no",
                table: "defects",
                column: "lot_no");

            migrationBuilder.CreateIndex(
                name: "IX_defects_panel_id",
                table: "defects",
                column: "panel_id");

            migrationBuilder.CreateIndex(
                name: "IX_defects_panel_no",
                table: "defects",
                column: "panel_no");

            migrationBuilder.CreateIndex(
                name: "IX_defects_process_run_id",
                table: "defects",
                column: "process_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_defects_tool_code",
                table: "defects",
                column: "tool_code");

            migrationBuilder.CreateIndex(
                name: "IX_defects_tool_id",
                table: "defects",
                column: "tool_id");

            migrationBuilder.CreateIndex(
                name: "IX_document_chunks_document_id_chunk_index",
                table: "document_chunks",
                columns: new[] { "document_id", "chunk_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lines_line_code",
                table: "lines",
                column: "line_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lots_lot_no",
                table: "lots",
                column: "lot_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_material_lots_material_lot_no",
                table: "material_lots",
                column: "material_lot_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_material_lots_material_type",
                table: "material_lots",
                column: "material_type");

            migrationBuilder.CreateIndex(
                name: "IX_panel_material_usage_material_lot_id",
                table: "panel_material_usage",
                column: "material_lot_id");

            migrationBuilder.CreateIndex(
                name: "IX_panel_material_usage_material_lot_no",
                table: "panel_material_usage",
                column: "material_lot_no");

            migrationBuilder.CreateIndex(
                name: "IX_panel_material_usage_panel_no",
                table: "panel_material_usage",
                column: "panel_no");

            migrationBuilder.CreateIndex(
                name: "IX_panel_station_log_panel_id_entered_at",
                table: "panel_station_log",
                columns: new[] { "panel_id", "entered_at" });

            migrationBuilder.CreateIndex(
                name: "IX_panel_station_log_panel_no",
                table: "panel_station_log",
                column: "panel_no");

            migrationBuilder.CreateIndex(
                name: "IX_panel_station_log_station_code",
                table: "panel_station_log",
                column: "station_code");

            migrationBuilder.CreateIndex(
                name: "IX_panels_lot_id",
                table: "panels",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "IX_panels_lot_no",
                table: "panels",
                column: "lot_no");

            migrationBuilder.CreateIndex(
                name: "IX_panels_panel_no",
                table: "panels",
                column: "panel_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_parameters_parameter_code",
                table: "parameters",
                column: "parameter_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_process_runs_lot_id",
                table: "process_runs",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "IX_process_runs_lot_no",
                table: "process_runs",
                column: "lot_no");

            migrationBuilder.CreateIndex(
                name: "IX_process_runs_panel_id",
                table: "process_runs",
                column: "panel_id");

            migrationBuilder.CreateIndex(
                name: "IX_process_runs_panel_no",
                table: "process_runs",
                column: "panel_no");

            migrationBuilder.CreateIndex(
                name: "IX_process_runs_recipe_id",
                table: "process_runs",
                column: "recipe_id");

            migrationBuilder.CreateIndex(
                name: "IX_process_runs_run_start_at",
                table: "process_runs",
                column: "run_start_at");

            migrationBuilder.CreateIndex(
                name: "IX_process_runs_tool_code",
                table: "process_runs",
                column: "tool_code");

            migrationBuilder.CreateIndex(
                name: "IX_process_runs_tool_id",
                table: "process_runs",
                column: "tool_id");

            migrationBuilder.CreateIndex(
                name: "IX_recipes_recipe_code",
                table: "recipes",
                column: "recipe_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_spc_measurements_is_violation",
                table: "spc_measurements",
                column: "is_violation");

            migrationBuilder.CreateIndex(
                name: "IX_spc_measurements_line_code_measured_at",
                table: "spc_measurements",
                columns: new[] { "line_code", "measured_at" });

            migrationBuilder.CreateIndex(
                name: "IX_spc_measurements_panel_id",
                table: "spc_measurements",
                column: "panel_id");

            migrationBuilder.CreateIndex(
                name: "IX_spc_measurements_panel_no_measured_at",
                table: "spc_measurements",
                columns: new[] { "panel_no", "measured_at" });

            migrationBuilder.CreateIndex(
                name: "IX_spc_measurements_parameter_code_measured_at",
                table: "spc_measurements",
                columns: new[] { "parameter_code", "measured_at" });

            migrationBuilder.CreateIndex(
                name: "IX_spc_measurements_parameter_id",
                table: "spc_measurements",
                column: "parameter_id");

            migrationBuilder.CreateIndex(
                name: "IX_spc_measurements_tool_code_measured_at",
                table: "spc_measurements",
                columns: new[] { "tool_code", "measured_at" });

            migrationBuilder.CreateIndex(
                name: "IX_spc_measurements_tool_id",
                table: "spc_measurements",
                column: "tool_id");

            migrationBuilder.CreateIndex(
                name: "IX_stations_station_code",
                table: "stations",
                column: "station_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tools_line_code",
                table: "tools",
                column: "line_code");

            migrationBuilder.CreateIndex(
                name: "IX_tools_line_id",
                table: "tools",
                column: "line_id");

            migrationBuilder.CreateIndex(
                name: "IX_tools_tool_code",
                table: "tools",
                column: "tool_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workorders_lot_id",
                table: "workorders",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "IX_workorders_lot_no",
                table: "workorders",
                column: "lot_no");

            migrationBuilder.CreateIndex(
                name: "IX_workorders_workorder_no",
                table: "workorders",
                column: "workorder_no",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "copilot_queries");

            migrationBuilder.DropTable(
                name: "defect_images");

            migrationBuilder.DropTable(
                name: "defect_reviews");

            migrationBuilder.DropTable(
                name: "document_chunks");

            migrationBuilder.DropTable(
                name: "panel_material_usage");

            migrationBuilder.DropTable(
                name: "panel_station_log");

            migrationBuilder.DropTable(
                name: "spc_measurements");

            migrationBuilder.DropTable(
                name: "workorders");

            migrationBuilder.DropTable(
                name: "alarms");

            migrationBuilder.DropTable(
                name: "defects");

            migrationBuilder.DropTable(
                name: "documents");

            migrationBuilder.DropTable(
                name: "material_lots");

            migrationBuilder.DropTable(
                name: "stations");

            migrationBuilder.DropTable(
                name: "parameters");

            migrationBuilder.DropTable(
                name: "process_runs");

            migrationBuilder.DropTable(
                name: "panels");

            migrationBuilder.DropTable(
                name: "recipes");

            migrationBuilder.DropTable(
                name: "tools");

            migrationBuilder.DropTable(
                name: "lots");

            migrationBuilder.DropTable(
                name: "lines");
        }
    }
}

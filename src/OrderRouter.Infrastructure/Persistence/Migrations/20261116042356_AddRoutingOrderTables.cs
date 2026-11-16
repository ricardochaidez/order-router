using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderRouter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoutingOrderTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "routing_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<string>(type: "text", nullable: false),
                    caller_hash = table.Column<string>(type: "text", nullable: true),
                    customer_zip = table.Column<string>(type: "char(5)", nullable: false),
                    mail_order = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    feasible = table.Column<bool>(type: "boolean", nullable: false),
                    experiment_arms = table.Column<string>(type: "jsonb", nullable: false),
                    config_version = table.Column<string>(type: "text", nullable: false),
                    search_mode = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_routing_orders", x => x.id);
                    table.CheckConstraint("ck_routing_orders_search_mode", "search_mode IS NULL OR search_mode IN ('exact', 'greedy')");
                    table.CheckConstraint("ck_routing_orders_status", "status IN ('routed', 'partially_routed', 'not_routed')");
                });

            migrationBuilder.CreateTable(
                name: "routing_order_items",
                columns: table => new
                {
                    routing_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    product_code = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<string>(type: "text", nullable: true),
                    supplier_id = table.Column<string>(type: "text", nullable: true),
                    supplier_name = table.Column<string>(type: "text", nullable: true),
                    fulfillment_mode = table.Column<string>(type: "text", nullable: true),
                    reason_code = table.Column<string>(type: "text", nullable: true),
                    message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_routing_order_items", x => new { x.routing_order_id, x.line_number });
                    table.CheckConstraint("ck_routing_order_items_fulfillment_mode", "fulfillment_mode IS NULL OR fulfillment_mode IN ('local', 'mail')");
                    table.CheckConstraint("ck_routing_order_items_line_number", "line_number >= 1");
                    table.CheckConstraint("ck_routing_order_items_quantity", "quantity >= 1");
                    table.ForeignKey(
                        name: "FK_routing_order_items_routing_orders_routing_order_id",
                        column: x => x.routing_order_id,
                        principalTable: "routing_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "routing_order_warnings",
                columns: table => new
                {
                    routing_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    unfulfilled_product_codes = table.Column<string[]>(type: "text[]", nullable: false),
                    affected_product_codes = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_routing_order_warnings", x => new { x.routing_order_id, x.ordinal });
                    table.CheckConstraint("ck_routing_order_warnings_ordinal", "ordinal >= 1");
                    table.ForeignKey(
                        name: "FK_routing_order_warnings_routing_orders_routing_order_id",
                        column: x => x.routing_order_id,
                        principalTable: "routing_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_routing_orders_order_id",
                table: "routing_orders",
                column: "order_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "routing_order_items");

            migrationBuilder.DropTable(
                name: "routing_order_warnings");

            migrationBuilder.DropTable(
                name: "routing_orders");
        }
    }
}

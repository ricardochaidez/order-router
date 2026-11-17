using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderRouter.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "products",
            columns: table => new
            {
                product_code = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                category = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_products", x => x.product_code);
            });

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
            name: "suppliers",
            columns: table => new
            {
                supplier_id = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                rating = table.Column<decimal>(type: "numeric(4,2)", nullable: true),
                can_mail_order = table.Column<bool>(type: "boolean", nullable: false),
                seeded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_suppliers", x => x.supplier_id);
                table.CheckConstraint("ck_suppliers_rating", "rating IS NULL OR rating BETWEEN 1 AND 10");
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

        migrationBuilder.CreateTable(
            name: "supplier_categories",
            columns: table => new
            {
                supplier_id = table.Column<string>(type: "text", nullable: false),
                category = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_supplier_categories", x => new { x.supplier_id, x.category });
                table.ForeignKey(
                    name: "FK_supplier_categories_suppliers_supplier_id",
                    column: x => x.supplier_id,
                    principalTable: "suppliers",
                    principalColumn: "supplier_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "supplier_service_areas",
            columns: table => new
            {
                supplier_id = table.Column<string>(type: "text", nullable: false),
                start_zip = table.Column<int>(type: "integer", nullable: false),
                end_zip = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_supplier_service_areas", x => new { x.supplier_id, x.start_zip });
                table.CheckConstraint("ck_supplier_service_areas_start_le_end", "start_zip <= end_zip");
                table.CheckConstraint("ck_supplier_service_areas_zip_bounds", "start_zip BETWEEN 0 AND 99999 AND end_zip BETWEEN 0 AND 99999");
                table.ForeignKey(
                    name: "FK_supplier_service_areas_suppliers_supplier_id",
                    column: x => x.supplier_id,
                    principalTable: "suppliers",
                    principalColumn: "supplier_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_routing_orders_order_id",
            table: "routing_orders",
            column: "order_id");

        migrationBuilder.CreateIndex(
            name: "ix_supplier_categories_category_supplier_id",
            table: "supplier_categories",
            columns: new[] { "category", "supplier_id" });

        migrationBuilder.CreateIndex(
            name: "ix_supplier_service_areas_start_zip_end_zip",
            table: "supplier_service_areas",
            columns: new[] { "start_zip", "end_zip" });

        // EF Core cannot model expression indexes, so these are raw SQL and the model snapshot is unchanged.
        // ProductCatalog queries lower(product_code) and lower(category), which the primary key and plain columns cannot serve.
        // Unique: the seed file rejects case-insensitive duplicates, and the database does too. Without it a re-seed
        // that changes only the case of a code leaves two rows, and every order with that code then fails as ambiguous.
        migrationBuilder.Sql("CREATE UNIQUE INDEX ux_products_product_code_lower ON products (lower(product_code));");
        migrationBuilder.Sql("CREATE INDEX ix_products_category_lower ON products (lower(category));");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS ix_products_category_lower;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS ux_products_product_code_lower;");

        migrationBuilder.DropTable(
            name: "products");

        migrationBuilder.DropTable(
            name: "routing_order_items");

        migrationBuilder.DropTable(
            name: "routing_order_warnings");

        migrationBuilder.DropTable(
            name: "supplier_categories");

        migrationBuilder.DropTable(
            name: "supplier_service_areas");

        migrationBuilder.DropTable(
            name: "routing_orders");

        migrationBuilder.DropTable(
            name: "suppliers");
    }
}

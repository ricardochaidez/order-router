using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderRouter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialReferenceDataSchema : Migration
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
                name: "ix_supplier_categories_category_supplier_id",
                table: "supplier_categories",
                columns: new[] { "category", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_service_areas_start_zip_end_zip",
                table: "supplier_service_areas",
                columns: new[] { "start_zip", "end_zip" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "supplier_categories");

            migrationBuilder.DropTable(
                name: "supplier_service_areas");

            migrationBuilder.DropTable(
                name: "suppliers");
        }
    }
}

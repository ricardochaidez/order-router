using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderRouter.Api.Extensions;
using OrderRouter.Infrastructure;
using OrderRouter.Tests.TestData;

namespace OrderRouter.Tests.Infrastructure;

/// <summary>
/// Inspects the EF Core model only. No database is opened, so these run without PostgreSQL.
/// </summary>
public class OrderRouterDataContextModelTests
{
    private const string dummyConnectionString = "Host=localhost;Database=unused;Username=unused;Password=unused";

    private static OrderRouterDataContext CreateContext()
        => new(new DbContextOptionsBuilder<OrderRouterDataContext>().UseNpgsql(dummyConnectionString).Options);

    private static ITable GetTable(string name)
    {
        using var context = CreateContext();

        return context.GetService<IDesignTimeModel>().Model.GetRelationalModel().Tables.Single(table => table.Name == name);
    }

    [Fact]
    public void Model_Always_ContainsTheReferenceTablesAndTheStoredOrderTables()
    {
        using var context = CreateContext();

        var tables = context.GetService<IDesignTimeModel>().Model.GetRelationalModel().Tables.Select(table => table.Name).Order();

        Assert.Equal(
            ["products", "routing_order_items", "routing_order_warnings", "routing_orders", "supplier_categories", "supplier_service_areas", "suppliers"],
            tables);
    }

    // AC3 / SC-09: mail-only is derived from the configurable nationwide range, never stored.
    [Fact]
    public void SuppliersTable_Always_HasExactlyTheAgreedColumnsAndNoMailOnlyColumn()
    {
        var columns = GetTable("suppliers").Columns.Select(column => column.Name).ToArray();

        Assert.Equal(["can_mail_order", "name", "rating", "seeded_at", "supplier_id"], columns.Order());
        Assert.DoesNotContain(columns, name => name.Replace("_", string.Empty, StringComparison.Ordinal).Contains("mailonly", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("suppliers", "supplier_id", "text", false)]
    [InlineData("suppliers", "name", "text", false)]
    [InlineData("suppliers", "rating", "numeric(4,2)", true)]
    [InlineData("suppliers", "can_mail_order", "boolean", false)]
    [InlineData("suppliers", "seeded_at", "timestamp with time zone", false)]
    [InlineData("supplier_categories", "supplier_id", "text", false)]
    [InlineData("supplier_categories", "category", "text", false)]
    [InlineData("supplier_service_areas", "supplier_id", "text", false)]
    [InlineData("supplier_service_areas", "start_zip", "integer", false)]
    [InlineData("supplier_service_areas", "end_zip", "integer", false)]
    [InlineData("products", "product_code", "text", false)]
    [InlineData("products", "name", "text", false)]
    [InlineData("products", "category", "text", false)]
    public void Column_ForTable_HasExpectedTypeAndNullability(string table, string column, string storeType, bool isNullable)
    {
        var actual = GetTable(table).Columns.Single(candidate => candidate.Name == column);

        Assert.Equal(storeType, actual.StoreType);
        Assert.Equal(isNullable, actual.IsNullable);
    }

    [Theory]
    [InlineData("suppliers", new[] { "supplier_id" })]
    [InlineData("supplier_categories", new[] { "supplier_id", "category" })]
    [InlineData("supplier_service_areas", new[] { "supplier_id", "start_zip" })]
    [InlineData("products", new[] { "product_code" })]
    public void PrimaryKey_ForTable_HasExpectedColumnsInOrder(string table, string[] expected)
    {
        var key = GetTable(table).PrimaryKey!;

        Assert.Equal(expected, key.Columns.Select(column => column.Name));
    }

    [Theory]
    [InlineData("supplier_categories", "ix_supplier_categories_category_supplier_id", new[] { "category", "supplier_id" })]
    [InlineData("supplier_service_areas", "ix_supplier_service_areas_start_zip_end_zip", new[] { "start_zip", "end_zip" })]
    public void Index_ForTable_HasExpectedNameAndColumnOrder(string table, string indexName, string[] expected)
    {
        var index = GetTable(table).Indexes.Single(candidate => candidate.Name == indexName);

        Assert.Equal(expected, index.Columns.Select(column => column.Name));
    }

    [Theory]
    [InlineData("supplier_categories")]
    [InlineData("supplier_service_areas")]
    public void ForeignKey_FromChildTable_CascadesFromSuppliers(string table)
    {
        var foreignKey = Assert.Single(GetTable(table).ForeignKeyConstraints);

        Assert.Equal("suppliers", foreignKey.PrincipalTable.Name);
        Assert.Equal(ReferentialAction.Cascade, foreignKey.OnDeleteAction);
    }

    [Fact]
    public void ProductsTable_Always_HasNoForeignKeys()
    {
        Assert.Empty(GetTable("products").ForeignKeyConstraints);
    }

    [Theory]
    [InlineData("suppliers", "ck_suppliers_rating")]
    [InlineData("supplier_service_areas", "ck_supplier_service_areas_zip_bounds")]
    [InlineData("supplier_service_areas", "ck_supplier_service_areas_start_le_end")]
    public void CheckConstraint_ForTable_Exists(string table, string constraint)
    {
        Assert.Contains(GetTable(table).CheckConstraints, candidate => candidate.Name == constraint);
    }

    // Fails the build when a configuration change is made without generating a new migration.
    [Fact]
    public void Model_ComparedWithLatestMigration_HasNoPendingChanges()
    {
        using var context = CreateContext();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ConfigureDbContext_WithBlankConnectionString_Throws(string connectionString)
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => services.ConfigureDbContext(TestConfiguration.WithConnectionString(connectionString)));
    }

    [Fact]
    public void ConfigureDbContext_WithNullConnectionString_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => services.ConfigureDbContext(TestConfiguration.WithConnectionString(null)));
    }

    // Resolving the context must not open a connection: the dummy host does not exist.
    [Fact]
    public void ConfigureDbContext_WithConnectionString_ResolvesContextWithoutConnecting()
    {
        using var provider = new ServiceCollection().ConfigureDbContext(TestConfiguration.WithConnectionString(dummyConnectionString)).BuildServiceProvider();
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<OrderRouterDataContext>();

        Assert.Equal(dummyConnectionString, context.Database.GetConnectionString());
    }

    [Fact]
    public void CreateDbContext_WithoutEnvironmentVariable_UsesComposeDefault()
    {
        var original = Environment.GetEnvironmentVariable(OrderRouterDataContextFactory.ConnectionStringVariable);
        Environment.SetEnvironmentVariable(OrderRouterDataContextFactory.ConnectionStringVariable, null);

        try
        {
            using var context = new OrderRouterDataContextFactory().CreateDbContext([]);

            Assert.Equal(OrderRouterDataContextFactory.DefaultConnectionString, context.Database.GetConnectionString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(OrderRouterDataContextFactory.ConnectionStringVariable, original);
        }
    }
}

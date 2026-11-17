
namespace OrderRouter.Api.ViewModels.Response;

/// <summary>The items one supplier will ship, in request order (FR-15).</summary>
/// <param name="SupplierId">The supplier identifier.</param>
/// <param name="SupplierName">The supplier name as stored.</param>
/// <param name="Items">The items assigned to this supplier.</param>
public sealed record SupplierRoutingResponseViewModel(string SupplierId, string SupplierName, IReadOnlyList<RoutedItemResponseViewModel> Items);

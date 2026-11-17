namespace OrderRouter.Api.ViewModels.Response;

/// <summary>One line the caller requested, as stored (FR-21).</summary>
/// <param name="ProductCode">The product code as requested.</param>
/// <param name="Quantity">The requested quantity.</param>
public sealed record RequestedItemResponseViewModel(string ProductCode, int Quantity);

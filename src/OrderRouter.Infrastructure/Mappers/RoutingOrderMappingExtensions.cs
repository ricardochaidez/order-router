using OrderRouter.Domain.Entities.Routing;
using OrderRouter.Infrastructure.Models;

namespace OrderRouter.Infrastructure.Mappers;

/// <summary>Maps a stored routing order between the domain model and its persistence entities (ADR-0007).</summary>
internal static class RoutingOrderMappingExtensions
{
    /// <summary>Maps the domain order, its items and its warnings to the entity graph that is inserted.</summary>
    /// <param name="order">The stored order.</param>
    public static RoutingOrderEntity ToEntity(this StoredRoutingOrder order)
        => new()
        {
            Id = order.Id,
            OrderId = order.OrderId,
            CallerHash = order.CallerHash,
            CustomerZip = order.CustomerZip,
            MailOrder = order.MailOrder,
            Status = order.Status,
            Feasible = order.Feasible,
            ExperimentArms = order.ExperimentArmsJson,
            ConfigVersion = order.ConfigVersion,
            SearchMode = order.SearchMode,
            CreatedAt = order.CreatedAt,
            Items =
            [
                .. order.Items.Select(item => new RoutingOrderItemEntity
                {
                    LineNumber = item.LineNumber,
                    ProductCode = item.ProductCode,
                    Quantity = item.Quantity,
                    Category = item.Category,
                    SupplierId = item.SupplierId,
                    SupplierName = item.SupplierName,
                    FulfillmentMode = item.FulfillmentMode,
                    ReasonCode = item.ReasonCode,
                    Message = item.Message,
                }),
            ],
            Warnings =
            [
                .. order.Warnings.Select((warning, index) => new RoutingOrderWarningEntity
                {
                    Ordinal = index + 1,
                    Code = warning.Code,
                    Message = warning.Message,
                    UnfulfilledProductCodes = [.. warning.UnfulfilledProductCodes],
                    AffectedProductCodes = [.. warning.AffectedProductCodes],
                }),
            ],
        };

    /// <summary>Maps the entity graph (items and warnings loaded, in order) back to the domain order.</summary>
    /// <param name="entity">The stored order entity.</param>
    public static StoredRoutingOrder ToDomainModel(this RoutingOrderEntity entity)
        => new(
            entity.Id,
            entity.OrderId,
            entity.CallerHash,
            entity.CustomerZip,
            entity.MailOrder,
            entity.Status,
            entity.Feasible,
            entity.ExperimentArms,
            entity.ConfigVersion,
            entity.SearchMode,
            entity.CreatedAt,
            [
                .. entity.Items.Select(item => new StoredOrderItem(
                    item.LineNumber,
                    item.ProductCode,
                    item.Quantity,
                    item.Category,
                    item.SupplierId,
                    item.SupplierName,
                    item.FulfillmentMode,
                    item.ReasonCode,
                    item.Message)),
            ],
            [
                .. entity.Warnings.Select(warning => new RoutingWarning(
                    warning.Code,
                    warning.Message,
                    [.. warning.UnfulfilledProductCodes],
                    [.. warning.AffectedProductCodes])),
            ]);
}

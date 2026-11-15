namespace OrderRouter.Domain;

/// <summary>
/// Marker type that identifies the Domain assembly.
/// Tests and dependency-injection assembly scanning use it to obtain this layer's
/// <see cref="System.Reflection.Assembly"/> without referencing an arbitrary type.
/// It contains no business logic.
/// </summary>
public static class DomainAssemblyMarker
{
}

using System.ComponentModel.DataAnnotations;

namespace OrderRouter.Api.ViewModels.Request;

/// <summary>Rejects a collection that contains <c>null</c> entries, which element validation would skip.</summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class NoNullElementsAttribute : ValidationAttribute
{
    /// <inheritdoc />
    public override bool IsValid(object? value)
        => value is not System.Collections.IEnumerable items || items.Cast<object?>().All(item => item is not null);
}

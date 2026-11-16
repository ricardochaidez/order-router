using OrderRouter.UnitTests.DataNormalizer;

namespace OrderRouter.UnitTests.Domain.ReferenceData;

/// <summary>
/// Protects the OR-006 whitespace decision: the database side only lower-cases, so no stored product code may
/// contain whitespace, or two codes could collapse into one lookup key.
/// </summary>
public class SeedProductCodeGuardTests
{
    [Fact]
    public async Task SeedProducts_Always_HaveNoWhitespaceInTheCode()
    {
        var path = Path.Combine(TestRepository.SeedDirectory, "products.normalized.csv");
        var products = await OrderRouter.Infrastructure.Persistence.Seeding.NormalizedSeedFileReader
            .ReadProductsAsync(path, TestContext.Current.CancellationToken);

        Assert.NotEmpty(products);
        Assert.DoesNotContain(products, product => product.Code.Any(char.IsWhiteSpace));
    }
}

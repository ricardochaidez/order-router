using System.Security.Claims;
using OrderRouter.Application.Routing.Experiments;

namespace OrderRouter.UnitTests.Application.Routing;

public class CallerIdentityTests
{
    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims)
        => new(new ClaimsIdentity([.. claims.Select(claim => new Claim(claim.Type, claim.Value))], "test"));

    [Fact]
    public void Resolve_SubWinsOverTheOtherClaims()
        => Assert.Equal("the-sub", CallerIdentity.Resolve(Principal(("azp", "the-azp"), ("client_id", "the-client"), ("sub", "the-sub"))));

    [Fact]
    public void Resolve_NoSub_UsesClientId()
        => Assert.Equal("the-client", CallerIdentity.Resolve(Principal(("azp", "the-azp"), ("client_id", "the-client"))));

    [Fact]
    public void Resolve_NoSubOrClientId_UsesAzp()
        => Assert.Equal("the-azp", CallerIdentity.Resolve(Principal(("azp", "the-azp"))));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_BlankSub_FallsThrough(string blank)
        => Assert.Equal("the-client", CallerIdentity.Resolve(Principal(("sub", blank), ("client_id", "the-client"))));

    [Fact]
    public void Resolve_NoClaims_IsNull()
        => Assert.Null(CallerIdentity.Resolve(Principal(("name", "someone"))));

    [Fact]
    public void Resolve_NullPrincipal_IsNull()
        => Assert.Null(CallerIdentity.Resolve(null));

    [Fact]
    public void Resolve_DevKeyPrincipal_IsDevKey()
        => Assert.Equal("dev-key", CallerIdentity.Resolve(Principal(("sub", "dev-key"))));
}

using OrderRouter.Domain.Sections;

namespace OrderRouter.Tests.Domain.Sections;

public class ItemDependencyOptionsValidatorTests
{
    private readonly ItemDependencyOptionsValidator validator = new();

    private static ItemDependencyOptions Options(params ItemDependencyRuleOptions[] rules) => new() { Rules = [.. rules] };

    private static ItemDependencyRuleOptions Rule(string? category, params string?[] requires) => new() { Category = category, Requires = [.. requires] };

    // DR-03: the shipped rule set is empty.
    [Fact]
    public void Validate_Defaults_Succeeds()
    {
        Assert.Empty(new ItemDependencyOptions().Rules);
        Assert.True(validator.Validate(null, new ItemDependencyOptions()).Succeeded);
    }

    // AC4, AC5: a category the reference data may not have is still well-formed; startup never checks the data.
    [Fact]
    public void Validate_WellFormedRules_Succeeds()
        => Assert.True(validator.Validate(null, Options(Rule("hospital bed", "patient lift"), Rule("not in the data", "cpap", "nebulizer"))).Succeeded);

    // OR-020 AC3.
    [Fact]
    public void Validate_EmptyCategory_FailsWithTheRulePath()
    {
        var result = validator.Validate(null, Options(Rule("hospital bed", "patient lift"), Rule("  ", "patient lift")));

        Assert.True(result.Failed);
        Assert.Equal("ItemDependencies:Rules:1:Category must not be empty.", Assert.Single(result.Failures!));
    }

    [Fact]
    public void Validate_NoRequirement_Fails()
        => Assert.Equal("ItemDependencies:Rules:0:Requires must name at least one category.", Assert.Single(validator.Validate(null, Options(Rule("hospital bed"))).Failures!));

    [Fact]
    public void Validate_EmptyRequirement_Fails()
        => Assert.Equal("ItemDependencies:Rules:0:Requires:1 must not be empty.", Assert.Single(validator.Validate(null, Options(Rule("hospital bed", "patient lift", ""))).Failures!));

    [Fact]
    public void Validate_RequiresItself_Fails()
        => Assert.Contains("cannot require itself", Assert.Single(validator.Validate(null, Options(Rule("Hospital Bed", "hospital bed"))).Failures!), StringComparison.Ordinal);

    [Fact]
    public void Validate_TwoRulesForOneCategory_Fails()
        => Assert.Contains("already has a rule", Assert.Single(validator.Validate(null, Options(Rule("hospital bed", "patient lift"), Rule(" HOSPITAL BED ", "commode"))).Failures!), StringComparison.Ordinal);

    [Fact]
    public void Validate_NullRule_Fails()
        => Assert.Equal("ItemDependencies:Rules:0 is empty.", Assert.Single(validator.Validate(null, Options([null!])).Failures!));

    // No category and no requirement in the first rule; a self-requirement and an empty name in the second.
    [Fact]
    public void Validate_SeveralProblems_ReportsAllAtOnce()
        => Assert.Equal(4, validator.Validate(null, Options(Rule(null), Rule("cpap", "cpap", " "))).Failures!.Count());
}

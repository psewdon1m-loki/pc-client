namespace Client.Routing;

public sealed record RoutingRuleSet
{
    public string Id { get; init; } = "russia-smart";
    public string Name { get; init; } = "Russia smart";
    public string DomainStrategy { get; init; } = "AsIs";
    public IReadOnlyList<RoutingRule> Rules { get; init; } = Array.Empty<RoutingRule>();
}

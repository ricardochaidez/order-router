namespace OrderRouter.Api.ViewModels.Response;

/// <summary>The arm one experiment gave an order, and why (AB-04).</summary>
/// <param name="Arm"><c>control</c> or <c>treatment</c>.</param>
/// <param name="Reason">Why the order got that arm, for example <c>disabled</c> or <c>bucketed</c>.</param>
public sealed record ExperimentDiagnosticResponseViewModel(string Arm, string Reason);

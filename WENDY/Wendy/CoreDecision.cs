namespace WENDY.Wendy;

/// <summary>
/// The result of the core interpreting the reports returned by managers.
/// </summary>
public sealed record CoreDecision(
    CoreDecisionKind Kind,
    string Summary,
    string RecommendedNextStep,
    IReadOnlyList<Managers.ManagerReport> Reports);

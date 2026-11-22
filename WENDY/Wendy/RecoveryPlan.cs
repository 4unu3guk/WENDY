namespace WENDY.Wendy;

/// <summary>
/// A diagnostic result together with the safe recovery actions currently available.
/// </summary>
public sealed record RecoveryPlan(
    CoreExecutionResult Diagnostic,
    IReadOnlyList<RecoveryAction> Actions,
    string Explanation);

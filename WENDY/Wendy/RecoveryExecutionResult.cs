using WENDY.Modules;

namespace WENDY.Wendy;

/// <summary>
/// Records an approved recovery attempt and its follow-up verification.
/// </summary>
public sealed record RecoveryExecutionResult(
    RecoveryAction Action,
    bool WasApproved,
    ModuleResult ActionResult,
    CoreDecision? VerificationDecision)
{
    public bool WasExecuted => WasApproved && ActionResult.Succeeded;
    public bool VerificationSucceeded => VerificationDecision?.Kind == CoreDecisionKind.YouTubeReachable;
}

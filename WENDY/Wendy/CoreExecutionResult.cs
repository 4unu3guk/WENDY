using WENDY.Modules;

namespace WENDY.Wendy;

/// <summary>
/// Captures a core decision and the outcome of the module dispatch that followed it.
/// </summary>
public sealed record CoreExecutionResult(
    CoreDecision Decision,
    string? SelectedModuleName,
    ModuleResult? ModuleResult,
    string ExecutionStatus)
{
    public bool ModuleWasSelected => SelectedModuleName is not null;
    public bool ModuleSucceeded => ModuleResult?.Succeeded == true;
}

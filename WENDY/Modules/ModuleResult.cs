namespace WENDY.Modules;

/// <summary>
/// The result returned by a module to WendyCore.
/// The core interprets this result and chooses the next step.
/// </summary>
public sealed record ModuleResult(
    bool Succeeded,
    string Summary,
    IReadOnlyDictionary<string, string> Data);

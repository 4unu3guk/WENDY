namespace WENDY.Modules;

/// <summary>
/// A task prepared by WendyCore for a module.
/// Parameters are intentionally generic so modules remain loosely coupled.
/// </summary>
public sealed record ModuleRequest(
    string Operation,
    IReadOnlyDictionary<string, string> Parameters);

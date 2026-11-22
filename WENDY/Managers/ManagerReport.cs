namespace WENDY.Managers;

/// <summary>
/// A snapshot of information collected by a manager.
/// Managers report facts; WendyCore decides what to do with them.
/// </summary>
public sealed record ManagerReport(
    string ManagerName,
    DateTimeOffset CollectedAt,
    bool IsHealthy,
    string Summary,
    IReadOnlyDictionary<string, string> Facts);

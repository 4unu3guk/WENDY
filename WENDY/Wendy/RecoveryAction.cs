namespace WENDY.Wendy;

/// <summary>
/// A recovery action proposed by WendyCore after interpreting diagnostic results.
/// </summary>
public sealed record RecoveryAction(
    string Id,
    string Title,
    string Description,
    string Impact,
    string RequiredPrivileges,
    string Operation);

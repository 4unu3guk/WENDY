namespace WENDY.Managers;

/// <summary>
/// A manager observes and reports the state of its area.
/// It does not decide which module should run.
/// </summary>
public interface IManager
{
    string Name { get; }

    Task<ManagerReport> CollectAsync(CancellationToken cancellationToken = default);
}

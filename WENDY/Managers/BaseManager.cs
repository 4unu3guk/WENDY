namespace WENDY.Managers;

/// <summary>
/// Shared base for managers. Concrete managers implement their own data collection.
/// </summary>
public abstract class BaseManager : IManager
{
    public abstract string Name { get; }

    public Task<ManagerReport> CollectAsync(CancellationToken cancellationToken = default)
        => CollectCoreAsync(cancellationToken);

    protected abstract Task<ManagerReport> CollectCoreAsync(
        CancellationToken cancellationToken);
}

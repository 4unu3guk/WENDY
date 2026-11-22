using WENDY.Managers;
using WENDY.Modules;

namespace WENDY.Wendy;

/// <summary>
/// Central coordinator of WENDY.
/// Managers report observations; this core will interpret them and decide the next action.
/// </summary>
public sealed class WendyCore
{
    private readonly List<IManager> _managers = [];
    private readonly List<IModule> _modules = [];

    public IReadOnlyCollection<IManager> Managers => _managers.AsReadOnly();
    public IReadOnlyCollection<IModule> Modules => _modules.AsReadOnly();

    public void RegisterManager(IManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);

        if (_managers.Any(item => item.Name == manager.Name))
            throw new InvalidOperationException($"Manager '{manager.Name}' is already registered.");

        _managers.Add(manager);
    }

    public void RegisterModule(IModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        if (_modules.Any(item => item.Name == module.Name))
            throw new InvalidOperationException($"Module '{module.Name}' is already registered.");

        _modules.Add(module);
    }

    public async Task<IReadOnlyList<ManagerReport>> CollectReportsAsync(
        CancellationToken cancellationToken = default)
    {
        var reports = new List<ManagerReport>(_managers.Count);

        foreach (var manager in _managers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reports.Add(await manager.CollectAsync(cancellationToken));
        }

        return reports.AsReadOnly();
    }
}

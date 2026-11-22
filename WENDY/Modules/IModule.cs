namespace WENDY.Modules;

/// <summary>
/// Common contract for WENDY modules.
/// A module performs a requested operation and returns a result.
/// </summary>
public interface IModule
{
    string Name { get; }

    bool CanExecute(ModuleRequest request);

    Task<ModuleResult> ExecuteAsync(
        ModuleRequest request,
        CancellationToken cancellationToken = default);
}

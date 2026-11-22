using WENDY.Managers;
using WENDY.Modules;

namespace WENDY.Wendy;

/// <summary>
/// Central coordinator of WENDY.
/// Managers report observations; this core interprets them and decides the next action.
/// </summary>
public sealed class WendyCore
{
    private const string NetworkManagerName = "Network";

    private readonly List<IManager> _managers = [];
    private readonly List<IModule> _modules = [];

    public IReadOnlyCollection<IManager> Managers => _managers.AsReadOnly();
    public IReadOnlyCollection<IModule> Modules => _modules.AsReadOnly();

    public void RegisterManager(IManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);

        if (_managers.Any(item => string.Equals(item.Name, manager.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Manager '{manager.Name}' is already registered.");

        _managers.Add(manager);
    }

    public void RegisterModule(IModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        if (_modules.Any(item => string.Equals(item.Name, module.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Module '{module.Name}' is already registered.");

        _modules.Add(module);
    }

    /// <summary>
    /// Runs managers, interprets their reports, selects a capable module and executes it.
    /// The core owns the decision and dispatch process.
    /// </summary>
    public async Task<CoreExecutionResult> DiagnoseAndExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var decision = await DiagnoseAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var request = CreateModuleRequest(decision);
        var selectedModule = _modules.FirstOrDefault(module => module.CanExecute(request));

        if (selectedModule is null)
        {
            return new CoreExecutionResult(
                decision,
                null,
                null,
                "No registered module declared support for this task.");
        }

        var result = await selectedModule.ExecuteAsync(request, cancellationToken);

        return new CoreExecutionResult(
            decision,
            selectedModule.Name,
            result,
            result.Succeeded
                ? "The selected module completed the task."
                : "The selected module reported that the task did not succeed.");
    }

    public async Task<CoreDecision> DiagnoseAsync(
        CancellationToken cancellationToken = default)
    {
        var reports = await CollectReportsAsync(cancellationToken);
        return InterpretReports(reports);
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

    private static ModuleRequest CreateModuleRequest(CoreDecision decision)
    {
        var parameters = new Dictionary<string, string>
        {
            ["DecisionKind"] = decision.Kind.ToString(),
            ["Summary"] = decision.Summary,
            ["RecommendedNextStep"] = decision.RecommendedNextStep
        };

        return new ModuleRequest(
            $"Handle{decision.Kind}",
            parameters);
    }

    private static CoreDecision InterpretReports(IReadOnlyList<ManagerReport> reports)
    {
        if (reports.Count == 0)
        {
            return new CoreDecision(
                CoreDecisionKind.NoManagersRegistered,
                "No diagnostic managers are registered.",
                "Register a manager before starting diagnostics.",
                reports);
        }

        var networkReport = reports.FirstOrDefault(
            report => string.Equals(
                report.ManagerName,
                NetworkManagerName,
                StringComparison.OrdinalIgnoreCase));

        if (networkReport is null)
        {
            return new CoreDecision(
                CoreDecisionKind.NoApplicableDiagnostic,
                "The core received reports, but none contain a supported network diagnostic.",
                "Wait for a decision rule for the available manager types.",
                reports);
        }

        if (!TryReadFact(networkReport, "Dns.Success", out var dnsSucceeded))
            return UnknownResult(reports, "The network report does not contain a DNS result.");

        if (!dnsSucceeded)
        {
            return new CoreDecision(
                CoreDecisionKind.DnsFailure,
                "YouTube's hostname could not be resolved.",
                "Investigate DNS resolution before testing higher network layers.",
                reports);
        }

        if (!TryReadFact(networkReport, "Tcp443.Success", out var tcpSucceeded))
            return UnknownResult(reports, "The network report does not contain a TCP port 443 result.");

        if (!tcpSucceeded)
        {
            return new CoreDecision(
                CoreDecisionKind.TcpFailure,
                "DNS works, but a TCP connection to YouTube on port 443 failed.",
                "Investigate the route or connection to the HTTPS port.",
                reports);
        }

        if (!TryReadFact(networkReport, "Https.Success", out var httpsSucceeded))
            return UnknownResult(reports, "The network report does not contain an HTTPS result.");

        if (!httpsSucceeded)
        {
            return new CoreDecision(
                CoreDecisionKind.HttpsFailure,
                "DNS and TCP work, but the HTTPS request to YouTube failed.",
                "Investigate the HTTPS/TLS exchange or the response from YouTube.",
                reports);
        }

        return new CoreDecision(
            CoreDecisionKind.YouTubeReachable,
            "YouTube responded successfully to the DNS, TCP and HTTPS checks.",
            "Basic connectivity is available; continue with application-level diagnostics if playback still fails.",
            reports);
    }

    private static bool TryReadFact(
        ManagerReport report,
        string key,
        out bool value)
    {
        value = false;
        return report.Facts.TryGetValue(key, out var rawValue)
            && bool.TryParse(rawValue, out value);
    }

    private static CoreDecision UnknownResult(
        IReadOnlyList<ManagerReport> reports,
        string reason)
        => new(
            CoreDecisionKind.IncompleteReport,
            reason,
            "Check the manager report before selecting a recovery action.",
            reports);
}

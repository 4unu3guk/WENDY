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
    private const string FlushDnsCacheActionId = "network.flush-dns-cache";

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

    /// <summary>
    /// Runs diagnostics and proposes only recovery actions supported by the collected evidence.
    /// </summary>
    public async Task<RecoveryPlan> CreateRecoveryPlanAsync(
        CancellationToken cancellationToken = default)
    {
        var diagnostic = await DiagnoseAndExecuteAsync(cancellationToken);
        var actions = new List<RecoveryAction>();
        string explanation;

        if (diagnostic.Decision.Kind == CoreDecisionKind.DnsFailure)
        {
            var controlDnsSucceeded = TryReadModuleFact(
                diagnostic.ModuleResult,
                "ControlCheck.Dns.Success",
                out var controlDnsValue)
                && controlDnsValue;

            actions.Add(new RecoveryAction(
                FlushDnsCacheActionId,
                "Flush the Windows DNS cache",
                "Clear cached hostname-to-IP results, then repeat the YouTube connectivity checks.",
                "This clears the local DNS resolver cache only. It does not change DNS servers, proxy settings, routes, or provider configuration.",
                "Normally no administrator privileges are required; Windows may deny the operation under restricted policies.",
                "FlushDnsCache"));

            explanation = controlDnsSucceeded
                ? "The control hostname resolved while YouTube did not. This points to a YouTube-specific or cached DNS result. Flushing the local cache is a limited, low-impact maintenance step, but it cannot bypass provider-side restrictions."
                : "Both YouTube and the control hostname failed DNS resolution. This suggests a broader DNS problem. Flushing the local cache is a low-impact first step; if DNS still fails, Wendy should investigate adapter and resolver configuration rather than changing it blindly.";
        }
        else
        {
            explanation = diagnostic.Decision.Kind switch
            {
                CoreDecisionKind.YouTubeReachable =>
                    "The basic YouTube DNS, TCP and HTTPS checks succeeded. No network repair is recommended by this diagnostic run.",
                CoreDecisionKind.TcpFailure =>
                    "DNS resolved, but the TCP connection failed. Wendy has no sufficiently supported, low-risk automatic repair for this result; inspect routing, firewall and provider connectivity before changing settings.",
                CoreDecisionKind.HttpsFailure =>
                    "DNS and TCP succeeded, but HTTPS failed. The evidence is not sufficient to alter DNS or proxy settings automatically; the next diagnostic stage should inspect TLS, proxy and HTTP response details.",
                CoreDecisionKind.IncompleteReport =>
                    "The diagnostic report is incomplete. Recovery is withheld until the missing evidence is collected.",
                _ =>
                    "The available diagnostic evidence does not currently justify an automatic network change."
            };
        }

        return new RecoveryPlan(diagnostic, actions.AsReadOnly(), explanation);
    }

    /// <summary>
    /// Executes a recovery action only when the caller explicitly confirms approval.
    /// Successful actions are followed by a fresh diagnostic verification.
    /// </summary>
    public async Task<RecoveryExecutionResult> ExecuteApprovedRecoveryAsync(
        RecoveryAction action,
        bool userApproved,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        if (action.Id != FlushDnsCacheActionId || action.Operation != "FlushDnsCache")
        {
            return new RecoveryExecutionResult(
                action,
                userApproved,
                new ModuleResult(
                    false,
                    "WendyCore rejected an unknown or modified recovery action.",
                    new Dictionary<string, string> { ["Recovery.Executed"] = "False" }),
                null);
        }

        if (!userApproved)
        {
            return new RecoveryExecutionResult(
                action,
                false,
                new ModuleResult(
                    false,
                    "The recovery action was not executed because the user did not approve it.",
                    new Dictionary<string, string> { ["Recovery.Executed"] = "False" }),
                null);
        }

        var request = new ModuleRequest(
            action.Operation,
            new Dictionary<string, string> { ["UserApproved"] = "True" });

        var selectedModule = _modules.FirstOrDefault(module => module.CanExecute(request));
        if (selectedModule is null)
        {
            return new RecoveryExecutionResult(
                action,
                true,
                new ModuleResult(
                    false,
                    "No registered module can execute this recovery action.",
                    new Dictionary<string, string> { ["Recovery.Executed"] = "False" }),
                null);
        }

        var result = await selectedModule.ExecuteAsync(request, cancellationToken);
        var verification = result.Succeeded
            ? await DiagnoseAsync(cancellationToken)
            : null;

        return new RecoveryExecutionResult(action, true, result, verification);
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

        return new ModuleRequest($"Handle{decision.Kind}", parameters);
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

    private static bool TryReadFact(ManagerReport report, string key, out bool value)
    {
        value = false;
        return report.Facts.TryGetValue(key, out var rawValue)
            && bool.TryParse(rawValue, out value);
    }

    private static bool TryReadModuleFact(
        ModuleResult? result,
        string key,
        out bool value)
    {
        value = false;
        return result is not null
            && result.Data.TryGetValue(key, out var rawValue)
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

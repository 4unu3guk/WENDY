using System.Windows;
using WENDY.Wendy;

namespace WENDY;

public partial class MainWindow : Window
{
    public WendyCore Core { get; }

    public MainWindow(WendyCore core)
    {
        ArgumentNullException.ThrowIfNull(core);

        Core = core;
        InitializeComponent();
    }

    /// <summary>
    /// Entry point for the UI to request a diagnostic run without owning diagnostic logic.
    /// </summary>
    public Task<CoreDecision> RunDiagnosticsAsync(
        CancellationToken cancellationToken = default)
        => Core.DiagnoseAsync(cancellationToken);

    /// <summary>
    /// Runs diagnostics and lets WendyCore dispatch the resulting task to a capable module.
    /// </summary>
    public Task<CoreExecutionResult> RunDiagnosticWorkflowAsync(
        CancellationToken cancellationToken = default)
        => Core.DiagnoseAndExecuteAsync(cancellationToken);

    /// <summary>
    /// Requests a diagnostic run and receives the recovery options selected by WendyCore.
    /// The UI should display the action details and obtain approval before execution.
    /// </summary>
    public Task<RecoveryPlan> CreateRecoveryPlanAsync(
        CancellationToken cancellationToken = default)
        => Core.CreateRecoveryPlanAsync(cancellationToken);

    /// <summary>
    /// Executes a displayed recovery action only after the UI has obtained explicit approval.
    /// </summary>
    public Task<RecoveryExecutionResult> ExecuteApprovedRecoveryAsync(
        RecoveryAction action,
        bool userApproved,
        CancellationToken cancellationToken = default)
        => Core.ExecuteApprovedRecoveryAsync(action, userApproved, cancellationToken);
}

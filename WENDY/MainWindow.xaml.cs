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
}

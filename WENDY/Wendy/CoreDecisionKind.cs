namespace WENDY.Wendy;

/// <summary>
/// High-level diagnostic outcomes understood by WendyCore.
/// </summary>
public enum CoreDecisionKind
{
    NoManagersRegistered,
    NoApplicableDiagnostic,
    IncompleteReport,
    DnsFailure,
    NoDnsServersConfigured,
    NetworkAdapterUnavailable,
    NoDefaultGateway,
    TcpFailure,
    HttpsFailure,
    YouTubeReachable
}

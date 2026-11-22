using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace WENDY.Managers;

/// <summary>
/// Collects read-only Windows network configuration context for interpreting connectivity failures.
/// </summary>
public sealed class NetworkEnvironmentManager : BaseManager
{
    private const string InternetSettingsRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    public override string Name => "NetworkEnvironment";

    protected override Task<ManagerReport> CollectCoreAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var facts = new Dictionary<string, string>();
        var activeAdapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter =>
                adapter.OperationalStatus == OperationalStatus.Up
                && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .ToArray();

        var adapterDescriptions = new List<string>();
        var dnsServers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var gateways = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var adapter in activeAdapters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var properties = adapter.GetIPProperties();
            var adapterDns = properties.DnsAddresses
                .Where(address => !IPAddress.IsLoopback(address))
                .Select(address => address.ToString())
                .ToArray();
            var adapterGateways = properties.GatewayAddresses
                .Select(gateway => gateway.Address)
                .Where(address => address is not null
                    && !IPAddress.Any.Equals(address)
                    && !IPAddress.IPv6Any.Equals(address))
                .Select(address => address!.ToString())
                .ToArray();

            foreach (var address in adapterDns)
                dnsServers.Add(address);

            foreach (var address in adapterGateways)
                gateways.Add(address);

            adapterDescriptions.Add(
                $"{adapter.Name} ({adapter.NetworkInterfaceType}): "
                + $"DNS [{string.Join(", ", adapterDns)}], "
                + $"Gateway [{string.Join(", ", adapterGateways)}]");
        }

        facts["Adapters.ActiveCount"] = activeAdapters.Length.ToString();
        facts["Adapters.Active"] = string.Join(Environment.NewLine, adapterDescriptions);
        facts["Dns.Servers"] = string.Join(", ", dnsServers);
        facts["Gateways"] = string.Join(", ", gateways);

        ReadProxyConfiguration(facts);

        var summary = activeAdapters.Length > 0
            ? $"Collected configuration for {activeAdapters.Length} active network adapter(s)."
            : "No active non-loopback network adapters were found.";

        var report = new ManagerReport(
            Name,
            DateTimeOffset.UtcNow,
            activeAdapters.Length > 0,
            summary,
            facts);

        return Task.FromResult(report);
    }

    private static void ReadProxyConfiguration(IDictionary<string, string> facts)
    {
        try
        {
            using var settings = Registry.CurrentUser.OpenSubKey(InternetSettingsRegistryPath);

            var proxyEnabled = int.TryParse(
                settings?.GetValue("ProxyEnable")?.ToString(),
                out var proxyEnabledValue)
                && proxyEnabledValue == 1;
            var proxyServer = settings?.GetValue("ProxyServer")?.ToString() ?? string.Empty;
            var autoConfigUrl = settings?.GetValue("AutoConfigURL")?.ToString() ?? string.Empty;

            facts["Proxy.Enabled"] = proxyEnabled.ToString();
            facts["Proxy.Server"] = proxyServer;
            facts["Proxy.AutoConfigUrl"] = autoConfigUrl;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException
            or System.Security.SecurityException
            or IOException)
        {
            facts["Proxy.Readable"] = "False";
            facts["Proxy.Error"] = exception.Message;
        }
    }
}

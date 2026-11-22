using System.IO;
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
        NetworkInterface[] activeAdapters;

        try
        {
            activeAdapters = NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter =>
                    adapter.OperationalStatus == OperationalStatus.Up
                    && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToArray();
        }
        catch (NetworkInformationException exception)
        {
            facts["Adapters.Readable"] = "False";
            facts["Adapters.Error"] = exception.Message;
            ReadProxyConfiguration(facts);

            return Task.FromResult(new ManagerReport(
                Name,
                DateTimeOffset.UtcNow,
                false,
                "Windows did not provide network adapter information.",
                facts));
        }

        var adapterDescriptions = new List<string>();
        var dnsServers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var gateways = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var readableAdapterCount = 0;

        foreach (var adapter in activeAdapters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
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
                readableAdapterCount++;
            }
            catch (NetworkInformationException exception)
            {
                adapterDescriptions.Add(
                    $"{adapter.Name}: configuration unavailable ({exception.Message})");
            }
        }

        facts["Adapters.ActiveCount"] = activeAdapters.Length.ToString();
        facts["Adapters.ReadableCount"] = readableAdapterCount.ToString();
        facts["Adapters.Active"] = string.Join(Environment.NewLine, adapterDescriptions);
        facts["Dns.Servers"] = string.Join(", ", dnsServers);
        facts["Gateways"] = string.Join(", ", gateways);
        ReadProxyConfiguration(facts);

        var isHealthy = activeAdapters.Length > 0 && readableAdapterCount > 0;
        var summary = isHealthy
            ? $"Collected configuration for {readableAdapterCount} of {activeAdapters.Length} active network adapter(s)."
            : "No active network adapter configuration could be read.";

        return Task.FromResult(new ManagerReport(
            Name,
            DateTimeOffset.UtcNow,
            isHealthy,
            summary,
            facts));
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
            facts["Proxy.Readable"] = "True";
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

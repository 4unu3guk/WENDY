using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace WENDY.Modules;

/// <summary>
/// Performs a read-only control check against an unrelated HTTPS host.
/// This helps distinguish a general connectivity problem from a YouTube-specific one.
/// </summary>
public sealed class NetworkModule : IModule
{
    private const string ControlHost = "www.microsoft.com";
    private const string ControlUrl = "https://www.microsoft.com/";
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(8);

    public string Name => "NetworkControlCheck";

    public bool CanExecute(ModuleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Operation is
            "HandleDnsFailure" or
            "HandleTcpFailure" or
            "HandleHttpsFailure" or
            "HandleYouTubeReachable";
    }

    public async Task<ModuleResult> ExecuteAsync(
        ModuleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Operation == "HandleYouTubeReachable")
        {
            return new ModuleResult(
                true,
                "YouTube passed the basic connectivity checks; no network recovery action is indicated.",
                new Dictionary<string, string>
                {
                    ["ControlCheck.Performed"] = "False",
                    ["ControlCheck.Reason"] = "YouTube is already reachable."
                });
        }

        return request.Operation switch
        {
            "HandleDnsFailure" => await CheckDnsAsync(cancellationToken),
            "HandleTcpFailure" => await CheckTcpAsync(cancellationToken),
            "HandleHttpsFailure" => await CheckHttpsAsync(cancellationToken),
            _ => new ModuleResult(
                false,
                $"Unsupported network operation: {request.Operation}",
                new Dictionary<string, string>())
        };
    }

    private static async Task<ModuleResult> CheckDnsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(ControlHost, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            return new ModuleResult(
                addresses.Length > 0,
                addresses.Length > 0
                    ? "The control hostname resolved successfully; the DNS issue may be specific to YouTube."
                    : "The control hostname returned no IP addresses.",
                new Dictionary<string, string>
                {
                    ["ControlCheck.Host"] = ControlHost,
                    ["ControlCheck.Dns.Success"] = (addresses.Length > 0).ToString(),
                    ["ControlCheck.Dns.Addresses"] = string.Join(", ", addresses.Select(address => address.ToString()))
                });
        }
        catch (SocketException exception)
        {
            return new ModuleResult(
                false,
                "The control hostname also failed to resolve; the DNS problem may be broader than YouTube.",
                new Dictionary<string, string>
                {
                    ["ControlCheck.Host"] = ControlHost,
                    ["ControlCheck.Dns.Success"] = "False",
                    ["ControlCheck.Dns.Error"] = exception.Message
                });
        }
    }

    private static async Task<ModuleResult> CheckTcpAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(StepTimeout);

            using var client = new TcpClient();
            await client.ConnectAsync(ControlHost, 443, timeout.Token);

            return new ModuleResult(
                client.Connected,
                client.Connected
                    ? "The control host accepts TCP connections; the failure may be specific to YouTube's route."
                    : "The control host did not accept a TCP connection.",
                new Dictionary<string, string>
                {
                    ["ControlCheck.Host"] = ControlHost,
                    ["ControlCheck.Tcp443.Success"] = client.Connected.ToString()
                });
        }
        catch (SocketException exception)
        {
            return new ModuleResult(
                false,
                "The control host also rejected or failed the TCP connection.",
                new Dictionary<string, string>
                {
                    ["ControlCheck.Host"] = ControlHost,
                    ["ControlCheck.Tcp443.Success"] = "False",
                    ["ControlCheck.Tcp443.Error"] = exception.Message
                });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ModuleResult(
                false,
                "The control TCP connection timed out.",
                new Dictionary<string, string>
                {
                    ["ControlCheck.Host"] = ControlHost,
                    ["ControlCheck.Tcp443.Success"] = "False",
                    ["ControlCheck.Tcp443.Error"] = "Connection attempt timed out."
                });
        }
    }

    private static async Task<ModuleResult> CheckHttpsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(StepTimeout);

            using var client = new HttpClient();
            using var response = await client.GetAsync(
                ControlUrl,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);

            return new ModuleResult(
                response.IsSuccessStatusCode,
                response.IsSuccessStatusCode
                    ? "The control HTTPS request succeeded; the failure may be specific to YouTube."
                    : "The control HTTPS host responded with an unsuccessful status code.",
                new Dictionary<string, string>
                {
                    ["ControlCheck.Host"] = ControlHost,
                    ["ControlCheck.Https.Success"] = response.IsSuccessStatusCode.ToString(),
                    ["ControlCheck.Https.StatusCode"] = ((int)response.StatusCode).ToString(),
                    ["ControlCheck.Https.FinalUri"] = response.RequestMessage?.RequestUri?.ToString() ?? ControlUrl
                });
        }
        catch (HttpRequestException exception)
        {
            return new ModuleResult(
                false,
                "The control HTTPS request also failed.",
                new Dictionary<string, string>
                {
                    ["ControlCheck.Host"] = ControlHost,
                    ["ControlCheck.Https.Success"] = "False",
                    ["ControlCheck.Https.Error"] = exception.Message
                });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ModuleResult(
                false,
                "The control HTTPS request timed out.",
                new Dictionary<string, string>
                {
                    ["ControlCheck.Host"] = ControlHost,
                    ["ControlCheck.Https.Success"] = "False",
                    ["ControlCheck.Https.Error"] = "HTTPS request timed out."
                });
        }
    }
}

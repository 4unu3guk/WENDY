using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.Http;
using WENDY.Managers;

namespace WENDY.Managers;

/// <summary>
/// Performs read-only connectivity checks for YouTube.
/// It collects facts only; WendyCore remains responsible for decisions.
/// </summary>
public sealed class NetworkManager : BaseManager
{
    private const string TargetHost = "www.youtube.com";
    private const string ProbeUrl = "https://www.youtube.com/generate_204";
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(8);

    public override string Name => "Network";

    protected override async Task<ManagerReport> CollectCoreAsync(
        CancellationToken cancellationToken)
    {
        var facts = new Dictionary<string, string>();
        var dnsSucceeded = false;
        var tcpSucceeded = false;
        var httpsSucceeded = false;

        // Step 1: Can the system resolve YouTube's hostname?
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(TargetHost, cancellationToken);
            dnsSucceeded = addresses.Length > 0;
            facts["Dns.Success"] = dnsSucceeded.ToString();
            facts["Dns.Addresses"] = string.Join(", ", addresses.Select(address => address.ToString()));
        }
        catch (Exception exception) when (
            exception is SocketException or ArgumentException)
        {
            facts["Dns.Success"] = false.ToString();
            facts["Dns.Error"] = exception.Message;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Step 2: Can the device establish a TCP connection to port 443?
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(StepTimeout);

            using var client = new TcpClient();
            await client.ConnectAsync(TargetHost, 443, timeout.Token);
            tcpSucceeded = client.Connected;
            facts["Tcp443.Success"] = tcpSucceeded.ToString();
        }
        catch (Exception exception) when (
            exception is SocketException or OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            facts["Tcp443.Success"] = false.ToString();
            facts["Tcp443.Error"] = exception.Message;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Step 3: Can an HTTPS request reach YouTube and receive a successful response?
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(StepTimeout);

            using var client = new HttpClient();
            using var response = await client.GetAsync(
                ProbeUrl,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);

            httpsSucceeded = response.IsSuccessStatusCode;
            facts["Https.Success"] = httpsSucceeded.ToString();
            facts["Https.StatusCode"] = ((int)response.StatusCode).ToString();
            facts["Https.FinalUri"] = response.RequestMessage?.RequestUri?.ToString() ?? ProbeUrl;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            facts["Https.Success"] = false.ToString();
            facts["Https.Error"] = exception.Message;
        }

        var isHealthy = dnsSucceeded && tcpSucceeded && httpsSucceeded;
        var summary = isHealthy
            ? "YouTube is reachable over DNS, TCP and HTTPS."
            : "One or more connectivity checks for YouTube failed.";

        return new ManagerReport(
            Name,
            DateTimeOffset.UtcNow,
            isHealthy,
            summary,
            facts);
    }
}

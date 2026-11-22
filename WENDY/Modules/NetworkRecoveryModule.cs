using System.Diagnostics;
using System.IO;
using System.ComponentModel;

namespace WENDY.Modules;

/// <summary>
/// Executes narrowly scoped, user-approved Windows network maintenance operations.
/// </summary>
public sealed class NetworkRecoveryModule : IModule
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(15);

    public string Name => "NetworkRecovery";

    public bool CanExecute(ModuleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Operation == "FlushDnsCache";
    }

    public async Task<ModuleResult> ExecuteAsync(
        ModuleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!CanExecute(request))
        {
            return new ModuleResult(
                false,
                $"Unsupported recovery operation: {request.Operation}",
                new Dictionary<string, string>());
        }

        if (!request.Parameters.TryGetValue("UserApproved", out var approval)
            || !bool.TryParse(approval, out var isApproved)
            || !isApproved)
        {
            return new ModuleResult(
                false,
                "The operation was not executed because explicit user approval was not supplied.",
                new Dictionary<string, string> { ["Recovery.Executed"] = "False" });
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "ipconfig.exe"),
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("/flushdns");

        try
        {
            if (!process.Start())
            {
                return Failure("Windows did not start the DNS cache maintenance process.");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(OperationTimeout);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryStopProcess(process);
                throw;
            }
            catch (OperationCanceledException)
            {
                TryStopProcess(process);
                return Failure("Flushing the DNS cache timed out.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            return process.ExitCode == 0
                ? new ModuleResult(
                    true,
                    "Windows DNS resolver cache was flushed successfully.",
                    new Dictionary<string, string>
                    {
                        ["Recovery.Executed"] = "True",
                        ["Recovery.Operation"] = "FlushDnsCache",
                        ["Recovery.ExitCode"] = process.ExitCode.ToString()
                    })
                : Failure($"Windows DNS cache maintenance returned exit code {process.ExitCode}.");
        }
        catch (Win32Exception exception)
        {
            return Failure($"Windows could not start ipconfig.exe: {exception.Message}");
        }
    }

    private static void TryStopProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between the check and the kill attempt.
        }
        catch (Win32Exception)
        {
            // The process could not be stopped; cancellation still propagates to the caller.
        }
    }

    private static ModuleResult Failure(string summary)
        => new(
            false,
            summary,
            new Dictionary<string, string>
            {
                ["Recovery.Executed"] = "False",
                ["Recovery.Operation"] = "FlushDnsCache"
            });
}

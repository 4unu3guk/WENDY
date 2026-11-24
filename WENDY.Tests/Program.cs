using WENDY.Managers;
using WENDY.Wendy;

var failures = new List<string>();

await CheckAsync("No managers produces NoManagersRegistered", async () =>
{
    var decision = await new WendyCore().DiagnoseAsync();
    Assert(decision.Kind == CoreDecisionKind.NoManagersRegistered,
        $"Expected NoManagersRegistered, got {decision.Kind}.");
});

await CheckAsync("Successful connectivity produces YouTubeReachable", async () =>
{
    var core = new WendyCore();
    core.RegisterManager(new StubManager("Network", _ =>
        Task.FromResult(CreateNetworkReport(dns: true, tcp: true, https: true))));

    var decision = await core.DiagnoseAsync();
    Assert(decision.Kind == CoreDecisionKind.YouTubeReachable,
        $"Expected YouTubeReachable, got {decision.Kind}.");
});

await CheckAsync("No active adapter is identified before DNS failure", async () =>
{
    var core = new WendyCore();
    core.RegisterManager(new StubManager("Network", _ =>
        Task.FromResult(CreateNetworkReport(dns: false, tcp: false, https: false))));
    core.RegisterManager(new StubManager("NetworkEnvironment", _ =>
        Task.FromResult(CreateEnvironmentReport(activeAdapters: 0))));

    var decision = await core.DiagnoseAsync();
    Assert(decision.Kind == CoreDecisionKind.NetworkAdapterUnavailable,
        $"Expected NetworkAdapterUnavailable, got {decision.Kind}.");
});

await CheckAsync("A manager exception is reported and does not stop other managers", async () =>
{
    var secondaryManagerWasCalled = false;
    var core = new WendyCore();

    core.RegisterManager(new StubManager("Network", _ =>
        throw new InvalidOperationException("Synthetic test failure.")));
    core.RegisterManager(new StubManager("Secondary", _ =>
    {
        secondaryManagerWasCalled = true;
        return Task.FromResult(new ManagerReport(
            "Secondary",
            DateTimeOffset.UtcNow,
            true,
            "Secondary manager completed.",
            new Dictionary<string, string>()));
    }));

    var decision = await core.DiagnoseAsync();
    var failedReport = decision.Reports.Single(report => report.ManagerName == "Network");

    Assert(decision.Kind == CoreDecisionKind.IncompleteReport,
        $"Expected IncompleteReport, got {decision.Kind}.");
    Assert(secondaryManagerWasCalled, "The secondary manager was not called.");
    Assert(failedReport.Facts.TryGetValue("Manager.CollectionSucceeded", out var succeeded)
        && succeeded == "False", "The failed manager report is missing its failure marker.");
});

await CheckAsync("Caller cancellation is propagated", async () =>
{
    var core = new WendyCore();
    core.RegisterManager(new StubManager("Slow", async token =>
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        throw new InvalidOperationException("Unreachable test statement.");
    }));

    using var cancellation = new CancellationTokenSource();
    cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));

    var cancellationWasPropagated = false;
    try
    {
        await core.DiagnoseAsync(cancellation.Token);
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
        cancellationWasPropagated = true;
    }

    Assert(cancellationWasPropagated, "The caller's cancellation was swallowed.");
});

await CheckAsync("Duplicate manager names are rejected", () =>
{
    var core = new WendyCore();
    core.RegisterManager(new StubManager("Network", _ =>
        Task.FromResult(CreateNetworkReport(dns: true, tcp: true, https: true))));

    var rejected = false;
    try
    {
        core.RegisterManager(new StubManager("network", _ =>
            Task.FromResult(CreateNetworkReport(dns: true, tcp: true, https: true))));
    }
    catch (InvalidOperationException)
    {
        rejected = true;
    }

    Assert(rejected, "A duplicate manager name was accepted.");
    return Task.CompletedTask;
});

Console.WriteLine();
Console.WriteLine($"Tests passed: {6 - failures.Count}/6");
return failures.Count == 0 ? 0 : 1;

async Task CheckAsync(string name, Func<Task> test)
{
    try
    {
        await test();
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception exception)
    {
        failures.Add(name);
        Console.WriteLine($"FAIL  {name}: {exception.Message}");
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static ManagerReport CreateNetworkReport(bool dns, bool tcp, bool https)
    => new(
        "Network",
        DateTimeOffset.UtcNow,
        dns && tcp && https,
        "Synthetic network report.",
        new Dictionary<string, string>
        {
            ["Dns.Success"] = dns.ToString(),
            ["Tcp443.Success"] = tcp.ToString(),
            ["Https.Success"] = https.ToString()
        });

static ManagerReport CreateEnvironmentReport(int activeAdapters)
    => new(
        "NetworkEnvironment",
        DateTimeOffset.UtcNow,
        activeAdapters > 0,
        "Synthetic environment report.",
        new Dictionary<string, string>
        {
            ["Adapters.ActiveCount"] = activeAdapters.ToString(),
            ["Dns.Servers"] = "192.0.2.1",
            ["Gateways"] = "192.0.2.254"
        });

sealed class StubManager(
    string name,
    Func<CancellationToken, Task<ManagerReport>> collect) : IManager
{
    public string Name { get; } = name;

    public Task<ManagerReport> CollectAsync(CancellationToken cancellationToken = default)
        => collect(cancellationToken);
}

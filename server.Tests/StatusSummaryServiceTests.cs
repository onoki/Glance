using Glance.Server.Integrations.AzureDevOps;
using Glance.Server.Integrations.Outlook;
using Glance.Server.StatusUpdates;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json.Nodes;

namespace Glance.Server.Tests;

public sealed class StatusSummaryServiceTests
{
    [Fact]
    public async Task Collect_RerunReusesDailyReportAndIncrementsRevision()
    {
        await using var app = TestAppFixture.Create();
        var service = new StatusSummaryService(
            app.Paths,
            app.Tasks,
            new EmptyAzureReader(),
            new EmptyOutlookReader(),
            Options.Create(new StatusIntegrationOptions { ProjectName = "Test" }));

        var first = await service.CollectAsync(new StatusCollectRequest(null, null, null), CancellationToken.None);
        var (_, firstBytes) = await service.GetLatestJsonAsync(CancellationToken.None);
        var second = await service.CollectAsync(new StatusCollectRequest(null, null, null), CancellationToken.None);

        Assert.Equal(first.ReportId, second.ReportId);
        Assert.Equal(first.InputRevision + 1, second.InputRevision);
        Assert.Single(Directory.GetFiles(app.Paths.StatusUpdatesDirectory, "*.json.gz", SearchOption.AllDirectories));
        Assert.Equal("input", second.DocumentStatus);

        var stale = JsonNode.Parse(firstBytes)!.AsObject();
        stale["documentStatus"] = "completed";
        stale["output"]!["projectStatus"] = "Stale output";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(stale.ToJsonString()));
        var rejected = await service.ImportAsync(stream, CancellationToken.None);
        Assert.Contains(rejected.ValidationErrors, error => error.Contains("superseded", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Import_AcceptsCompletedOutputAndPreservesInputIntegrity()
    {
        await using var app = TestAppFixture.Create();
        var service = CreateService(app);
        await service.CollectAsync(new StatusCollectRequest("Test", null, null), CancellationToken.None);
        var (_, bytes) = await service.GetLatestJsonAsync(CancellationToken.None);
        var document = JsonNode.Parse(bytes)!.AsObject();
        document["documentStatus"] = "completed";
        document["output"]!["projectStatus"] = "On track";
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(document.ToJsonString()));

        var result = await service.ImportAsync(input, CancellationToken.None);

        Assert.Empty(result.ValidationErrors);
        Assert.Equal("completed", result.Run.DocumentStatus);
        Assert.NotNull(result.Run.CompletedAt);
    }

    private static StatusSummaryService CreateService(TestAppFixture app) => new(
        app.Paths,
        app.Tasks,
        new EmptyAzureReader(),
        new EmptyOutlookReader(),
        Options.Create(new StatusIntegrationOptions { ProjectName = "Test" }));

    private sealed class EmptyAzureReader : IAzureDevOpsStatusReader
    {
        public StatusProviderConfiguration GetConfiguration() => new(false, "Not configured");
        public Task<StatusSourceResult<AzureStatusData>> ReadAsync(StatusPeriod period, CancellationToken token) =>
            Task.FromResult(new StatusSourceResult<AzureStatusData>("notConfigured", null, "Not configured", new AzureStatusData(null, null, null, Array.Empty<AzureWorkItemInput>())));
    }

    private sealed class EmptyOutlookReader : IOutlookStatusReader
    {
        public StatusProviderConfiguration GetConfiguration() => new(false, "Not configured");
        public Task<StatusSourceResult<OutlookStatusData>> ReadAsync(StatusPeriod period, CancellationToken token) =>
            Task.FromResult(new StatusSourceResult<OutlookStatusData>("notConfigured", null, "Not configured", new OutlookStatusData(null, Array.Empty<OutlookMessageInput>())));
    }
}

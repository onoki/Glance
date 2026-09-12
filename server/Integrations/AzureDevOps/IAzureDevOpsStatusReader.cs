using Glance.Server.StatusUpdates;

namespace Glance.Server.Integrations.AzureDevOps;

public interface IAzureDevOpsStatusReader
{
    StatusProviderConfiguration GetConfiguration();
    Task<StatusSourceResult<AzureStatusData>> ReadAsync(StatusPeriod period, CancellationToken token);
}

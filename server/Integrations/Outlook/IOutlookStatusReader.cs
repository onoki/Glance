using Glance.Server.StatusUpdates;

namespace Glance.Server.Integrations.Outlook;

public interface IOutlookStatusReader
{
    StatusProviderConfiguration GetConfiguration();
    Task<StatusSourceResult<OutlookStatusData>> ReadAsync(StatusPeriod period, CancellationToken token);
}

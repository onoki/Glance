namespace Glance.Server.Integrations.Microsoft;

public interface IMicrosoftTokenProvider
{
    Task<string> GetAzureDevOpsTokenAsync(CancellationToken token);
    Task<string> GetGraphTokenAsync(CancellationToken token);
}

public sealed class WorkplaceIntegrationNotConfiguredException : InvalidOperationException
{
    public WorkplaceIntegrationNotConfiguredException(string message) : base(message)
    {
    }
}

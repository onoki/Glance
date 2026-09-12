using Glance.Server.StatusUpdates;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace Glance.Server.Integrations.Microsoft;

/// <summary>
/// Compilable Entra/MSAL starting point for workplace integration.
/// TODO(WORKPLACE_INTEGRATION): validate tenant policy, redirect URI and scopes at work.
/// This class is inert until StatusUpdates:Microsoft:Enabled is true.
/// </summary>
public sealed class MicrosoftTokenProvider : IMicrosoftTokenProvider
{
    private const string AzureDevOpsResource = "499b84ac-1321-427f-aa17-267ca6975798/.default";
    private const string GraphMailRead = "https://graph.microsoft.com/Mail.Read";
    private readonly StatusIntegrationOptions _options;
    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private IPublicClientApplication? _application;

    public MicrosoftTokenProvider(IOptions<StatusIntegrationOptions> options, AppPaths paths)
    {
        _options = options.Value;
        _paths = paths;
    }

    public Task<string> GetAzureDevOpsTokenAsync(CancellationToken token) =>
        GetTokenAsync(new[] { AzureDevOpsResource }, token);

    public Task<string> GetGraphTokenAsync(CancellationToken token) =>
        GetTokenAsync(new[] { GraphMailRead }, token);

    private async Task<string> GetTokenAsync(string[] scopes, CancellationToken token)
    {
        var application = await GetApplicationAsync();
        var accounts = await application.GetAccountsAsync();
        try
        {
            var silent = await application.AcquireTokenSilent(scopes, accounts.FirstOrDefault()).ExecuteAsync(token);
            return silent.AccessToken;
        }
        catch (MsalUiRequiredException)
        {
            var interactive = await application
                .AcquireTokenInteractive(scopes)
                .WithUseEmbeddedWebView(false)
                .ExecuteAsync(token);
            return interactive.AccessToken;
        }
    }

    private async Task<IPublicClientApplication> GetApplicationAsync()
    {
        if (!_options.Microsoft.Enabled ||
            string.IsNullOrWhiteSpace(_options.Microsoft.ClientId) ||
            string.IsNullOrWhiteSpace(_options.Microsoft.TenantId))
        {
            throw new WorkplaceIntegrationNotConfiguredException(
                "Microsoft Entra integration is disabled or missing tenant/client configuration.");
        }
        if (_application is not null)
        {
            return _application;
        }

        await _initializeLock.WaitAsync();
        try
        {
            if (_application is not null)
            {
                return _application;
            }
            var application = PublicClientApplicationBuilder
                .Create(_options.Microsoft.ClientId)
                .WithAuthority(AzureCloudInstance.AzurePublic, _options.Microsoft.TenantId)
                .WithDefaultRedirectUri()
                .Build();

            var cacheDirectory = Path.Combine(_paths.DataDirectory, "auth");
            Directory.CreateDirectory(cacheDirectory);
            var storage = new StorageCreationPropertiesBuilder("msal.cache", cacheDirectory).Build();
            var cacheHelper = await MsalCacheHelper.CreateAsync(storage);
            cacheHelper.VerifyPersistence();
            cacheHelper.RegisterCache(application.UserTokenCache);
            _application = application;
            return application;
        }
        finally
        {
            _initializeLock.Release();
        }
    }
}

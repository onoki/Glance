using Microsoft.Extensions.Hosting;

namespace Glance.Server;

public sealed class StartupReporter : IHostedService
{
    private readonly ILogger<StartupReporter> _logger;
    private readonly AppMetaRepository _meta;
    private readonly IHostApplicationLifetime _lifetime;

    public StartupReporter(ILogger<StartupReporter> logger, AppMetaRepository meta, IHostApplicationLifetime lifetime)
    {
        _logger = logger;
        _meta = meta;
        _lifetime = lifetime;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var storedVersion = await _meta.GetValueAsync("app_version", cancellationToken);
                var schemaVersion = await _meta.GetSchemaVersionAsync(cancellationToken);
                _logger.LogInformation(
                    "App version {CurrentVersion}, stored app_version {StoredVersion}, schema {SchemaVersion}",
                    BuildInfo.Version,
                    storedVersion ?? "none",
                    schemaVersion);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read startup metadata.");
            }
        }, cancellationToken);

        _lifetime.ApplicationStarted.Register(() =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _meta.SetValueAsync("app_version", BuildInfo.Version, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to store app version in app_meta.");
                }
            });
        });

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

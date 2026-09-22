using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.StaticFiles;
using Glance.Server.Integrations.AzureDevOps;
using Glance.Server.Integrations.Microsoft;
using Glance.Server.Integrations.Outlook;
using Glance.Server.StatusUpdates;
using Glance.Server.Portability;

namespace Glance.Server;

public static class ServerHost
{
    private const int DefaultPort = 5588;

    public static async Task RunAsync(string[] args)
    {
        var appRoot = ResolveAppRoot();
        var port = ResolvePort();

        var app = await StartAsync(appRoot, port, CancellationToken.None);
        await app.WaitForShutdownAsync();
    }

    public static async Task<WebApplication> StartAsync(string appRoot, int port, CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>(),
            ContentRootPath = appRoot
        });

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        builder.Services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });
        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = 1024L * 1024 * 1024;
        });
        builder.Services.Configure<StatusIntegrationOptions>(builder.Configuration.GetSection(StatusIntegrationOptions.SectionName));
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = 1024L * 1024 * 1024;
        });

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
                policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
                    .AllowAnyHeader()
                    .AllowAnyMethod());
        });

        var paths = new AppPaths(appRoot);
        builder.Services.AddSingleton(paths);
        builder.Services.AddSingleton<DatabaseInitializer>();
        builder.Services.AddSingleton<TaskRepository>();
        builder.Services.AddSingleton<PeopleRepository>();
        builder.Services.AddSingleton<IMicrosoftTokenProvider, MicrosoftTokenProvider>();
        builder.Services.AddHttpClient<IAzureDevOpsStatusReader, AzureDevOpsStatusReader>(client => client.Timeout = TimeSpan.FromSeconds(60));
        builder.Services.AddHttpClient<IOutlookStatusReader, OutlookStatusReader>(client => client.Timeout = TimeSpan.FromSeconds(60));
        builder.Services.AddSingleton<StatusSummaryService>();
        builder.Services.AddSingleton<StatusOfficeExporter>();
        builder.Services.AddSingleton<PortableExportService>();
        builder.Services.AddSingleton<ChangeLogRepository>();
        builder.Services.AddSingleton<MaintenanceStateStore>();
        builder.Services.AddSingleton<AttachmentMaintenance>();
        builder.Services.AddSingleton<DatabaseHealthService>();
        builder.Services.AddSingleton<DataSafetySettingsStore>();
        builder.Services.AddSingleton<DataSafetyService>();
        builder.Services.AddSingleton<RestoreCoordinator>();
        builder.Services.AddSingleton<DatabaseStartupCoordinator>();
        var startupState = new DataSafetyStartupState();
        builder.Services.AddSingleton(startupState);
        builder.Services.AddSingleton<MaintenanceService>();
        builder.Services.AddSingleton<AppMetaRepository>();
        builder.Services.AddHostedService<StartupReporter>();
        builder.Services.AddHostedService<BackupSchedulerService>();

        var app = builder.Build();

        app.Urls.Add($"http://127.0.0.1:{port}");

        var logger = app.Logger;
        logger.LogInformation("Starting Glance server at {Url}", $"http://127.0.0.1:{port}");
        logger.LogInformation("Database path: {DatabasePath}", paths.DatabasePath);

        var startupCoordinator = app.Services.GetRequiredService<DatabaseStartupCoordinator>();
        var startupInfo = await startupCoordinator.PrepareAsync(cancellationToken);
        startupState.Set(startupInfo);

        var tasks = app.Services.GetRequiredService<TaskRepository>();
        var maintenance = app.Services.GetRequiredService<MaintenanceService>();
        if (startupInfo.Healthy)
        {
            await tasks.GenerateRecurringTasksAsync(TimeProvider.Now, cancellationToken);
            _ = Task.Run(() => maintenance.RunStartupMaintenanceAsync(TimeProvider.Now, CancellationToken.None));
        }
        else
        {
            logger.LogError("Glance started in recovery mode: {RecoveryMessage}", startupInfo.Message);
        }

        app.UseCors();

        app.Use(async (context, next) =>
        {
            if (startupState.Info.RecoveryMode
                && context.Request.Path.StartsWithSegments("/api")
                && !IsRecoveryEndpoint(context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "RecoveryMode",
                    message = startupState.Info.Message ?? "Glance is in recovery mode. Restore a verified backup before editing notes."
                });
                return;
            }
            await next();
        });

        MapEndpoints(app);

        var distPath = Path.Combine(appRoot, "ui", "dist");
        if (Directory.Exists(distPath))
        {
            var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(distPath);
            app.UseDefaultFiles(new DefaultFilesOptions
            {
                FileProvider = fileProvider
            });
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = fileProvider,
                OnPrepareResponse = context =>
                {
                    var fileName = context.File?.Name;
                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        return;
                    }

                    if (fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                    {
                        var headers = context.Context.Response.Headers;
                        headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
                        headers["Pragma"] = "no-cache";
                        headers["Expires"] = "0";
                    }
                }
            });
        }
        else
        {
            logger.LogInformation("UI dist folder not found at {DistPath}.", distPath);
        }

        await app.StartAsync(cancellationToken);
        return app;
    }

    private static void MapEndpoints(WebApplication app)
    {
        AttachmentEndpoints.Map(app);
        TaskEndpoints.Map(app);
        TaskSendEndpoints.Map(app);
        PeopleEndpoints.Map(app);
        StatusUpdateEndpoints.Map(app);
        ChangesEndpoints.Map(app);
        SearchEndpoints.Map(app);
        MaintenanceEndpoints.Map(app);
        HistoryEndpoints.Map(app);
        HistoryMaintenanceEndpoints.Map(app);
        VersionEndpoints.Map(app);
        DataSafetyEndpoints.Map(app);
        PortableExportEndpoints.Map(app);
    }

    private static bool IsRecoveryEndpoint(PathString path)
    {
        return path.StartsWithSegments("/api/data-safety")
            || path.StartsWithSegments("/api/backups")
            || path.StartsWithSegments("/api/backup")
            || path.StartsWithSegments("/api/warnings")
            || path.StartsWithSegments("/api/maintenance/status")
            || path.StartsWithSegments("/api/version");
    }

    private static string ResolveAppRoot()
    {
        var env = Environment.GetEnvironmentVariable("GLANCE_APP_ROOT");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        return AppRootLocator.Find(AppContext.BaseDirectory);
    }

    private static int ResolvePort()
    {
        var env = Environment.GetEnvironmentVariable("GLANCE_PORT");
        return int.TryParse(env, out var port) ? port : DefaultPort;
    }
}

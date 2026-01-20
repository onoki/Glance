using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.StaticFiles;

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
        builder.Services.AddSingleton<ChangeLogRepository>();
        builder.Services.AddSingleton<MaintenanceStateStore>();
        builder.Services.AddSingleton<AttachmentMaintenance>();
        builder.Services.AddSingleton<MaintenanceService>();
        builder.Services.AddSingleton<AppMetaRepository>();
        builder.Services.AddHostedService<StartupReporter>();

        var app = builder.Build();

        app.Urls.Add($"http://127.0.0.1:{port}");

        var logger = app.Logger;
        logger.LogInformation("Starting Glance server at {Url}", $"http://127.0.0.1:{port}");
        logger.LogInformation("Database path: {DatabasePath}", paths.DatabasePath);

        var initializer = app.Services.GetRequiredService<DatabaseInitializer>();
        initializer.Initialize();

        var tasks = app.Services.GetRequiredService<TaskRepository>();
        var maintenance = app.Services.GetRequiredService<MaintenanceService>();
        await maintenance.RunIntegrityCheckAsync(cancellationToken);
        await tasks.GenerateRecurringTasksAsync(TimeProvider.Now, cancellationToken);
        _ = Task.Run(() => maintenance.RunStartupMaintenanceAsync(TimeProvider.Now, CancellationToken.None));

        app.UseCors();

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
                FileProvider = fileProvider
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
        ChangesEndpoints.Map(app);
        SearchEndpoints.Map(app);
        MaintenanceEndpoints.Map(app);
        HistoryEndpoints.Map(app);
        DebugEndpoints.Map(app);
        VersionEndpoints.Map(app);
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

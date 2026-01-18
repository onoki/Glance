using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;

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

        var app = builder.Build();

        app.Urls.Add($"http://127.0.0.1:{port}");

        var logger = app.Logger;
        logger.LogInformation("Starting Glance server at {Url}", $"http://127.0.0.1:{port}");
        logger.LogInformation("Database path: {DatabasePath}", paths.DatabasePath);

        var initializer = app.Services.GetRequiredService<DatabaseInitializer>();
        initializer.Initialize();

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
        app.MapGet("/api/dashboard", async (TaskRepository tasks, CancellationToken token) =>
        {
            var newTasks = await tasks.GetTasksByPageAsync("dashboard:new", token);
            var mainTasks = await tasks.GetTasksByPageAsync("dashboard:main", token);
            return Results.Ok(new DashboardResponse(newTasks, mainTasks));
        });

        app.MapPost("/api/tasks", async (TaskCreateRequest request, TaskRepository tasks, CancellationToken token) =>
        {
            if (request.Content.ValueKind == JsonValueKind.Undefined)
            {
                return Results.BadRequest(new { error = "ValidationError", message = "Task content is required" });
            }

            var validation = ValidateTaskInput(request.Title, request.Content);
            if (validation != null)
            {
                return validation;
            }

            var response = await tasks.CreateTaskAsync(request, token);
            return Results.Ok(response);
        });

        app.MapPut("/api/tasks/{taskId}", async (string taskId, TaskUpdateRequest request, TaskRepository tasks, CancellationToken token) =>
        {
            var title = request.Title;
            if (title != null)
            {
                var validation = ValidateTaskInput(title, request.Content);
                if (validation != null)
                {
                    return validation;
                }
            }
            else if (request.Content.HasValue && TaskTextExtractor.ContainsHeading(request.Content.Value))
            {
                return Results.BadRequest(new { error = "ValidationError", message = "Task content must not contain heading nodes" });
            }

            var response = await tasks.UpdateTaskAsync(taskId, request, token);
            return response is null
                ? Results.NotFound(new { error = "NotFound", message = "Task not found" })
                : Results.Ok(response);
        });

        app.MapPost("/api/tasks/{taskId}/complete", async (string taskId, TaskCompleteRequest request, TaskRepository tasks, CancellationToken token) =>
        {
            var response = await tasks.SetCompletionAsync(taskId, request.Completed, token);
            return response is null
                ? Results.NotFound(new { error = "NotFound", message = "Task not found" })
                : Results.Ok(response);
        });

        app.MapGet("/api/changes", async (long? since, ChangeLogRepository changes, CancellationToken token) =>
        {
            var response = await changes.GetChangesAsync(since ?? 0, token);
            return Results.Ok(response);
        });

        app.MapGet("/api/search", async (string q, TaskRepository tasks, CancellationToken token) =>
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return Results.Ok(new SearchResponse(q ?? string.Empty, Array.Empty<SearchResult>()));
            }

            var matches = await tasks.SearchAsync(q, token);
            var results = matches.Select(task => new SearchResult(task, new[] { q })).ToList();
            return Results.Ok(new SearchResponse(q, results));
        });
    }

    private static IResult? ValidateTaskInput(string title, JsonElement? content)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Results.BadRequest(new { error = "ValidationError", message = "Task title must be a single line" });
        }

        if (title.Contains('\n') || title.Contains('\r'))
        {
            return Results.BadRequest(new { error = "ValidationError", message = "Task title must be a single line" });
        }

        if (content.HasValue && TaskTextExtractor.ContainsHeading(content.Value))
        {
            return Results.BadRequest(new { error = "ValidationError", message = "Task content must not contain heading nodes" });
        }

        return null;
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

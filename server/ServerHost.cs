using System.Net;
using System.Linq;
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

        var app = builder.Build();

        app.Urls.Add($"http://127.0.0.1:{port}");

        var logger = app.Logger;
        logger.LogInformation("Starting Glance server at {Url}", $"http://127.0.0.1:{port}");
        logger.LogInformation("Database path: {DatabasePath}", paths.DatabasePath);

        var initializer = app.Services.GetRequiredService<DatabaseInitializer>();
        initializer.Initialize();

        var tasks = app.Services.GetRequiredService<TaskRepository>();
        await tasks.GenerateRecurringTasksAsync(DateTime.Now, cancellationToken);

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
        app.MapPost("/api/attachments", async (HttpRequest request, HttpContext context, AppPaths paths) =>
        {
            if (!IsLocalRequest(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "ValidationError", message = "Attachment must be sent as multipart form data" });
            }

            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null)
            {
                return Results.BadRequest(new { error = "ValidationError", message = "No attachment was provided" });
            }

            const long maxBytes = 10 * 1024 * 1024;
            if (file.Length <= 0)
            {
                return Results.BadRequest(new { error = "ValidationError", message = "Attachment is empty" });
            }

            if (file.Length > maxBytes)
            {
                return Results.BadRequest(new { error = "ValidationError", message = "Attachment exceeds the 10MB size limit" });
            }

            var contentType = file.ContentType?.ToLowerInvariant() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(contentType) && !IsAllowedContentType(contentType))
            {
                return Results.BadRequest(new { error = "ValidationError", message = $"Unsupported attachment type: {contentType}" });
            }

            var extension = GetAllowedExtension(file.FileName) ?? GetExtensionFromContentType(contentType) ?? ".png";
            var attachmentId = Guid.NewGuid().ToString();
            var fileName = $"{attachmentId}{extension}";

            Directory.CreateDirectory(paths.AttachmentsDirectory);
            var filePath = Path.Combine(paths.AttachmentsDirectory, fileName);
            await using (var stream = File.Create(filePath))
            {
                await file.CopyToAsync(stream);
            }

            var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
            return Results.Ok(new { attachmentId, url = $"{baseUrl}/attachments/{fileName}" });
        });

        app.MapGet("/attachments/{fileName}", (string fileName, HttpContext context, AppPaths paths) =>
        {
            if (!IsLocalRequest(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var safeName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                return Results.NotFound();
            }

            var filePath = Path.Combine(paths.AttachmentsDirectory, safeName);
            if (!File.Exists(filePath))
            {
                return Results.NotFound();
            }

            var provider = new FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(filePath, out var contentType))
            {
                contentType = "application/octet-stream";
            }

            return Results.File(filePath, contentType);
        });

        app.MapGet("/api/dashboard", async (TaskRepository tasks, CancellationToken token) =>
        {
            var newTasks = await tasks.GetTasksByPageAsync("dashboard:new", token);
            var mainTasks = await tasks.GetDashboardMainTasksAsync(GetStartOfToday(), token);
            return Results.Ok(new DashboardResponse(newTasks, mainTasks));
        });

        app.MapPost("/api/tasks", async (TaskCreateRequest request, TaskRepository tasks, CancellationToken token) =>
        {
            if (request.Title.ValueKind == JsonValueKind.Undefined)
            {
                return Results.BadRequest(new { error = "ValidationError", message = "Task title is required" });
            }

            if (request.Content.ValueKind == JsonValueKind.Undefined)
            {
                return Results.BadRequest(new { error = "ValidationError", message = "Task content is required" });
            }

            var validation = ValidateTaskInput(request.Title, request.Content);
            if (validation != null)
            {
                return validation;
            }

            var scheduleValidation = ValidateScheduledDate(request.ScheduledDate);
            if (scheduleValidation != null)
            {
                return scheduleValidation;
            }

            var recurrenceValidation = ValidateRecurrence(request.Recurrence);
            if (recurrenceValidation != null)
            {
                return recurrenceValidation;
            }

            var response = await tasks.CreateTaskAsync(request, token);
            if (request.Recurrence.HasValue)
            {
                await tasks.GenerateRecurringTasksAsync(DateTime.Now, token);
            }
            return Results.Ok(response);
        });

        app.MapPut("/api/tasks/{taskId}", async (string taskId, TaskUpdateRequest request, TaskRepository tasks, CancellationToken token) =>
        {
            if (request.Title.HasValue)
            {
                var validation = ValidateTaskInput(request.Title.Value, request.Content);
                if (validation != null)
                {
                    return validation;
                }
            }
            else if (request.Content.HasValue && TaskTextExtractor.ContainsHeading(request.Content.Value))
            {
                return Results.BadRequest(new { error = "ValidationError", message = "Task content must not contain heading nodes" });
            }

            var scheduleValidation = ValidateScheduledDate(request.ScheduledDate.HasValue ? request.ScheduledDate.Value : null);
            if (scheduleValidation != null)
            {
                return scheduleValidation;
            }

            var recurrenceValidation = ValidateRecurrence(request.Recurrence);
            if (recurrenceValidation != null)
            {
                return recurrenceValidation;
            }

            var response = await tasks.UpdateTaskAsync(taskId, request, token);
            if (response is null)
            {
                return Results.NotFound(new { error = "NotFound", message = "Task not found" });
            }
            if (request.Recurrence.HasValue)
            {
                await tasks.GenerateRecurringTasksAsync(DateTime.Now, token);
            }
            return Results.Ok(response);
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

        app.MapPost("/api/recurrence/run", async (HttpContext context, TaskRepository tasks, CancellationToken token) =>
        {
            if (!IsLocalRequest(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            await tasks.GenerateRecurringTasksAsync(DateTime.Now, token);
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/history", async (TaskRepository tasks, CancellationToken token) =>
        {
            var completedTasks = await tasks.GetHistoryTasksAsync(token);
            var stats = await tasks.GetHistoryStatsAsync(GetHistoryStart(180), token);

            var groups = completedTasks
                .GroupBy(task => FormatDate(task.CompletedAt))
                .Select(group => new HistoryGroup(group.Key, group.ToList()))
                .ToList();

            return Results.Ok(new HistoryResponse(stats, groups));
        });

        app.MapPost("/api/debug/move-completed-to-history", async (HttpContext context, TaskRepository tasks, CancellationToken token) =>
        {
            if (!IsLocalRequest(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var moved = await tasks.MoveCompletedToHistoryAsync(GetStartOfToday(), token);
            return Results.Ok(new { moved });
        });
    }

    private static IResult? ValidateTaskInput(JsonElement title, JsonElement? content)
    {
        if (title.ValueKind == JsonValueKind.Undefined)
        {
            return Results.BadRequest(new { error = "ValidationError", message = "Task title is required" });
        }

        if (TaskTextExtractor.ContainsHeading(title))
        {
            return Results.BadRequest(new { error = "ValidationError", message = "Task title must not contain heading nodes" });
        }

        if (TaskTextExtractor.ContainsList(title))
        {
            return Results.BadRequest(new { error = "ValidationError", message = "Task title must not contain list nodes" });
        }

        if (content.HasValue && TaskTextExtractor.ContainsHeading(content.Value))
        {
            return Results.BadRequest(new { error = "ValidationError", message = "Task content must not contain heading nodes" });
        }

        return null;
    }

    private static IResult? ValidateScheduledDate(JsonElement? scheduledDate)
    {
        if (!scheduledDate.HasValue)
        {
            return null;
        }

        return scheduledDate.Value.ValueKind switch
        {
            JsonValueKind.String => null,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => Results.BadRequest(new { error = "ValidationError", message = "Scheduled date must be a string or null" })
        };
    }

    private static IResult? ValidateRecurrence(JsonElement? recurrence)
    {
        if (!recurrence.HasValue)
        {
            return null;
        }

        return recurrence.Value.ValueKind switch
        {
            JsonValueKind.Object => null,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => Results.BadRequest(new { error = "ValidationError", message = "Recurrence must be an object or null" })
        };
    }

    private static long GetStartOfToday()
    {
        var now = DateTimeOffset.Now;
        var start = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
        return start.ToUnixTimeMilliseconds();
    }

    private static long GetHistoryStart(int days)
    {
        var now = DateTimeOffset.Now;
        var start = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset).AddDays(-days + 1);
        return start.ToUnixTimeMilliseconds();
    }

    private static string FormatDate(long? timestamp)
    {
        if (!timestamp.HasValue)
        {
            return "Unknown";
        }
        var date = DateTimeOffset.FromUnixTimeMilliseconds(timestamp.Value).ToLocalTime().Date;
        return date.ToString("yyyy-MM-dd");
    }

    private static bool IsLocalRequest(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        return address != null && IPAddress.IsLoopback(address);
    }

    private static bool IsAllowedContentType(string contentType)
    {
        return contentType is "image/png"
            or "image/jpeg"
            or "image/webp"
            or "image/gif";
    }

    private static string? GetAllowedExtension(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".png" => extension,
            ".jpg" => extension,
            ".jpeg" => extension,
            ".webp" => extension,
            ".gif" => extension,
            _ => null
        };
    }

    private static string? GetExtensionFromContentType(string contentType)
    {
        return contentType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            _ => null
        };
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

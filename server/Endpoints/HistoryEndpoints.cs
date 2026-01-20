using System.Linq;

namespace Glance.Server;

internal static class HistoryEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapGet("/api/history", async (TaskRepository tasks, CancellationToken token) =>
        {
            var completedTasks = await tasks.GetHistoryTasksAsync(token);
            var stats = await tasks.GetHistoryStatsAsync(EndpointHelpers.GetHistoryStart(180), token);

            var groups = completedTasks
                .GroupBy(task => EndpointHelpers.FormatDate(task.CompletedAt))
                .Select(group => new HistoryGroup(group.Key, group.ToList()))
                .ToList();

            return Results.Ok(new HistoryResponse(stats, groups));
        });
    }
}

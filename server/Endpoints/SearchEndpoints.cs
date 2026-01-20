using System.Linq;

namespace Glance.Server;

internal static class SearchEndpoints
{
    internal static void Map(WebApplication app)
    {
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
}

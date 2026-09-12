namespace Glance.Server;

internal static class PeopleEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapGet("/api/people", async (bool? includeArchived, PeopleRepository people, CancellationToken token) =>
            Results.Ok(await people.GetAsync(includeArchived == true, token)));

        app.MapPost("/api/people", async (PersonCreateRequest request, PeopleRepository people, CancellationToken token) =>
        {
            try
            {
                return Results.Ok(await people.CreatePersonAsync(request.DisplayName, token));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = "ValidationError", message = ex.Message });
            }
        });

        app.MapPut("/api/people/{personId}", async (string personId, PersonUpdateRequest request, PeopleRepository people, CancellationToken token) =>
        {
            try
            {
                return await people.UpdatePersonAsync(personId, request, token)
                    ? Results.Ok(new { ok = true })
                    : Results.NotFound(new { error = "NotFound", message = "Person not found" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = "ValidationError", message = ex.Message });
            }
        });

        app.MapPut("/api/people/{personId}/tags", async (string personId, PersonTagsRequest request, PeopleRepository people, CancellationToken token) =>
        {
            await people.SetPersonTagsAsync(personId, request.TagIds ?? Array.Empty<string>(), token);
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/people/{personId}/tasks", async (string personId, TaskRepository tasks, CancellationToken token) =>
            Results.Ok(new { tasks = await tasks.GetPersonTasksAsync(personId, EndpointHelpers.GetStartOfToday(), token) }));

        app.MapPost("/api/people/tags", async (PersonTagCreateRequest request, PeopleRepository people, CancellationToken token) =>
        {
            try
            {
                return Results.Ok(await people.CreateTagAsync(request.Name, token));
            }
            catch (Exception ex) when (ex is ArgumentException or Microsoft.Data.Sqlite.SqliteException)
            {
                return Results.BadRequest(new { error = "ValidationError", message = "Tag names must be non-empty and unique" });
            }
        });

        app.MapPut("/api/people/tags/{tagId}", async (string tagId, PersonTagUpdateRequest request, PeopleRepository people, CancellationToken token) =>
        {
            try
            {
                return await people.UpdateTagAsync(tagId, request, token)
                    ? Results.Ok(new { ok = true })
                    : Results.NotFound(new { error = "NotFound", message = "Tag not found" });
            }
            catch (Exception ex) when (ex is ArgumentException or Microsoft.Data.Sqlite.SqliteException)
            {
                return Results.BadRequest(new { error = "ValidationError", message = "Tag names must be non-empty and unique" });
            }
        });

        app.MapDelete("/api/people/tags/{tagId}", async (string tagId, PeopleRepository people, CancellationToken token) =>
        {
            await people.DeleteTagAsync(tagId, token);
            return Results.Ok(new { ok = true });
        });
    }
}

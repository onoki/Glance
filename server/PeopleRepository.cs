using Microsoft.Data.Sqlite;

namespace Glance.Server;

public sealed class PeopleRepository
{
    private readonly AppPaths _paths;

    public PeopleRepository(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<PeopleResponse> GetAsync(bool includeArchived, CancellationToken token)
    {
        var people = new List<PersonItem>();
        var tags = new List<PersonTagItem>();
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(token);

        var memberships = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        await using (var membershipCommand = connection.CreateCommand())
        {
            membershipCommand.CommandText = "SELECT person_id, tag_id FROM person_tag_members;";
            await using var reader = await membershipCommand.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var personId = reader.GetString(0);
                if (!memberships.TryGetValue(personId, out var values))
                {
                    values = new List<string>();
                    memberships[personId] = values;
                }
                values.Add(reader.GetString(1));
            }
        }

        await using (var peopleCommand = connection.CreateCommand())
        {
            peopleCommand.CommandText = includeArchived
                ? "SELECT id, display_name, position, archived_at FROM people ORDER BY archived_at IS NOT NULL, position, display_name;"
                : "SELECT id, display_name, position, archived_at FROM people WHERE archived_at IS NULL ORDER BY position, display_name;";
            await using var reader = await peopleCommand.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var id = reader.GetString(0);
                people.Add(new PersonItem(
                    id,
                    reader.GetString(1),
                    reader.GetDouble(2),
                    reader.IsDBNull(3) ? null : reader.GetInt64(3),
                    memberships.TryGetValue(id, out var values) ? values : Array.Empty<string>()));
            }
        }

        await using (var tagCommand = connection.CreateCommand())
        {
            tagCommand.CommandText = "SELECT id, name, position FROM person_tags ORDER BY position, name;";
            await using var reader = await tagCommand.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                tags.Add(new PersonTagItem(reader.GetString(0), reader.GetString(1), reader.GetDouble(2)));
            }
        }
        return new PeopleResponse(people, tags);
    }

    public async Task<PersonItem> CreatePersonAsync(string displayName, CancellationToken token)
    {
        var name = NormalizeName(displayName, "Person name is required");
        var id = Guid.NewGuid().ToString("D");
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO people(id, display_name, position, created_at, updated_at)
            VALUES($id, $name, COALESCE((SELECT MAX(position) + 1 FROM people), 1), $now, $now);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT position FROM people WHERE id = $id;";
        command.Parameters.Clear();
        command.Parameters.AddWithValue("$id", id);
        var position = Convert.ToDouble(await command.ExecuteScalarAsync(token));
        return new PersonItem(id, name, position, null, Array.Empty<string>());
    }

    public async Task<bool> UpdatePersonAsync(string id, PersonUpdateRequest request, CancellationToken token)
    {
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        var assignments = new List<string> { "updated_at = $now" };
        if (request.DisplayName is not null)
        {
            assignments.Add("display_name = $name");
            command.Parameters.AddWithValue("$name", NormalizeName(request.DisplayName, "Person name is required"));
        }
        if (request.Position.HasValue)
        {
            assignments.Add("position = $position");
            command.Parameters.AddWithValue("$position", request.Position.Value);
        }
        if (request.Archived.HasValue)
        {
            assignments.Add("archived_at = $archivedAt");
            command.Parameters.AddWithValue("$archivedAt", request.Archived.Value ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : DBNull.Value);
        }
        command.CommandText = $"UPDATE people SET {string.Join(", ", assignments)} WHERE id = $id;";
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteNonQueryAsync(token) > 0;
    }

    public async Task SetPersonTagsAsync(string personId, IReadOnlyList<string> tagIds, CancellationToken token)
    {
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(token);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM person_tag_members WHERE person_id = $personId;";
            delete.Parameters.AddWithValue("$personId", personId);
            await delete.ExecuteNonQueryAsync(token);
        }
        foreach (var tagId in tagIds.Distinct(StringComparer.Ordinal))
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR IGNORE INTO person_tag_members(person_id, tag_id) VALUES($personId, $tagId);";
            insert.Parameters.AddWithValue("$personId", personId);
            insert.Parameters.AddWithValue("$tagId", tagId);
            await insert.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
    }

    public async Task<PersonTagItem> CreateTagAsync(string nameValue, CancellationToken token)
    {
        var name = NormalizeName(nameValue, "Tag name is required");
        var id = Guid.NewGuid().ToString("D");
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO person_tags(id, name, position, created_at, updated_at)
            VALUES($id, $name, COALESCE((SELECT MAX(position) + 1 FROM person_tags), 1), $now, $now);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT position FROM person_tags WHERE id = $id;";
        command.Parameters.Clear();
        command.Parameters.AddWithValue("$id", id);
        var position = Convert.ToDouble(await command.ExecuteScalarAsync(token));
        return new PersonTagItem(id, name, position);
    }

    public async Task<bool> UpdateTagAsync(string id, PersonTagUpdateRequest request, CancellationToken token)
    {
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        var assignments = new List<string> { "updated_at = $now" };
        if (request.Name is not null)
        {
            assignments.Add("name = $name");
            command.Parameters.AddWithValue("$name", NormalizeName(request.Name, "Tag name is required"));
        }
        if (request.Position.HasValue)
        {
            assignments.Add("position = $position");
            command.Parameters.AddWithValue("$position", request.Position.Value);
        }
        command.CommandText = $"UPDATE person_tags SET {string.Join(", ", assignments)} WHERE id = $id;";
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteNonQueryAsync(token) > 0;
    }

    public async Task DeleteTagAsync(string id, CancellationToken token)
    {
        await using var connection = new SqliteConnection(_paths.ConnectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM person_tags WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(token);
    }

    private static string NormalizeName(string value, string error)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException(error);
        }
        return normalized.Length <= 200 ? normalized : normalized[..200];
    }
}

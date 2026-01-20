using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Glance.Server;

public sealed class DatabaseInitializer
{
    private readonly AppPaths _paths;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(AppPaths paths, ILogger<DatabaseInitializer> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public void Initialize()
    {
        Directory.CreateDirectory(_paths.DataDirectory);

        if (!File.Exists(_paths.DatabasePath))
        {
            _logger.LogInformation("Creating database at {DatabasePath}", _paths.DatabasePath);
            ExecuteScript(_paths.SchemaPath);
        }

        using var connection = new SqliteConnection(_paths.ConnectionString);
        connection.Open();

        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode = WAL;";
            pragma.ExecuteNonQuery();
        }

        EnsureSchemaMigrationsTable(connection);
        EnsureAppMetaTable(connection);

        var appliedVersions = GetAppliedVersions(connection);
        var migrationFiles = GetMigrationFiles();

        foreach (var (version, path) in migrationFiles)
        {
            if (appliedVersions.Contains(version))
            {
                continue;
            }

            _logger.LogInformation("Applying migration {Version} from {Path}", version, path);
            ApplyMigration(connection, version, path);
        }
    }

    private void ExecuteScript(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Missing schema file: {path}");
        }

        using var connection = new SqliteConnection(_paths.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = File.ReadAllText(path);
        command.ExecuteNonQuery();
    }

    private static void EnsureSchemaMigrationsTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
              version INTEGER PRIMARY KEY,
              applied_at INTEGER NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static void EnsureAppMetaTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS app_meta (
              key TEXT PRIMARY KEY,
              value TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static HashSet<int> GetAppliedVersions(SqliteConnection connection)
    {
        var versions = new HashSet<int>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_migrations;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            versions.Add(reader.GetInt32(0));
        }

        return versions;
    }

    private List<(int Version, string Path)> GetMigrationFiles()
    {
        if (!Directory.Exists(_paths.MigrationsDirectory))
        {
            return new List<(int, string)>();
        }

        return Directory.GetFiles(_paths.MigrationsDirectory, "*.sql")
            .Select(path => (Version: ParseVersion(path), Path: path))
            .Where(entry => entry.Version > 0)
            .OrderBy(entry => entry.Version)
            .ToList();
    }

    private static int ParseVersion(string path)
    {
        var name = Path.GetFileName(path);
        var digits = new string(name.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            ? version
            : 0;
    }

    private void ApplyMigration(SqliteConnection connection, int version, string path)
    {
        var sql = File.ReadAllText(path);
        using var transaction = connection.BeginTransaction();

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        using (var record = connection.CreateCommand())
        {
            record.Transaction = transaction;
            record.CommandText = """
                INSERT OR IGNORE INTO schema_migrations(version, applied_at)
                VALUES ($version, $appliedAt);
                """;
            record.Parameters.AddWithValue("$version", version);
            record.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            record.ExecuteNonQuery();
        }

        transaction.Commit();
    }
}

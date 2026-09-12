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

        var currentVersion = GetCurrentVersion(connection);
        var migrationFiles = GetMigrationFiles();

        foreach (var (version, path) in migrationFiles)
        {
            // Schema versions are monotonic. A fresh baseline records the latest
            // version, so replaying every older migration would attempt duplicate
            // ALTER TABLE statements.
            if (version <= currentVersion)
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

    internal int GetSupportedSchemaVersion()
    {
        return GetMigrationFiles().Select(entry => entry.Version).DefaultIfEmpty(0).Max();
    }

    private static int GetCurrentVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
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

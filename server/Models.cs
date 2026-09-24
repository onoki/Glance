using System.Text.Json;

namespace Glance.Server;

public sealed record TaskItem(
    string Id,
    string Page,
    JsonElement Title,
    JsonElement Content,
    double Position,
    long CreatedAt,
    long UpdatedAt,
    long? CompletedAt,
    string? ScheduledDate,
    JsonElement? Recurrence,
    string? OwnerPersonId,
    string? OwnerPersonName,
    long? StatusInputAt,
    string? OriginLabel,
    bool SendMarkerVisible
);

public sealed record TaskCreateRequest(
    string Page,
    JsonElement Title,
    JsonElement Content,
    double Position,
    JsonElement? ScheduledDate,
    JsonElement? Recurrence,
    string? OwnerPersonId = null,
    string? OriginLabel = null
);

public sealed record TaskCreateResponse(
    string TaskId,
    long UpdatedAt
);

public sealed record TaskUpdateRequest(
    long BaseUpdatedAt,
    JsonElement? Title,
    JsonElement? Content,
    string? Page,
    double? Position,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(ExplicitJsonNullConverter))] JsonElement? ScheduledDate,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(ExplicitJsonNullConverter))] JsonElement? Recurrence
);

public sealed record TaskUpdateResponse(
    long UpdatedAt,
    bool ExternalUpdate
);

public sealed class TaskWriteConflictException : Exception
{
    public TaskWriteConflictException(string taskId, long currentUpdatedAt)
        : base("This note changed in another Glance window. Your local text was not overwritten.")
    {
        TaskId = taskId;
        CurrentUpdatedAt = currentUpdatedAt;
    }

    public string TaskId { get; }
    public long CurrentUpdatedAt { get; }
}

public sealed record TaskCompleteRequest(
    bool Completed
);

public sealed record TaskCompleteResponse(
    long? CompletedAt
);

public sealed record TaskRestoreResponse(
    long UpdatedAt
);

public sealed record TaskStatusMarkersRequest(
    long BaseUpdatedAt,
    JsonElement Title,
    JsonElement Content
);

public sealed record TaskStatusMarkersResponse(
    long UpdatedAt,
    long? StatusInputAt
);

public sealed record DashboardResponse(
    IReadOnlyList<TaskItem> NewTasks,
    IReadOnlyList<TaskItem> MainTasks
);

public sealed record ChangeItem(
    string EntityType,
    string EntityId,
    string ChangeType,
    long ChangedAt
);

public sealed record ChangesResponse(
    long LastId,
    IReadOnlyList<ChangeItem> Changes
);

public sealed record SearchResult(
    TaskItem Task,
    IReadOnlyList<string> Matches
);

public sealed record SearchResponse(
    string Query,
    IReadOnlyList<SearchResult> Results
);

public sealed record WarningItem(
    string Id,
    string Message
);

public sealed record WarningsResponse(
    IReadOnlyList<WarningItem> Warnings
);

public sealed record MaintenanceStatus(
    string? LastBackupAt,
    string? LastBackupError,
    string? LastReindexAt,
    string? MirrorBackupError,
    string? BackupVerificationError,
    bool RecoveryMode
);

public sealed record HistoryDayStat(
    string Date,
    int Count
);

public sealed record HistoryGroup(
    string Date,
    IReadOnlyList<TaskItem> Tasks
);

public sealed record HistoryResponse(
    IReadOnlyList<HistoryDayStat> Stats,
    IReadOnlyList<HistoryGroup> Groups
);

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
    JsonElement? Recurrence
);

public sealed record TaskCreateRequest(
    string Page,
    JsonElement Title,
    JsonElement Content,
    double Position,
    JsonElement? ScheduledDate,
    JsonElement? Recurrence
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
    JsonElement? ScheduledDate,
    JsonElement? Recurrence
);

public sealed record TaskUpdateResponse(
    long UpdatedAt,
    bool ExternalUpdate
);

public sealed record TaskCompleteRequest(
    bool Completed
);

public sealed record TaskCompleteResponse(
    long? CompletedAt
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

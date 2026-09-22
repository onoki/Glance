namespace Glance.Server;

public sealed record PersonTagItem(string Id, string Name, double Position, string? Color = null);

public sealed record PersonItem(
    string Id,
    string DisplayName,
    double Position,
    long? ArchivedAt,
    IReadOnlyList<string> TagIds
);

public sealed record PeopleResponse(
    IReadOnlyList<PersonItem> People,
    IReadOnlyList<PersonTagItem> Tags
);

public sealed record PersonCreateRequest(string DisplayName);
public sealed record PersonUpdateRequest(string? DisplayName, double? Position, bool? Archived);
public sealed record PersonTagsRequest(IReadOnlyList<string> TagIds);
public sealed record PersonTagCreateRequest(string Name);
public sealed record PersonTagUpdateRequest(string? Name, double? Position, string? Color = null);

public sealed record TaskSendToPeopleRequest(
    IReadOnlyList<string>? PersonIds,
    IReadOnlyList<string>? TagIds
);

public sealed record TaskSendEventItem(
    string Id,
    string DestinationKind,
    string? DestinationId,
    string DestinationLabel,
    string Direction,
    long SentAt
);

public sealed record TaskSendResponse(int Created, IReadOnlyList<string> TaskIds);

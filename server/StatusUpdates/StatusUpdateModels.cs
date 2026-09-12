using System.Text.Json.Nodes;

namespace Glance.Server.StatusUpdates;

public sealed record StatusCollectRequest(
    string? ProjectName,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ContextFromUtc
);

public sealed record StatusPeriod(DateTimeOffset FromUtc, DateTimeOffset ToUtc, DateTimeOffset ContextFromUtc);

public sealed record StatusSourceResult<T>(
    string CollectionStatus,
    DateTimeOffset? CollectedAtUtc,
    string? Error,
    T Data
);

public sealed record AzureWorkItemInput(
    int Id,
    string Url,
    string Type,
    string Title,
    string State,
    string? AssignedTo,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? ChangedAtUtc,
    bool IsNewSincePreviousReport,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, object?> AdditionalFields
);

public sealed record AzureStatusData(
    string? Organization,
    string? Project,
    string? QueryId,
    IReadOnlyList<AzureWorkItemInput> WorkItems
);

public sealed record OutlookFolderInput(string Id, string DisplayName);

public sealed record OutlookMessageInput(
    string Id,
    string? ConversationId,
    string Subject,
    string? From,
    DateTimeOffset ReceivedAtUtc,
    string BodyText,
    string? WebLink,
    bool IsNewSincePreviousReport,
    bool WasTruncated
);

public sealed record OutlookStatusData(
    OutlookFolderInput? Folder,
    IReadOnlyList<OutlookMessageInput> Messages
);

public sealed record StatusRunInfo(
    string ReportDate,
    string ReportId,
    int InputRevision,
    string DocumentStatus,
    long CreatedAt,
    long UpdatedAt,
    long? CompletedAt,
    string InputSha256,
    string RelativeJsonPath
);

public sealed record StatusOverviewResponse(
    StatusRunInfo? Latest,
    bool AzureDevOpsConfigured,
    bool OutlookConfigured,
    string AzureDevOpsMessage,
    string OutlookMessage,
    StatusPreview? Preview
);

public sealed record StatusPreview(
    string ProjectName,
    StatusPeriod Period,
    int GlanceItemCount,
    int AzureWorkItemCount,
    int OutlookMessageCount,
    StatusOutput Output
);

public sealed record StatusImportResponse(StatusRunInfo Run, IReadOnlyList<string> ValidationErrors);

public sealed record StatusRisk(
    string Id,
    string Title,
    string Description,
    bool IsNew,
    IReadOnlyList<StatusSourceReference> SourceRefs
);

public sealed record StatusQuestion(
    string Id,
    string Question,
    bool IsNew,
    IReadOnlyList<StatusSourceReference> SourceRefs
);

public sealed record StatusSourceReference(string Source, string Id);

public sealed record StatusOutput(
    DateTimeOffset? CompletedAtUtc,
    string ProjectStatus,
    IReadOnlyList<StatusRisk> Risks,
    IReadOnlyList<StatusQuestion> OpenQuestions
);

public sealed record StatusExportDocument(
    string ReportDate,
    string ProjectName,
    StatusPeriod Period,
    StatusOutput Output
);

public sealed record StatusValidationResult(bool IsValid, IReadOnlyList<string> Errors);

public sealed record StatusProviderConfiguration(bool IsConfigured, string Message);

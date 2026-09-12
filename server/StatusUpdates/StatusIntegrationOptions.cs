namespace Glance.Server.StatusUpdates;

public sealed class StatusIntegrationOptions
{
    public const string SectionName = "StatusUpdates";

    public string ProjectName { get; set; } = "Project";
    public MicrosoftOptions Microsoft { get; set; } = new();
    public AzureDevOpsOptions AzureDevOps { get; set; } = new();
    public OutlookOptions Outlook { get; set; } = new();
}

public sealed class MicrosoftOptions
{
    public bool Enabled { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
}

public sealed class AzureDevOpsOptions
{
    public bool Enabled { get; set; }
    public string Organization { get; set; } = string.Empty;
    public string Project { get; set; } = string.Empty;
    public string SavedQueryId { get; set; } = string.Empty;
    public List<string> AdditionalFields { get; set; } = new();
}

public sealed class OutlookOptions
{
    public bool Enabled { get; set; }
    public string FolderId { get; set; } = string.Empty;
    public string FolderDisplayName { get; set; } = string.Empty;
    public int MaximumMessages { get; set; } = 500;
    public int MaximumBodyCharacters { get; set; } = 20000;
}

namespace ApiInsightStudio.Api.Audit;

/// <summary>Denetim izine yazılan eylem kodları.</summary>
public static class AuditActions
{
    public const string ProjectUploaded = "project.uploaded";
    public const string AnalysisCompleted = "analysis.completed";
    public const string SuggestionCreated = "ai.suggestion.created";
    public const string SuggestionApproved = "ai.suggestion.approved";
    public const string SuggestionRejected = "ai.suggestion.rejected";
    public const string SuggestionSuperseded = "ai.suggestion.superseded";
    public const string SettingsUpdated = "settings.updated";
    public const string WebhookSecretRotated = "webhook.secret.rotated";
    public const string TargetTokenSet = "target.token.set";
    public const string TargetTokenCleared = "target.token.cleared";
    public const string TestRunStarted = "testrun.started";
    public const string TestRunFinished = "testrun.finished";
}

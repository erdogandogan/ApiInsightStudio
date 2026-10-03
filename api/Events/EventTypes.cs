namespace ApiInsightStudio.Api.Events;

/// <summary>Bir proje için analiz tamamlandı.</summary>
public sealed record AnalysisCompleted(int ProjectId, DateTime OccurredAt);

/// <summary>Bir uyarı kuralı tuttu ve yeni bir uyarı açıldı (Aşama 2'de dışarı bildirilecek).</summary>
public sealed record AlertRaised(
    int ProjectId, string RuleCode, string Message, string Severity, DateTime RaisedAt, Guid EventId);

/// <summary>Bir projenin test koşusu tamamlandı (sonuçlar veritabanında; olay yalnızca özet taşır).</summary>
public sealed record TestRunCompleted(int ProjectId, int RunId, int Passed, int Failed, int Skipped, DateTime OccurredAt);

/// <summary>Outbox'taki tür adını CLR olay türüne eşleyen kayıt.</summary>
public static class EventTypes
{
    public static readonly IReadOnlyDictionary<string, Type> All = new[]
    {
        typeof(AnalysisCompleted),
        typeof(AlertRaised),
        typeof(TestRunCompleted)
    }.ToDictionary(type => type.Name);
}

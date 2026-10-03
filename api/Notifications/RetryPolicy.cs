namespace ApiInsightStudio.Api.Notifications;

/// <summary>
/// Başarısız işler için üssel geri çekilme. Deneme sayısı ve bir sonraki deneme zamanı veritabanında tutulur,
/// bu yüzden uygulama yeniden başlasa bile bekleyen işler kaybolmaz (bellek içi bir kütüphane bunu yapamaz).
/// </summary>
public static class RetryPolicy
{
    /// <summary>Bu kadar başarısız denemeden sonra iş "ölü" sayılır ve bir daha denenmez.</summary>
    public const int MaxAttempts = 5;

    public static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Verilen sayıda başarısız denemeden sonra ne kadar beklenmeli: 1 → 10 sn, 2 → 20 sn, 3 → 40 sn, 4 → 80 sn.
    /// Hak bittiyse null döner (iş ölü olur).
    /// </summary>
    public static TimeSpan? NextDelay(int failedAttempts)
    {
        if (failedAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(failedAttempts), "En az bir başarısız deneme olmalı.");

        if (failedAttempts >= MaxAttempts)
            return null;

        return TimeSpan.FromTicks(BaseDelay.Ticks * (1L << (failedAttempts - 1)));
    }
}

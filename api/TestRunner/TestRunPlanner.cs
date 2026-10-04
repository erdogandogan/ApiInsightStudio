namespace ApiInsightStudio.Api.TestRunner;

/// <summary>Planlayıcının girdisi: üretilmiş bir test senaryosu ve ilgili uç noktanın kimlik bilgisi.</summary>
public sealed record ScenarioInput(
    int EndpointId, string Title, string Method, string Path, int ExpectedStatusCode, string? AuthType);

/// <summary>Gerçekten atılacak istek.</summary>
/// <param name="RequestPath">Yol parametreleri doldurulmuş yol (ana bilgisayar yok).</param>
/// <param name="SendToken">Hedef Bearer token'ı gönderilsin mi? (401 senaryosunda asla.)</param>
/// <param name="SendBody">Boş bir JSON gövde ("{}") gönderilsin mi?</param>
public sealed record PlannedCall(string Method, string RequestPath, bool SendToken, bool SendBody);

/// <summary>Bir senaryonun planı: ya çalıştırılacak bir istek ya da atlama nedeni.</summary>
public sealed record PlanItem(ScenarioInput Scenario, PlannedCall? Call, string? SkipReason)
{
    public bool IsRunnable => Call is not null;
}

/// <summary>
/// Hangi senaryoların güvenle ve anlamlı biçimde çalıştırılabileceğini belirler. Üretilmiş senaryolar şablondur
/// (her uç noktaya "200 bekle", "401 bekle" gibi); körlemesine çalıştırmak yanlış sonuç verir. Bu sınıf, bilinmeyen
/// bilgiye (geçerli id, geçerli gövde, kimlik bilgisi) dayanan senaryoları nedeniyle birlikte ATLAR.
/// Veritabanına ve ağa dokunmaz.
/// </summary>
public static class TestRunPlanner
{
    /// <summary>Bir koşuda atılacak en fazla istek sayısı; fazlası atlanır.</summary>
    public const int MaxRequests = 200;

    private const int MaxPathLength = 2000;

    private static readonly HashSet<string> MutatingMethods = new(StringComparer.Ordinal)
    {
        "POST", "PUT", "PATCH", "DELETE"
    };

    private static readonly HashSet<string> SafeMethods = new(StringComparer.Ordinal) { "GET", "HEAD" };

    private static readonly HashSet<string> BodyMethods = new(StringComparer.Ordinal) { "POST", "PUT", "PATCH" };

    public static bool IsMutating(string method) => MutatingMethods.Contains(method.ToUpperInvariant());

    /// <param name="allowMutating">Projede allowMutatingTests açık mı?</param>
    /// <param name="hasToken">Hedef için Bearer token tanımlı mı?</param>
    /// <param name="fillPathParameters">(metot, yol şablonu) → parametreleri doldurulmuş yol.</param>
    public static IReadOnlyList<PlanItem> Plan(
        IReadOnlyList<ScenarioInput> scenarios,
        bool allowMutating,
        bool hasToken,
        Func<string, string, string> fillPathParameters)
    {
        var items = scenarios
            .Select(scenario => PlanOne(scenario, allowMutating, hasToken, fillPathParameters))
            .ToList();

        // İstek sınırı: sınırı aşan çalıştırılabilir senaryolar atlanır (sıra korunur).
        var runnable = 0;
        for (var i = 0; i < items.Count; i++)
        {
            if (!items[i].IsRunnable)
                continue;

            runnable++;
            if (runnable > MaxRequests)
                items[i] = items[i] with { Call = null, SkipReason = $"Koşu istek sınırı ({MaxRequests}) aşıldı." };
        }

        return items;
    }

    private static PlanItem PlanOne(
        ScenarioInput scenario, bool allowMutating, bool hasToken, Func<string, string, string> fill)
    {
        var method = (scenario.Method ?? string.Empty).Trim().ToUpperInvariant();
        var mutating = MutatingMethods.Contains(method);

        if (!mutating && !SafeMethods.Contains(method))
            return Skip(scenario, $"Desteklenmeyen metot ({(method.Length == 0 ? "boş" : method)}).");

        if (!IsValidPathTemplate(scenario.Path))
            return Skip(scenario, "Geçersiz yol (yalnızca / ile başlayan, sorgu ve boşluk içermeyen yollar çalıştırılır).");

        if (mutating && !allowMutating)
            return Skip(scenario, "Mutating metot; projede allowMutatingTests kapalı.");

        var hasParameters = scenario.Path.Contains('{');
        var requiresAuth = !string.IsNullOrWhiteSpace(scenario.AuthType);
        var bodyNeeded = BodyMethods.Contains(method);

        switch (scenario.ExpectedStatusCode)
        {
            case 200:
                if (mutating)
                    return Skip(scenario, "Mutating metotlarda başarılı senaryo çalıştırılmaz (geçerli gövde bilinmiyor).");
                if (hasParameters)
                    return Skip(scenario, "Yol parametresi için geçerli bir değer bilinmiyor.");
                if (requiresAuth && !hasToken)
                    return Skip(scenario, "Uç nokta kimlik gerektiriyor ama hedef için token tanımlı değil.");
                return Run(scenario, method, scenario.Path, sendToken: requiresAuth, sendBody: false);

            case 401:
                if (!requiresAuth)
                    return Skip(scenario, "Herkese açık uç nokta; kimlik reddi beklenemez.");
                return RunFilled(scenario, method, fill, sendToken: false, sendBody: bodyNeeded);

            case 404:
                if (!hasParameters)
                    return Skip(scenario, "Yol parametresi yok; \"bulunamadı\" senaryosu anlamsız.");
                if (requiresAuth && !hasToken)
                    return Skip(scenario, "Uç nokta kimlik gerektiriyor ama hedef için token tanımlı değil.");
                return RunFilled(scenario, method, fill, sendToken: requiresAuth, sendBody: bodyNeeded);

            case 400:
                if (!bodyNeeded)
                    return Skip(scenario, "Hatalı gövde senaryosu yalnızca POST/PUT/PATCH için çalıştırılır.");
                if (hasParameters)
                    return Skip(scenario, "Yol parametreli uç noktada hatalı gövde senaryosu güvenilir değil (önce 404 dönebilir).");
                if (requiresAuth && !hasToken)
                    return Skip(scenario, "Uç nokta kimlik gerektiriyor ama hedef için token tanımlı değil.");
                return Run(scenario, method, scenario.Path, sendToken: requiresAuth, sendBody: true);

            default:
                return Skip(scenario, $"Bilinmeyen senaryo türü (beklenen {scenario.ExpectedStatusCode}).");
        }
    }

    private static PlanItem RunFilled(
        ScenarioInput scenario, string method, Func<string, string, string> fill, bool sendToken, bool sendBody)
    {
        var filled = fill(method, scenario.Path);

        // Doldurma sonrası şablon kalıntısı (örn. bozuk "{") veya geçersiz karakter kalmamalı
        if (filled.Contains('{') || filled.Contains('}') || !IsValidResolvedPath(filled))
            return Skip(scenario, "Yol parametreleri doldurulamadı.");

        return Run(scenario, method, filled, sendToken, sendBody);
    }

    private static PlanItem Run(ScenarioInput scenario, string method, string path, bool sendToken, bool sendBody) =>
        new(scenario, new PlannedCall(method, path, sendToken, sendBody), null);

    private static PlanItem Skip(ScenarioInput scenario, string reason) => new(scenario, null, reason);

    /// <summary>OpenAPI yol şablonu: / ile başlar, sorgu/parça/ters eğik çizgi/boşluk/denetim karakteri içermez.</summary>
    public static bool IsValidPathTemplate(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > MaxPathLength || path[0] != '/')
            return false;

        // "//" ile başlayan yol, bazı istemcilerde "ağ yolu" gibi yorumlanabilir; reddedilir
        if (path.Length > 1 && path[1] == '/')
            return false;

        return path.All(c => c != '?' && c != '#' && c != '\\' && !char.IsWhiteSpace(c) && !char.IsControl(c));
    }

    private static bool IsValidResolvedPath(string path) =>
        IsValidPathTemplate(path) && !path.Contains("..", StringComparison.Ordinal);

    /// <summary>
    /// Beklenen durum koduna göre gelen kod kabul edilebilir mi? 200 → herhangi 2xx; 401 → 401/403;
    /// 400 → 400/422; 404 → yalnızca 404; diğerleri birebir.
    /// </summary>
    public static bool IsAccepted(int expected, int actual) => expected switch
    {
        200 => actual is >= 200 and < 300,
        401 => actual is 401 or 403,
        400 => actual is 400 or 422,
        404 => actual == 404,
        _ => actual == expected
    };

    /// <summary>Beklentinin insan okur açıklaması (hata nedeni metni için).</summary>
    public static string DescribeExpectation(int expected) => expected switch
    {
        200 => "2xx",
        401 => "401 veya 403",
        400 => "400 veya 422",
        _ => expected.ToString()
    };
}

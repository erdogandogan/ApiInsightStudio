using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ApiInsightStudio.Api.Services;
using Xunit.Abstractions;

namespace ApiInsightStudio.Api.Tests;

/// <summary>
/// Kural motoru değerlendirmesi: <c>eval/rules/cases</c> altındaki OpenAPI örneklerini gerçek yükleme + analiz hattından
/// (HTTP, ayrıştırma, kurallar) geçirir. Her örnekte iki ölçüt vardır:
/// <list type="bullet">
/// <item><b>expect</b>: kural tanımına göre elle hesaplanmış skor ve bulgular (regresyon: motor tanımına uyuyor mu?).</item>
/// <item><b>ideal</b> (isteğe bağlı): gerçek dünyada olması gereken sonuç; farklıysa kuralın bilinen yanlış pozitif/negatifidir.</item>
/// </list>
/// Rapor üretmek için: <c>EVAL_WRITE_REPORT=1 dotnet test --filter Category=Eval</c> → <c>eval/results/rule-engine.md</c>.
/// </summary>
[Trait("Category", "Eval")]
public sealed class RuleEvalTests : IClassFixture<RuleEvalFixture>
{
    private readonly RuleEvalFixture _fixture;
    private readonly ITestOutputHelper _output;

    public RuleEvalTests(RuleEvalFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public static IEnumerable<object[]> CaseIds() => RuleEvalFixture.LoadCases().Select(c => new object[] { c.Id });

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void Kural_motoru_orneklerde_elle_hesaplanan_sonucu_verir(string id)
    {
        var result = _fixture.Results[id];

        Assert.Equal(result.Case.Expect.EndpointCount, result.ActualEndpointCount);
        Assert.Equal(result.Case.Expect.Score, result.ActualScore);
        Assert.Equal(result.Case.Expect.Findings.OrderBy(x => x), result.ActualFindings.OrderBy(x => x));
    }

    [Fact]
    public void Ornek_sayisi_yeterli_ve_kimlikler_benzersizdir()
    {
        var cases = RuleEvalFixture.LoadCases();
        Assert.InRange(cases.Count, 25, 40);
        Assert.Equal(cases.Count, cases.Select(c => c.Id).Distinct().Count());
        Assert.Contains(cases, c => c.Ideal is not null);   // bilinen sınırlar da kayıtlı
    }

    [Fact]
    public void Ozet_ve_rapor()
    {
        var summary = RuleEvalFixture.Summarize(_fixture.Results.Values.ToList());
        var markdown = RuleEvalFixture.ToMarkdown(_fixture.Results.Values.OrderBy(r => r.Case.Order).ToList(), summary);
        _output.WriteLine(markdown);

        // Motor tanımına uyum tam olmalı; gerçek dünya uyumu ise ölçülen bir sayıdır, eşik değildir.
        Assert.Equal(summary.Total, summary.SpecMatches);

        if (Environment.GetEnvironmentVariable("EVAL_WRITE_REPORT") == "1")
        {
            var path = Path.Combine(RuleEvalFixture.RepoRoot(), "eval", "results", "rule-engine.md");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, markdown, new UTF8Encoding(false));
        }
    }
}

public sealed record EvalExpectation(int EndpointCount, int Score, List<string> Findings);

public sealed record EvalIdeal(int Score, List<string> Findings, string Note);

public sealed record EvalCase(string Id, string Description, JsonElement Document, EvalExpectation Expect, EvalIdeal? Ideal)
{
    public int Order { get; init; }
}

public sealed record EvalResult(EvalCase Case, int ActualScore, int ActualEndpointCount, List<string> ActualFindings)
{
    public bool SpecMatch =>
        ActualScore == Case.Expect.Score
        && ActualEndpointCount == Case.Expect.EndpointCount
        && Case.Expect.Findings.OrderBy(x => x).SequenceEqual(ActualFindings.OrderBy(x => x));

    public int IdealScore => Case.Ideal?.Score ?? Case.Expect.Score;

    public List<string> IdealFindings => Case.Ideal?.Findings ?? Case.Expect.Findings;

    public bool IdealMatch =>
        ActualScore == IdealScore && IdealFindings.OrderBy(x => x).SequenceEqual(ActualFindings.OrderBy(x => x));
}

public sealed record EvalSummary(int Total, int SpecMatches, int IdealMatches, int TruePositives, int FalsePositives, int FalseNegatives)
{
    public double Precision => TruePositives + FalsePositives == 0 ? 1 : (double)TruePositives / (TruePositives + FalsePositives);

    public double Recall => TruePositives + FalseNegatives == 0 ? 1 : (double)TruePositives / (TruePositives + FalseNegatives);
}

/// <summary>Bütün örnekleri bir kez, gerçek yükleme hattından geçirir ve sonuçları saklar.</summary>
public sealed class RuleEvalFixture : IAsyncLifetime
{
    private readonly ApiFactory _factory = new();

    public Dictionary<string, EvalResult> Results { get; } = new();

    private static readonly Dictionary<string, string> MessageToCode = new()
    {
        ["Endpoint açıklaması eksik"] = "NO_SUMMARY",
        ["Hata durum kodları tanımlanmamış"] = "NO_ERROR_CODES",
        [WarningMessages.MissingAuthentication] = "NO_AUTH",
        ["ID bazlı veri silme işleminde yetkilendirme (BOLA) riski bulunuyor."] = "BOLA_DELETE_ID",
        ["GET metodu ile veri üzerinde kalıcı işlem yapılması REST güvenliğine aykırıdır."] = "GET_MUTATES"
    };

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ApiInsightStudio.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Depo kökü (ApiInsightStudio.sln) bulunamadı.");
    }

    public static List<EvalCase> LoadCases()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var directory = Path.Combine(RepoRoot(), "eval", "rules", "cases");
        return Directory.GetFiles(directory, "*.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select((file, index) => JsonSerializer.Deserialize<EvalCase>(File.ReadAllText(file), options)! with { Order = index + 1 })
            .ToList();
    }

    public async Task InitializeAsync()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("rule-eval@example.com");

        foreach (var evalCase in LoadCases())
        {
            var upload = await client.PostAsJsonAsync("/api/project/upload",
                new { openApiContent = evalCase.Document.GetRawText() });
            if (upload.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"{evalCase.Id}: yükleme başarısız ({(int)upload.StatusCode}): {await upload.Content.ReadAsStringAsync()}");

            var projectId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("projectId").GetInt32();
            var dashboard = await client.GetFromJsonAsync<JsonElement>($"/api/project/{projectId}/dashboard");

            var findings = dashboard.GetProperty("warnings").EnumerateArray()
                .Select(w =>
                {
                    var message = w.GetProperty("message").GetString()!;
                    var code = MessageToCode.TryGetValue(message, out var c) ? c : $"BILINMEYEN:{message}";
                    return $"{w.GetProperty("endpointMethod").GetString()} {w.GetProperty("endpointPath").GetString()} {code}";
                })
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            Results[evalCase.Id] = new EvalResult(
                evalCase,
                dashboard.GetProperty("kaliteSkoru").GetInt32(),
                dashboard.GetProperty("toplamEndpointSayisi").GetInt32(),
                findings);
        }
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    public static EvalSummary Summarize(IReadOnlyCollection<EvalResult> results)
    {
        int tp = 0, fp = 0, fn = 0;
        foreach (var r in results)
        {
            var actual = r.ActualFindings.ToHashSet();
            var ideal = r.IdealFindings.ToHashSet();
            tp += actual.Intersect(ideal).Count();
            fp += actual.Except(ideal).Count();
            fn += ideal.Except(actual).Count();
        }

        return new EvalSummary(results.Count, results.Count(r => r.SpecMatch), results.Count(r => r.IdealMatch), tp, fp, fn);
    }

    public static string ToMarkdown(IReadOnlyList<EvalResult> results, EvalSummary s)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Kural motoru değerlendirmesi");
        sb.AppendLine();
        sb.AppendLine("Bu dosya `EVAL_WRITE_REPORT=1 dotnet test --filter Category=Eval` ile üretilir; elle düzenlenmez.");
        sb.AppendLine("Her örnek gerçek yükleme + analiz hattından (ayrıştırma, kurallar, skor) geçirilir.");
        sb.AppendLine();
        sb.AppendLine("## Özet");
        sb.AppendLine();
        sb.AppendLine($"- Örnek sayısı: **{s.Total}**");
        sb.AppendLine($"- **Kural tanımına uyum** (elle hesaplanan skor ve bulgular): **{s.SpecMatches}/{s.Total}**");
        sb.AppendLine($"- **Gerçek dünya uyumu** (ideal sonuçla birebir): **{s.IdealMatches}/{s.Total}**");
        sb.AppendLine($"- Bulgu düzeyinde, ideal sonuca göre: kesinlik **%{s.Precision * 100:0.#}** ({s.TruePositives} doğru, {s.FalsePositives} yanlış pozitif), duyarlılık **%{s.Recall * 100:0.#}** ({s.FalseNegatives} kaçan bulgu)");
        sb.AppendLine();
        sb.AppendLine("\"Kural tanımına uyum\" motorun yazıldığı gibi çalıştığını (regresyon), \"gerçek dünya uyumu\" ise kuralların kendisinin ne kadar isabetli olduğunu ölçer. İkisi bilerek ayrıdır: aşağıdaki sınırlar kuralların bilinen zayıflıklarıdır, hata değil.");
        sb.AppendLine();
        sb.AppendLine("## Örnekler");
        sb.AppendLine();
        sb.AppendLine("| # | Örnek | Endpoint | Beklenen skor | Skor | Kural uyumu | Gerçek dünya |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var r in results)
        {
            sb.AppendLine($"| {r.Case.Order} | {r.Case.Id} | {r.ActualEndpointCount} | {r.Case.Expect.Score} | {r.ActualScore} | {(r.SpecMatch ? "✓" : "✗")} | {(r.IdealMatch ? "✓" : $"✗ (ideal {r.IdealScore})")} |");
        }

        var limits = results.Where(r => !r.IdealMatch).ToList();
        sb.AppendLine();
        sb.AppendLine("## Bilinen sınırlar");
        sb.AppendLine();
        if (limits.Count == 0)
            sb.AppendLine("Kayıtlı sınır yok.");
        foreach (var r in limits)
        {
            var fps = r.ActualFindings.Except(r.IdealFindings).ToList();
            var fns = r.IdealFindings.Except(r.ActualFindings).ToList();
            var kind = fps.Count > 0 && fns.Count == 0 ? "yanlış pozitif" : fns.Count > 0 && fps.Count == 0 ? "yanlış negatif" : "karma";
            sb.AppendLine($"- **{r.Case.Id}** ({kind}): {r.Case.Ideal?.Note ?? r.Case.Description}");
        }

        return sb.ToString();
    }
}

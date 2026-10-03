using System.Net;
using ApiInsightStudio.Api.Events;
using ApiInsightStudio.Api.Models;
using ApiInsightStudio.Api.TestRunner;
using Microsoft.AspNetCore.DataProtection;
using ApiInsightStudio.Api.Notifications;

namespace ApiInsightStudio.Api.Tests;

/// <summary>Koşucunun gerçek senaryo üreticisi, sahte hedef sunucu ve gerçek veritabanı üzerinden uçtan uca davranışı.</summary>
public class TestRunExecutorTests
{
    private static async Task<(TestDb Db, int ProjectId, int RunId)> TypicalProjectAsync(
        bool allowMutating = false, string? target = TestRunFixtures.Target, string? token = TestRunFixtures.Token)
    {
        var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), TestRunFixtures.TypicalEndpoints());
        await TestRunFixtures.GenerateScenariosAsync(db, projectId);
        TestRunFixtures.SetSettings(db, projectId, target: target, token: token, allowMutating: allowMutating);
        return (db, projectId, TestRunFixtures.AddRun(db, projectId));
    }

    private static List<TestRunResult> Results(TestRun run) => run.Results.OrderBy(r => r.Id).ToList();

    private static TestRunResult Find(TestRun run, string method, string path, int expected) =>
        run.Results.Single(r => r.Method == method && r.Path == path && r.ExpectedStatusCode == expected);

    // ----- Varsayılan: yalnızca GET -----

    [Fact]
    public async Task Varsayilan_olarak_yalnizca_GET_calisir_ve_11_senaryodan_3u_gecer_8i_atlanir()
    {
        var (db, _, runId) = await TypicalProjectAsync(allowMutating: false);
        using (db)
        {
            var handler = new FakeTargetHandler(FakeTargetHandler.TypicalApi);

            var run = await TestRunFixtures.ExecuteAsync(db, runId, handler);

            Assert.Equal(TestRun.StatusCompleted, run.Status);
            Assert.Equal(11, run.Total);
            Assert.Equal(3, run.Passed);
            Assert.Equal(0, run.Failed);
            Assert.Equal(8, run.Skipped);

            // Hedefe yalnızca GET gitti: mutating hiçbir istek atılmadı
            Assert.Equal(3, handler.Requests.Count);
            Assert.All(handler.Requests, r => Assert.Equal("GET", r.Method));
        }
    }

    [Fact]
    public async Task Mutating_kapaliyken_POST_ve_DELETE_senaryolari_nedenle_atlanir()
    {
        var (db, _, runId) = await TypicalProjectAsync(allowMutating: false);
        using (db)
        {
            var run = await TestRunFixtures.ExecuteAsync(db, runId, new FakeTargetHandler(FakeTargetHandler.TypicalApi));

            var skippedMutating = run.Results.Where(r => r.Method is "POST" or "DELETE").ToList();
            Assert.Equal(6, skippedMutating.Count);
            Assert.All(skippedMutating, r =>
            {
                Assert.Equal(TestRunResult.OutcomeSkipped, r.Outcome);
                Assert.Contains("allowMutatingTests", r.Reason);
                Assert.Null(r.RequestPath);
                Assert.Null(r.ActualStatusCode);
            });
        }
    }

    [Fact]
    public async Task Her_senaryo_dogru_kural_ve_nedenle_siniflandirilir()
    {
        var (db, _, runId) = await TypicalProjectAsync();
        using (db)
        {
            var run = await TestRunFixtures.ExecuteAsync(db, runId, new FakeTargetHandler(FakeTargetHandler.TypicalApi));

            // GET /items (herkese açık)
            Assert.Equal(TestRunResult.OutcomePassed, Find(run, "GET", "/items", 200).Outcome);
            Assert.Contains("herkese açık", Find(run, "GET", "/items", 401).Reason, StringComparison.OrdinalIgnoreCase);
            // GET /items/{id}: geçerli id bilinmediği için 200 atlanır; 401 ve 404 sahte id ile çalışır
            Assert.Contains("parametre", Find(run, "GET", "/items/{id}", 200).Reason);
            var unauthorized = Find(run, "GET", "/items/{id}", 401);
            Assert.Equal(TestRunResult.OutcomePassed, unauthorized.Outcome);
            Assert.Equal("/items/0", unauthorized.RequestPath);
            Assert.Equal(401, unauthorized.ActualStatusCode);
            var notFound = Find(run, "GET", "/items/{id}", 404);
            Assert.Equal(TestRunResult.OutcomePassed, notFound.Outcome);
            Assert.Equal(404, notFound.ActualStatusCode);
        }
    }

    [Fact]
    public async Task Mutating_acikken_yalnizca_olumsuz_senaryolar_calisir_basarili_olanlar_hic_calismaz()
    {
        var (db, _, runId) = await TypicalProjectAsync(allowMutating: true);
        using (db)
        {
            var handler = new FakeTargetHandler(FakeTargetHandler.TypicalApi);

            var run = await TestRunFixtures.ExecuteAsync(db, runId, handler);

            Assert.Equal(7, run.Passed);
            Assert.Equal(0, run.Failed);
            Assert.Equal(4, run.Skipped);

            // POST/DELETE için başarılı (200) senaryolar mutating açıkken bile atlanır
            Assert.Equal(TestRunResult.OutcomeSkipped, Find(run, "POST", "/items", 200).Outcome);
            Assert.Equal(TestRunResult.OutcomeSkipped, Find(run, "DELETE", "/items/{id}", 200).Outcome);

            // Atılan mutating istekler: yalnızca 401/400/404 senaryoları
            var mutating = handler.Requests.Where(r => r.Method is "POST" or "DELETE").ToList();
            Assert.Equal(4, mutating.Count);
            Assert.Equal(2, mutating.Count(r => r.Method == "POST"));
            Assert.Equal(2, mutating.Count(r => r.Method == "DELETE"));
        }
    }

    [Fact]
    public async Task Mutating_isteklerde_gövde_yalnizca_POST_PUT_PATCH_icin_ve_bos_JSON_olarak_gider()
    {
        var (db, _, runId) = await TypicalProjectAsync(allowMutating: true);
        using (db)
        {
            var handler = new FakeTargetHandler(FakeTargetHandler.TypicalApi);

            await TestRunFixtures.ExecuteAsync(db, runId, handler);

            Assert.All(handler.Requests.Where(r => r.Method == "POST"), r =>
            {
                Assert.Equal("{}", r.Body);
                Assert.Equal("application/json", r.ContentType);
            });
            Assert.All(handler.Requests.Where(r => r.Method is "GET" or "DELETE"), r => Assert.Null(r.Body));
        }
    }

    // ----- Token -----

    [Fact]
    public async Task Token_yalnizca_gerekli_isteklerde_gider_401_senaryosunda_asla_gitmez()
    {
        var (db, _, runId) = await TypicalProjectAsync(allowMutating: true);
        using (db)
        {
            var handler = new FakeTargetHandler(FakeTargetHandler.TypicalApi);

            var run = await TestRunFixtures.ExecuteAsync(db, runId, handler);

            var expectedAuth = $"Bearer {TestRunFixtures.Token}";
            var executed = run.Results.Where(r => r.RequestPath is not null).ToList();

            // Token taşıyan istekler tam olarak: 401 olmayan ve herkese açık olmayan (kimlik isteyen) senaryolar
            var expectedWithToken = executed.Count(r => r.ExpectedStatusCode != 401 && !(r.Method == "GET" && r.Path == "/items"));
            var withToken = handler.Requests.Where(r => r.Authorization is not null).ToList();
            Assert.Equal(expectedWithToken, withToken.Count);
            Assert.All(withToken, r => Assert.Equal(expectedAuth, r.Authorization));

            // Aynı yola (GET /items/0) iki senaryo gider: 401 olanı tokensiz, 404 olanı tokenla
            Assert.Equal(1, handler.Requests.Count(r => r.Method == "GET" && r.Path == "/items/0" && r.Authorization is null));
            Assert.Equal(1, handler.Requests.Count(r => r.Method == "GET" && r.Path == "/items/0" && r.Authorization is not null));

            // Herkese açık uç noktaya token gönderilmez
            Assert.Null(handler.Requests.Single(r => r.Method == "GET" && r.Path == "/items").Authorization);

            // Mutating istekler: 401 senaryoları (POST+DELETE) tokensiz, 400/404 senaryoları (POST+DELETE) tokenlı
            var mutating = handler.Requests.Where(r => r.Method is "POST" or "DELETE").ToList();
            Assert.Equal(2, mutating.Count(r => r.Authorization is null));
            Assert.Equal(2, mutating.Count(r => r.Authorization is not null));
        }
    }

    [Fact]
    public async Task Token_tanimli_degilse_kimlik_gerektiren_senaryolar_nedenle_atlanir()
    {
        var (db, _, runId) = await TypicalProjectAsync(allowMutating: true, token: null);
        using (db)
        {
            var handler = new FakeTargetHandler(FakeTargetHandler.TypicalApi);

            var run = await TestRunFixtures.ExecuteAsync(db, runId, handler);

            Assert.Contains("token", Find(run, "GET", "/items/{id}", 404).Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("token", Find(run, "POST", "/items", 400).Reason, StringComparison.OrdinalIgnoreCase);
            Assert.All(handler.Requests, r => Assert.Null(r.Authorization));
        }
    }

    [Fact]
    public async Task Token_sonuclara_hata_metnine_ya_da_istege_baska_yerde_yazilmaz()
    {
        var (db, _, runId) = await TypicalProjectAsync(allowMutating: true);
        using (db)
        {
            var handler = new FakeTargetHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

            var run = await TestRunFixtures.ExecuteAsync(db, runId, handler);

            Assert.DoesNotContain(TestRunFixtures.Token, run.Error ?? "");
            Assert.All(run.Results, r =>
            {
                Assert.DoesNotContain(TestRunFixtures.Token, r.Reason ?? "");
                Assert.DoesNotContain(TestRunFixtures.Token, r.RequestPath ?? "");
            });
            Assert.DoesNotContain(handler.Requests, r => r.PathAndQuery.Contains(TestRunFixtures.Token));
        }
    }

    [Fact]
    public async Task Cozulemeyen_token_koşuyu_hic_istek_atmadan_Failed_yapar()
    {
        var db = new TestDb();
        using (db)
        {
            var projectId = db.SeedProject(db.SeedUser(), TestRunFixtures.TypicalEndpoints());
            await TestRunFixtures.GenerateScenariosAsync(db, projectId);
            var otherKeyRing = new WebhookSecretProtector(new EphemeralDataProtectionProvider());
            TestRunFixtures.SetSettings(db, projectId, protector: otherKeyRing); // başka anahtarla şifrelenmiş
            var runId = TestRunFixtures.AddRun(db, projectId);
            var handler = new FakeTargetHandler(FakeTargetHandler.TypicalApi);

            var run = await TestRunFixtures.ExecuteAsync(db, runId, handler);

            Assert.Equal(TestRun.StatusFailed, run.Status);
            Assert.Contains("yeniden tanımlayın", run.Error);
            Assert.Empty(handler.Requests);
        }
    }

    // ----- Sonuç sınıflandırma -----

    [Fact]
    public async Task Beklenmeyen_durum_kodu_Failed_olur_ve_nedeni_yazar()
    {
        var (db, _, runId) = await TypicalProjectAsync();
        using (db)
        {
            var handler = new FakeTargetHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

            var run = await TestRunFixtures.ExecuteAsync(db, runId, handler);

            Assert.Equal(3, run.Failed); // 3 çalışan senaryonun hepsi 500 aldı
            var failed = Find(run, "GET", "/items", 200);
            Assert.Equal(TestRunResult.OutcomeFailed, failed.Outcome);
            Assert.Equal(500, failed.ActualStatusCode);
            Assert.Equal("Beklenen 2xx, gelen 500.", failed.Reason);
            Assert.Equal("Beklenen 401 veya 403, gelen 500.", Find(run, "GET", "/items/{id}", 401).Reason);
            Assert.Equal("Beklenen 404, gelen 500.", Find(run, "GET", "/items/{id}", 404).Reason);
        }
    }

    [Fact]
    public async Task Yonlendirme_basarisiz_sayilir()
    {
        var (db, _, runId) = await TypicalProjectAsync();
        using (db)
        {
            var handler = new FakeTargetHandler(_ => new HttpResponseMessage(HttpStatusCode.Found));

            var run = await TestRunFixtures.ExecuteAsync(db, runId, handler);

            var result = Find(run, "GET", "/items", 200);
            Assert.Equal(TestRunResult.OutcomeFailed, result.Outcome);
            Assert.Equal(302, result.ActualStatusCode);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, true)]       // 401 yerine 403 de kabul
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.OK, false)]             // korumasız: 401 beklenirken 200
    public async Task Yetkisiz_erisim_senaryosu_401_veya_403u_kabul_eder(HttpStatusCode returned, bool passes)
    {
        var (db, _, runId) = await TypicalProjectAsync();
        using (db)
        {
            var handler = new FakeTargetHandler(r => r.Authorization is null
                ? new HttpResponseMessage(returned)
                : new HttpResponseMessage(HttpStatusCode.NotFound));

            var run = await TestRunFixtures.ExecuteAsync(db, runId, handler);

            var outcome = Find(run, "GET", "/items/{id}", 401).Outcome;
            Assert.Equal(passes ? TestRunResult.OutcomePassed : TestRunResult.OutcomeFailed, outcome);
        }
    }

    [Fact]
    public async Task Zaman_asimi_Failed_olur_ve_yanit_kodu_yoktur()
    {
        var (db, _, runId) = await TypicalProjectAsync();
        using (db)
        {
            var handler = new FakeTargetHandler(_ => throw new TaskCanceledException("zaman aşımı"));

            var run = await TestRunFixtures.ExecuteAsync(db, runId, handler);

            var result = Find(run, "GET", "/items", 200);
            Assert.Equal(TestRunResult.OutcomeFailed, result.Outcome);
            Assert.Null(result.ActualStatusCode);
            Assert.Contains("Zaman aşımı", result.Reason);
        }
    }

    [Fact]
    public async Task Baglanti_hatasi_Failed_olur_ve_hata_metni_adresi_ya_da_sorgu_tokenini_sizdirmaz()
    {
        var (db, _, runId) = await TypicalProjectAsync();
        using (db)
        {
            var handler = new FakeTargetHandler(_ => throw new HttpRequestException(
                "baglanti hatasi https://api.example.test/items?token=COK-GIZLI", new System.Net.Sockets.SocketException()));

            var run = await TestRunFixtures.ExecuteAsync(db, runId, handler);

            Assert.All(run.Results.Where(r => r.Outcome == TestRunResult.OutcomeFailed), r =>
            {
                Assert.DoesNotContain("COK-GIZLI", r.Reason);
                Assert.DoesNotContain("api.example.test", r.Reason);
                Assert.Contains("Bağlantı hatası", r.Reason);
            });
        }
    }

    [Fact]
    public async Task Guvensiz_hedef_baglanti_engeli_Failed_olur_ve_nedeni_soyler()
    {
        var (db, _, runId) = await TypicalProjectAsync();
        using (db)
        {
            var handler = new FakeTargetHandler(_ => throw new HttpRequestException(
                "genel hata", new UnsafeTargetException("Hedef adres özel/yerel bir ağa çözülüyor.")));

            var run = await TestRunFixtures.ExecuteAsync(db, runId, handler);

            Assert.Contains("güvenli değil", Find(run, "GET", "/items", 200).Reason);
        }
    }

    [Fact]
    public async Task Ardisik_baglanti_hatalarindan_sonra_kalan_istekler_atilmadan_atlanir()
    {
        var db = new TestDb();
        using (db)
        {
            var specs = Enumerable.Range(0, 15).Select(i => new EndpointSpec(Method: "GET", Path: $"/ep{i}", AuthType: null)).ToArray();
            var projectId = db.SeedProject(db.SeedUser(), specs);
            await TestRunFixtures.GenerateScenariosAsync(db, projectId);
            TestRunFixtures.SetSettings(db, projectId);
            var runId = TestRunFixtures.AddRun(db, projectId);
            var handler = new FakeTargetHandler(_ => throw new HttpRequestException("kapalı", new System.Net.Sockets.SocketException()));

            var run = await TestRunFixtures.ExecuteAsync(db, runId, handler);

            var calls = handler.Requests.Count;
            Assert.InRange(calls, TestRunExecutor.ConsecutiveConnectionFailureLimit,
                TestRunExecutor.ConsecutiveConnectionFailureLimit + TestRunExecutor.MaxConcurrency - 1);
            Assert.Equal(calls, run.Failed);

            // 15 çalıştırılabilir senaryodan çağrı yapılmayanlar "ulaşılamıyor" nedeniyle atlandı
            var abortedSkips = run.Results.Where(r => r.Reason is not null && r.Reason.Contains("ulaşılamıyor")).ToList();
            Assert.Equal(15 - calls, abortedSkips.Count);
            Assert.All(abortedSkips, r => Assert.Equal(TestRunResult.OutcomeSkipped, r.Outcome));
        }
    }

    [Fact]
    public async Task Yanit_alinirsa_ardisik_hata_sayaci_sifirlanir_ve_koşu_surer()
    {
        var db = new TestDb();
        using (db)
        {
            var specs = Enumerable.Range(0, 12).Select(i => new EndpointSpec(Method: "GET", Path: $"/ep{i}", AuthType: null)).ToArray();
            var projectId = db.SeedProject(db.SeedUser(), specs);
            await TestRunFixtures.GenerateScenariosAsync(db, projectId);
            TestRunFixtures.SetSettings(db, projectId);
            var runId = TestRunFixtures.AddRun(db, projectId);
            var counter = 0;
            // Her 4. istek ulaşılamıyor, diğerleri 200: 5 ardışık hataya hiç ulaşılmaz
            var handler = new FakeTargetHandler(_ => Interlocked.Increment(ref counter) % 4 == 0
                ? throw new HttpRequestException("ara sıra hata", new System.Net.Sockets.SocketException())
                : new HttpResponseMessage(HttpStatusCode.OK));

            var run = await TestRunFixtures.ExecuteAsync(db, runId, handler);

            Assert.Equal(12, handler.Requests.Count); // hepsi denendi, hiçbiri atlanmadı
            Assert.Equal(3, run.Failed);
            Assert.Equal(9, run.Passed);
        }
    }

    // ----- Sınırlar -----

    [Fact]
    public async Task Istek_siniri_200dur_fazlasi_atlanir()
    {
        var db = new TestDb();
        using (db)
        {
            var specs = Enumerable.Range(0, 250).Select(i => new EndpointSpec(Method: "GET", Path: $"/ep{i}", AuthType: null)).ToArray();
            var projectId = db.SeedProject(db.SeedUser(), specs);
            await TestRunFixtures.GenerateScenariosAsync(db, projectId);
            TestRunFixtures.SetSettings(db, projectId);
            var runId = TestRunFixtures.AddRun(db, projectId);
            var handler = new FakeTargetHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

            var run = await TestRunFixtures.ExecuteAsync(db, runId, handler);

            Assert.Equal(TestRunPlanner.MaxRequests, handler.Requests.Count);
            Assert.Equal(TestRunPlanner.MaxRequests, run.Passed);
            Assert.Equal(500, run.Total); // 250 başarılı + 250 yetkisiz (herkese açık olduğu için atlanan)
            Assert.Equal(50, run.Results.Count(r => r.Reason is not null && r.Reason.Contains("istek sınırı")));
        }
    }

    [Fact]
    public async Task Ayni_anda_en_fazla_3_istek_ucusta_olur()
    {
        var db = new TestDb();
        using (db)
        {
            var specs = Enumerable.Range(0, 20).Select(i => new EndpointSpec(Method: "GET", Path: $"/ep{i}", AuthType: null)).ToArray();
            var projectId = db.SeedProject(db.SeedUser(), specs);
            await TestRunFixtures.GenerateScenariosAsync(db, projectId);
            TestRunFixtures.SetSettings(db, projectId);
            var runId = TestRunFixtures.AddRun(db, projectId);

            var inFlight = 0;
            var maxInFlight = 0;
            var handler = new ConcurrencyProbeHandler(
                onStart: () => { var now = Interlocked.Increment(ref inFlight); InterlockedMax(ref maxInFlight, now); },
                onEnd: () => Interlocked.Decrement(ref inFlight));

            await TestRunFixtures.ExecuteAsync(db, runId, handler);

            Assert.InRange(maxInFlight, 1, TestRunExecutor.MaxConcurrency);
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, value, current) == current)
                return;
        }
    }

    private sealed class ConcurrencyProbeHandler : HttpMessageHandler
    {
        private readonly Action _onStart;
        private readonly Action _onEnd;

        public ConcurrencyProbeHandler(Action onStart, Action onEnd)
        {
            _onStart = onStart;
            _onEnd = onEnd;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _onStart();
            try
            {
                await Task.Delay(30, cancellationToken); // eşzamanlılık üst üste binsin
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            finally
            {
                _onEnd();
            }
        }
    }

    // ----- Adres ve yol -----

    [Fact]
    public async Task Kok_adresteki_yol_oneki_isteklere_eklenir()
    {
        var (db, _, runId) = await TypicalProjectAsync(target: "https://api.example.test/v1");
        using (db)
        {
            var handler = new FakeTargetHandler(FakeTargetHandler.TypicalApi);

            await TestRunFixtures.ExecuteAsync(db, runId, handler);

            Assert.All(handler.Requests, r => Assert.StartsWith("/v1/items", r.PathAndQuery));
        }
    }

    [Fact]
    public async Task Yol_parametresi_OpenAPI_tipine_gore_doldurulur()
    {
        var db = new TestDb();
        using (db)
        {
            var projectId = db.SeedProject(db.SeedUser(),
                new EndpointSpec(Method: "GET", Path: "/users/{userId}", AuthType: null));
            using (var context = db.CreateContext())
            {
                var project = context.Projects.Single(p => p.Id == projectId);
                project.OpenApiContent = """
                    { "openapi": "3.0.0", "info": { "title": "t", "version": "1" },
                      "paths": { "/users/{userId}": { "get": {
                        "parameters": [ { "name": "userId", "in": "path", "required": true, "schema": { "type": "string", "format": "uuid" } } ],
                        "responses": { "200": { "description": "ok" } } } } } }
                    """;
                context.SaveChanges();
            }
            await TestRunFixtures.GenerateScenariosAsync(db, projectId);
            TestRunFixtures.SetSettings(db, projectId, token: null);
            var runId = TestRunFixtures.AddRun(db, projectId);
            var handler = new FakeTargetHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

            var run = await TestRunFixtures.ExecuteAsync(db, runId, handler);

            Assert.Equal("/users/00000000-0000-0000-0000-000000000000", Find(run, "GET", "/users/{userId}", 404).RequestPath);
            Assert.Contains(handler.Requests, r => r.Path == "/users/00000000-0000-0000-0000-000000000000");
        }
    }

    // ----- Koşu başlatılamıyorsa -----

    [Fact]
    public async Task Hedef_adres_yoksa_koşu_Failed_olur_istek_atilmaz_ve_olay_yayinlanmaz()
    {
        var (db, projectId, runId) = await TypicalProjectAsync(target: null);
        using (db)
        {
            var handler = new FakeTargetHandler(FakeTargetHandler.TypicalApi);

            var run = await TestRunFixtures.ExecuteAsync(db, runId, handler);

            Assert.Equal(TestRun.StatusFailed, run.Status);
            Assert.Contains("ayarlı değil", run.Error);
            Assert.NotNull(run.CompletedAt);
            Assert.Empty(handler.Requests);
            using var context = db.CreateContext();
            Assert.DoesNotContain(context.OutboxMessages.ToList(), m => m.Type == nameof(TestRunCompleted));
        }
    }

    [Theory]
    [InlineData("http://api.example.test")]            // düz http, izin listesinde değil
    [InlineData("https://user:pw@api.example.test")]
    [InlineData("https://api.example.test/v1?x=1")]
    [InlineData("ftp://api.example.test")]
    public async Task Gecersiz_ya_da_guvensiz_hedef_adres_koşuyu_Failed_yapar_ve_istek_atilmaz(string target)
    {
        var (db, _, runId) = await TypicalProjectAsync(target: target);
        using (db)
        {
            var handler = new FakeTargetHandler(FakeTargetHandler.TypicalApi);

            var run = await TestRunFixtures.ExecuteAsync(db, runId, handler);

            Assert.Equal(TestRun.StatusFailed, run.Status);
            Assert.Contains("geçerli değil", run.Error);
            Assert.Empty(handler.Requests);
            Assert.Empty(run.Results);
        }
    }

    [Fact]
    public async Task Izin_listesindeki_host_icin_duz_http_hedefi_calisir()
    {
        var (db, _, runId) = await TypicalProjectAsync(target: "http://127.0.0.1:5000");
        using (db)
        {
            var handler = new FakeTargetHandler(FakeTargetHandler.TypicalApi);

            var run = await TestRunFixtures.ExecuteAsync(db, runId, handler, allowedPrivateHosts: "127.0.0.1");

            Assert.Equal(TestRun.StatusCompleted, run.Status);
            Assert.Equal(3, handler.Requests.Count);
        }
    }

    [Fact]
    public async Task Senaryo_yoksa_koşu_Failed_olur()
    {
        var db = new TestDb();
        using (db)
        {
            var projectId = db.SeedProject(db.SeedUser(), TestRunFixtures.TypicalEndpoints()); // senaryo üretilmedi
            TestRunFixtures.SetSettings(db, projectId);
            var runId = TestRunFixtures.AddRun(db, projectId);
            var handler = new FakeTargetHandler(FakeTargetHandler.TypicalApi);

            var run = await TestRunFixtures.ExecuteAsync(db, runId, handler);

            Assert.Equal(TestRun.StatusFailed, run.Status);
            Assert.Contains("senaryosu yok", run.Error);
            Assert.Empty(handler.Requests);
        }
    }

    // ----- Kayıt ve olay -----

    [Fact]
    public async Task Sonuc_alanlari_hedef_host_ve_tamamlanma_zamani_kaydedilir()
    {
        var (db, _, runId) = await TypicalProjectAsync();
        using (db)
        {
            var clock = new TestClock();

            var run = await TestRunFixtures.ExecuteAsync(db, runId, new FakeTargetHandler(FakeTargetHandler.TypicalApi), clock);

            Assert.Equal("api.example.test", run.TargetHost);
            Assert.Equal(clock.GetUtcNow().UtcDateTime, run.CompletedAt);
            Assert.Equal(run.Results.Count, run.Total);
            Assert.Equal(run.Total, run.Passed + run.Failed + run.Skipped);
            Assert.All(run.Results.Where(r => r.Outcome != TestRunResult.OutcomeSkipped), r =>
            {
                Assert.NotNull(r.RequestPath);
                Assert.NotNull(r.ActualStatusCode);
                Assert.True(r.DurationMs >= 0);
            });
            Assert.All(run.Results, r => Assert.NotNull(r.EndpointId));
        }
    }

    [Fact]
    public async Task Tamamlanan_koşu_TestRunCompleted_olayini_ozet_sayilarla_yayinlar()
    {
        var (db, projectId, runId) = await TypicalProjectAsync();
        using (db)
        {
            var run = await TestRunFixtures.ExecuteAsync(db, runId, new FakeTargetHandler(FakeTargetHandler.TypicalApi));

            using var context = db.CreateContext();
            var message = Assert.Single(context.OutboxMessages.Where(m => m.Type == nameof(TestRunCompleted)).ToList());
            var evt = System.Text.Json.JsonSerializer.Deserialize<TestRunCompleted>(message.PayloadJson)!;
            Assert.Equal(projectId, evt.ProjectId);
            Assert.Equal(runId, evt.RunId);
            Assert.Equal(run.Passed, evt.Passed);
            Assert.Equal(run.Failed, evt.Failed);
            Assert.Equal(run.Skipped, evt.Skipped);
        }
    }

    [Fact]
    public async Task Baska_projenin_senaryolari_koşuya_karismaz()
    {
        var db = new TestDb();
        using (db)
        {
            var userId = db.SeedUser();
            var mine = db.SeedProject(userId, new EndpointSpec(Method: "GET", Path: "/mine", AuthType: null));
            var other = db.SeedProject(userId, new EndpointSpec(Method: "GET", Path: "/other", AuthType: null));
            await TestRunFixtures.GenerateScenariosAsync(db, mine);
            await TestRunFixtures.GenerateScenariosAsync(db, other);
            TestRunFixtures.SetSettings(db, mine);
            var runId = TestRunFixtures.AddRun(db, mine);
            var handler = new FakeTargetHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

            var run = await TestRunFixtures.ExecuteAsync(db, runId, handler);

            Assert.All(run.Results, r => Assert.Equal("/mine", r.Path));
            Assert.All(handler.Requests, r => Assert.Equal("/mine", r.Path));
        }
    }
}

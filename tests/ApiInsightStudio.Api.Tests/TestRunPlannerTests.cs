using ApiInsightStudio.Api.TestRunner;

namespace ApiInsightStudio.Api.Tests;

/// <summary>Üretilmiş senaryolardan hangilerinin güvenle ve anlamlı biçimde çalıştırılacağını belirleyen kurallar.</summary>
public class TestRunPlannerTests
{
    private const string Bearer = "Bearer";

    private static ScenarioInput S(string method, string path, int expected, string? auth = Bearer) =>
        new(EndpointId: 1, Title: "t", Method: method, Path: path, ExpectedStatusCode: expected, AuthType: auth);

    private static string Fill(string method, string path) => path.Replace("{id}", "0");

    private static PlanItem PlanOne(ScenarioInput scenario, bool allowMutating = false, bool hasToken = false) =>
        Assert.Single(TestRunPlanner.Plan(new[] { scenario }, allowMutating, hasToken, Fill));

    private static void AssertSkipped(PlanItem item, string reasonPart)
    {
        Assert.False(item.IsRunnable);
        Assert.Contains(reasonPart, item.SkipReason, StringComparison.OrdinalIgnoreCase);
    }

    // ----- 200: başarılı istek -----

    [Fact]
    public void Herkese_acik_parametresiz_GET_200_calistirilir_tokensiz_ve_govdesiz()
    {
        var item = PlanOne(S("GET", "/items", 200, auth: null));

        Assert.True(item.IsRunnable);
        Assert.Equal(new PlannedCall("GET", "/items", SendToken: false, SendBody: false), item.Call);
    }

    [Fact]
    public void Kimlik_gerektiren_GET_200_token_yoksa_atlanir()
    {
        AssertSkipped(PlanOne(S("GET", "/items", 200), hasToken: false), "token");
    }

    [Fact]
    public void Kimlik_gerektiren_GET_200_token_varsa_tokenla_calistirilir()
    {
        var item = PlanOne(S("GET", "/items", 200), hasToken: true);

        Assert.True(item.Call!.SendToken);
    }

    [Fact]
    public void Yol_parametreli_GET_200_atlanir_cunku_gecerli_deger_bilinmiyor()
    {
        AssertSkipped(PlanOne(S("GET", "/items/{id}", 200, auth: null), hasToken: true), "parametre");
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public void Mutating_metotta_basarili_senaryo_mutating_acik_olsa_bile_calistirilmaz(string method)
    {
        AssertSkipped(PlanOne(S(method, "/items", 200), allowMutating: true, hasToken: true), "başarılı senaryo");
    }

    [Fact]
    public void HEAD_non_mutating_oldugu_icin_200_calistirilir()
    {
        Assert.True(PlanOne(S("HEAD", "/items", 200, auth: null)).IsRunnable);
    }

    // ----- 401: token yok -----

    [Fact]
    public void Kimlik_gerektiren_uc_noktada_401_tokensiz_calistirilir_token_olsa_bile()
    {
        var item = PlanOne(S("GET", "/items", 401), hasToken: true);

        Assert.True(item.IsRunnable);
        Assert.False(item.Call!.SendToken); // 401 senaryosunda token asla gönderilmez
    }

    [Fact]
    public void Herkese_acik_uc_noktada_401_atlanir()
    {
        AssertSkipped(PlanOne(S("GET", "/items", 401, auth: null)), "herkese açık");
    }

    [Fact]
    public void Parametreli_yolda_401_icin_yol_sahte_degerle_doldurulur()
    {
        var item = PlanOne(S("GET", "/items/{id}", 401));

        Assert.Equal("/items/0", item.Call!.RequestPath);
    }

    [Fact]
    public void POST_ile_401_mutating_aciksa_bos_JSON_govdeyle_ve_tokensiz_calisir()
    {
        var item = PlanOne(S("POST", "/items", 401), allowMutating: true);

        Assert.Equal(new PlannedCall("POST", "/items", SendToken: false, SendBody: true), item.Call);
    }

    [Fact]
    public void DELETE_ile_401_govde_gondermez()
    {
        var item = PlanOne(S("DELETE", "/items/{id}", 401), allowMutating: true);

        Assert.False(item.Call!.SendBody);
    }

    // ----- 404: geçersiz id -----

    [Fact]
    public void Parametreli_GET_404_sahte_idyle_ve_kimlik_gerekiyorsa_tokenla_calisir()
    {
        var item = PlanOne(S("GET", "/items/{id}", 404), hasToken: true);

        Assert.Equal(new PlannedCall("GET", "/items/0", SendToken: true, SendBody: false), item.Call);
    }

    [Fact]
    public void Herkese_acik_parametreli_GET_404_tokensiz_calisir()
    {
        var item = PlanOne(S("GET", "/items/{id}", 404, auth: null));

        Assert.True(item.IsRunnable);
        Assert.False(item.Call!.SendToken);
    }

    [Fact]
    public void Kimlik_gerektiren_404_token_yoksa_atlanir_cunku_401_donerdi()
    {
        AssertSkipped(PlanOne(S("GET", "/items/{id}", 404), hasToken: false), "token");
    }

    [Fact]
    public void Parametresiz_yolda_404_anlamsiz_oldugu_icin_atlanir()
    {
        AssertSkipped(PlanOne(S("GET", "/items", 404)), "parametresi yok");
    }

    [Fact]
    public void PUT_ile_404_mutating_aciksa_govdeyle_calisir()
    {
        var item = PlanOne(S("PUT", "/items/{id}", 404), allowMutating: true, hasToken: true);

        Assert.Equal(new PlannedCall("PUT", "/items/0", SendToken: true, SendBody: true), item.Call);
    }

    // ----- 400: hatalı gövde -----

    [Fact]
    public void POST_400_mutating_aciksa_bos_govdeyle_ve_tokenla_calisir()
    {
        var item = PlanOne(S("POST", "/items", 400), allowMutating: true, hasToken: true);

        Assert.Equal(new PlannedCall("POST", "/items", SendToken: true, SendBody: true), item.Call);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("DELETE")]
    public void Govde_almayan_metotlarda_400_atlanir(string method)
    {
        AssertSkipped(PlanOne(S(method, "/items", 400), allowMutating: true, hasToken: true), "yalnızca POST/PUT/PATCH");
    }

    [Fact]
    public void Yol_parametreli_uc_noktada_400_atlanir_cunku_once_404_donebilir()
    {
        AssertSkipped(PlanOne(S("PUT", "/items/{id}", 400), allowMutating: true, hasToken: true), "güvenilir değil");
    }

    [Fact]
    public void Kimlik_gerektiren_400_token_yoksa_atlanir()
    {
        AssertSkipped(PlanOne(S("POST", "/items", 400), allowMutating: true, hasToken: false), "token");
    }

    // ----- Mutating kapısı -----

    [Theory]
    [InlineData("POST", 401)]
    [InlineData("POST", 400)]
    [InlineData("PUT", 404)]
    [InlineData("DELETE", 401)]
    [InlineData("DELETE", 404)]
    [InlineData("PATCH", 401)]
    public void Mutating_kapaliyken_hicbir_mutating_senaryo_calistirilmaz(string method, int expected)
    {
        var path = expected == 404 ? "/items/{id}" : "/items";

        var item = PlanOne(S(method, path, expected), allowMutating: false, hasToken: true);

        AssertSkipped(item, "allowMutatingTests kapalı");
    }

    [Fact]
    public void Mutating_kapali_ve_acik_planlari_GET_icin_ayni_sonucu_verir()
    {
        var closed = PlanOne(S("GET", "/items", 200, auth: null), allowMutating: false);
        var open = PlanOne(S("GET", "/items", 200, auth: null), allowMutating: true);

        Assert.Equal(closed.Call, open.Call);
    }

    [Theory]
    [InlineData("post")]
    [InlineData("Post")]
    [InlineData(" POST ")]
    public void Metot_buyuk_kucuk_harf_ve_bosluktan_etkilenmez(string method)
    {
        var item = PlanOne(S(method, "/items", 401), allowMutating: false);

        AssertSkipped(item, "mutating"); // POST olarak tanınıp mutating kapısına takılır, "desteklenmeyen" demez
    }

    [Theory]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    [InlineData("")]
    [InlineData("FOO")]
    public void Desteklenmeyen_metotlar_atlanir(string method)
    {
        AssertSkipped(PlanOne(S(method, "/items", 401), allowMutating: true, hasToken: true), "Desteklenmeyen metot");
    }

    // ----- Yol güvenliği -----

    [Theory]
    [InlineData("items")]                 // / ile başlamıyor
    [InlineData("")]
    [InlineData("/items?x=1")]            // sorgu
    [InlineData("/items#frag")]           // parça
    [InlineData("/it ems")]               // boşluk
    [InlineData("/it\tems")]
    [InlineData("/items\\x")]             // ters eğik çizgi
    [InlineData("//evil.example.com/x")]  // ağ yolu
    [InlineData("/items\n")]
    public void Gecersiz_yollar_atlanir_ve_istek_planlanmaz(string path)
    {
        AssertSkipped(PlanOne(S("GET", path, 200, auth: null)), "Geçersiz yol");
    }

    [Fact]
    public void Cok_uzun_yol_atlanir()
    {
        AssertSkipped(PlanOne(S("GET", "/" + new string('a', 2100), 200, auth: null)), "Geçersiz yol");
    }

    [Fact]
    public void Doldurma_sonrasi_sablon_kalintisi_varsa_atlanir()
    {
        var items = TestRunPlanner.Plan(new[] { S("GET", "/items/{id}", 404, auth: null) },
            allowMutating: false, hasToken: false, fillPathParameters: (_, p) => p); // doldurmayan sahte doldurucu

        AssertSkipped(Assert.Single(items), "doldurulamadı");
    }

    [Fact]
    public void Doldurma_nokta_nokta_iceren_yol_uretirse_atlanir()
    {
        var items = TestRunPlanner.Plan(new[] { S("GET", "/items/{id}", 404, auth: null) },
            allowMutating: false, hasToken: false, fillPathParameters: (_, _) => "/items/../admin");

        AssertSkipped(Assert.Single(items), "doldurulamadı");
    }

    [Fact]
    public void Bilinmeyen_beklenen_kod_atlanir()
    {
        AssertSkipped(PlanOne(S("GET", "/items", 500, auth: null)), "Bilinmeyen senaryo türü");
    }

    // ----- İstek sınırı -----

    [Fact]
    public void Calistirilabilir_senaryolar_istek_sinirini_asarsa_fazlasi_atlanir_ve_sira_korunur()
    {
        var scenarios = Enumerable.Range(0, TestRunPlanner.MaxRequests + 50)
            .Select(i => S("GET", $"/items{i}", 200, auth: null))
            .ToList();

        var plan = TestRunPlanner.Plan(scenarios, false, false, Fill);

        Assert.Equal(TestRunPlanner.MaxRequests + 50, plan.Count);
        Assert.Equal(TestRunPlanner.MaxRequests, plan.Count(p => p.IsRunnable));
        Assert.All(plan.Take(TestRunPlanner.MaxRequests), p => Assert.True(p.IsRunnable));
        Assert.All(plan.Skip(TestRunPlanner.MaxRequests), p =>
        {
            Assert.False(p.IsRunnable);
            Assert.Contains("istek sınırı", p.SkipReason);
        });
    }

    [Fact]
    public void Atlanan_senaryolar_istek_sinirina_sayilmaz()
    {
        var skipped = Enumerable.Range(0, 300).Select(_ => S("GET", "/items", 401, auth: null)); // hepsi atlanır
        var runnable = Enumerable.Range(0, TestRunPlanner.MaxRequests).Select(i => S("GET", $"/ok{i}", 200, auth: null));

        var plan = TestRunPlanner.Plan(skipped.Concat(runnable).ToList(), false, false, Fill);

        Assert.Equal(TestRunPlanner.MaxRequests, plan.Count(p => p.IsRunnable));
    }

    [Fact]
    public void Plan_senaryo_sirasini_ve_sayisini_korur()
    {
        var scenarios = new[]
        {
            S("GET", "/a", 200, auth: null),
            S("POST", "/b", 400),
            S("GET", "/c/{id}", 404, auth: null)
        };

        var plan = TestRunPlanner.Plan(scenarios, false, false, Fill);

        Assert.Equal(new[] { "/a", "/b", "/c/{id}" }, plan.Select(p => p.Scenario.Path));
    }

    // ----- Kabul edilen durum kodları -----

    [Theory]
    [InlineData(200, 200, true)]
    [InlineData(200, 201, true)]
    [InlineData(200, 204, true)]
    [InlineData(200, 299, true)]
    [InlineData(200, 199, false)]
    [InlineData(200, 301, false)]
    [InlineData(200, 401, false)]
    [InlineData(200, 500, false)]
    [InlineData(401, 401, true)]
    [InlineData(401, 403, true)]
    [InlineData(401, 200, false)]
    [InlineData(401, 404, false)]
    [InlineData(400, 400, true)]
    [InlineData(400, 422, true)]
    [InlineData(400, 401, false)]
    [InlineData(400, 200, false)]
    [InlineData(400, 500, false)]
    [InlineData(404, 404, true)]
    [InlineData(404, 400, false)]
    [InlineData(404, 410, false)]
    [InlineData(404, 200, false)]
    [InlineData(500, 500, true)]
    [InlineData(500, 200, false)]
    public void Beklenen_ve_gelen_kod_eslemesi(int expected, int actual, bool accepted)
    {
        Assert.Equal(accepted, TestRunPlanner.IsAccepted(expected, actual));
    }

    [Theory]
    [InlineData(200, "2xx")]
    [InlineData(401, "401 veya 403")]
    [InlineData(400, "400 veya 422")]
    [InlineData(404, "404")]
    public void Beklenti_aciklamasi(int expected, string text)
    {
        Assert.Equal(text, TestRunPlanner.DescribeExpectation(expected));
    }

    [Theory]
    [InlineData("POST", true)]
    [InlineData("put", true)]
    [InlineData("PATCH", true)]
    [InlineData("DELETE", true)]
    [InlineData("GET", false)]
    [InlineData("head", false)]
    public void IsMutating(string method, bool expected)
    {
        Assert.Equal(expected, TestRunPlanner.IsMutating(method));
    }
}

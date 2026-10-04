using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace ApiInsightStudio.Api.Tests;

/// <summary>Telegram ayarı, test bildirimi ve gerçek uyarı → Telegram teslimatı (sahte Telegram ile, HTTP üzerinden).</summary>
public class TelegramApiTests : IClassFixture<TelegramApiFactory>
{
    // GET temiz. POST: açıklama yok (-10), hata kodu yok (-15), kimlik doğrulama yok (-20) → skor 55.
    private const string OpenApiDocument = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Telegram testi", "version": "1.0.0" },
          "paths": {
            "/items": {
              "get": {
                "summary": "Listeler",
                "responses": { "200": { "description": "ok" }, "400": { "description": "hatalı" } }
              },
              "post": { "responses": { "201": { "description": "oluştu" } } }
            }
          }
        }
        """;

    private readonly TelegramApiFactory _factory;

    public TelegramApiTests(TelegramApiFactory factory) => _factory = factory;

    private async Task<int> UploadProjectAsync(HttpClient owner)
    {
        var upload = await owner.PostAsJsonAsync("/api/project/upload", new { openApiContent = OpenApiDocument });
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        return (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("projectId").GetInt32();
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, int projectId, bool notifyTelegram, int threshold = 60) =>
        client.PutAsJsonAsync($"/api/automation/{projectId}/settings",
            new { scoreThreshold = threshold, notifyOnMissingAuth = true, enabled = true, notifyTelegram, webhookUrl = (string?)null });

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    // ----- Ayar -----

    [Fact]
    public async Task Telegram_yapilandirilmissa_ayarlarda_kullanilabilir_gorunur_ve_varsayilan_kapalidir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("tg-defaults@example.com");
        var projectId = await UploadProjectAsync(owner);

        var settings = await ReadAsync(await owner.GetAsync($"/api/automation/{projectId}/settings"));

        Assert.True(settings.GetProperty("telegramAvailable").GetBoolean());
        Assert.False(settings.GetProperty("notifyTelegram").GetBoolean());
    }

    [Fact]
    public async Task Telegram_bildirimi_acilip_kaydedilir_ve_cevap_token_icermez()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("tg-enable@example.com");
        var projectId = await UploadProjectAsync(owner);

        var put = await PutAsync(owner, projectId, notifyTelegram: true);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var text = await put.Content.ReadAsStringAsync();
        Assert.DoesNotContain(TelegramApiFactory.FakeToken, text);
        Assert.DoesNotContain(TelegramApiFactory.FakeChatId, text);
        Assert.True(JsonDocument.Parse(text).RootElement.GetProperty("notifyTelegram").GetBoolean());

        var get = await ReadAsync(await owner.GetAsync($"/api/automation/{projectId}/settings"));
        Assert.True(get.GetProperty("notifyTelegram").GetBoolean());
    }

    // ----- Test bildirimi -----

    [Fact]
    public async Task Kanal_yokken_test_bildirimi_400_doner()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("tg-nochannel@example.com");
        var projectId = await UploadProjectAsync(owner);

        var response = await owner.PostAsync($"/api/automation/{projectId}/test-notification", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Test_bildirimi_Telegram_a_gider_sonucu_hemen_doner_ve_uyari_kaydi_olusturmaz()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("tg-test@example.com");
        var projectId = await UploadProjectAsync(owner);
        await PutAsync(owner, projectId, notifyTelegram: true);
        var before = _factory.TelegramRequests.Count;

        var response = await owner.PostAsync($"/api/automation/{projectId}/test-notification", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(TelegramApiFactory.FakeToken, text);
        var body = JsonDocument.Parse(text).RootElement;
        var delivery = Assert.Single(body.GetProperty("deliveries").EnumerateArray().ToList());
        Assert.Equal("telegram", delivery.GetProperty("channel").GetString());
        Assert.Equal("Succeeded", delivery.GetProperty("status").GetString());
        Assert.Equal(1, delivery.GetProperty("attempts").GetInt32());

        // Telegram'a tam olarak bir istek gitti, doğru adres ve sohbet kimliğiyle
        var requests = _factory.TelegramRequests.Skip(before).ToList();
        var sent = Assert.Single(requests);
        Assert.Equal($"/bot{TelegramApiFactory.FakeToken}/sendMessage", sent.PathAndQuery);
        using var sentBody = JsonDocument.Parse(sent.Body);
        Assert.Equal(TelegramApiFactory.FakeChatId, sentBody.RootElement.GetProperty("chat_id").GetString());
        Assert.Contains("TEST", sentBody.RootElement.GetProperty("text").GetString());
        Assert.Contains("test bildirimidir", sentBody.RootElement.GetProperty("text").GetString());

        // Gerçek uyarı kaydı açılmadı
        using var scope = _factory.Services.CreateScope();
        Assert.DoesNotContain(scope.ServiceProvider.GetRequiredService<AppDbContext>().Alerts.ToList(),
            a => a.ProjectId == projectId && a.RuleCode == "TEST");
    }

    [Fact]
    public async Task Telegram_401_donerse_teslimat_kalici_olarak_Dead_olur_ve_hata_token_icermez()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("tg-401@example.com");
        var projectId = await UploadProjectAsync(owner);
        await PutAsync(owner, projectId, notifyTelegram: true);

        _factory.ReplyStatus = HttpStatusCode.Unauthorized;
        _factory.ReplyBody = "{\"ok\":false,\"error_code\":401,\"description\":\"Unauthorized\"}";
        try
        {
            var response = await owner.PostAsync($"/api/automation/{projectId}/test-notification", null);

            var text = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(TelegramApiFactory.FakeToken, text);
            var delivery = JsonDocument.Parse(text).RootElement.GetProperty("deliveries").EnumerateArray().Single();
            Assert.Equal("Dead", delivery.GetProperty("status").GetString());
            Assert.Equal(1, delivery.GetProperty("attempts").GetInt32());
            Assert.Equal(401, delivery.GetProperty("lastStatusCode").GetInt32());
            Assert.Contains("Unauthorized", delivery.GetProperty("lastError").GetString());
        }
        finally
        {
            _factory.ReplyStatus = HttpStatusCode.OK;
            _factory.ReplyBody = "{\"ok\":true,\"result\":{\"message_id\":1}}";
        }
    }

    [Fact]
    public async Task Telegram_500_donerse_teslimat_Pending_kalir_ve_ertelenir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("tg-500@example.com");
        var projectId = await UploadProjectAsync(owner);
        await PutAsync(owner, projectId, notifyTelegram: true);

        _factory.ReplyStatus = HttpStatusCode.InternalServerError;
        _factory.ReplyBody = "{\"ok\":false,\"error_code\":500,\"description\":\"Internal Server Error\"}";
        try
        {
            var response = await owner.PostAsync($"/api/automation/{projectId}/test-notification", null);

            var delivery = (await ReadAsync(response)).GetProperty("deliveries").EnumerateArray().Single();
            Assert.Equal("Pending", delivery.GetProperty("status").GetString());
            Assert.Equal(1, delivery.GetProperty("attempts").GetInt32());
            Assert.NotEqual(JsonValueKind.Null, delivery.GetProperty("nextAttemptAt").ValueKind);
        }
        finally
        {
            _factory.ReplyStatus = HttpStatusCode.OK;
            _factory.ReplyBody = "{\"ok\":true,\"result\":{\"message_id\":1}}";

            // Geri çekilmedeki teslimat 10 sn sonra vadesi gelip yavaş/yüklü bir çalıştırmada sonraki testin
            // "tam bir istek gitti" sayımına karışmasın diye bu testin artığı temizlenir.
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.NotificationDeliveries.RemoveRange(db.NotificationDeliveries.Where(d => d.ProjectId == projectId));
            await db.SaveChangesAsync();
        }
    }

    // ----- Gerçek uyarı → Telegram -----

    [Fact]
    public async Task Yeni_gercek_uyari_Telegram_teslimati_acar_ve_islenince_mesaj_gider()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("tg-alert@example.com");
        var projectId = await UploadProjectAsync(owner);   // skor 55, LOW_SCORE zaten açıldı (Telegram henüz kapalı)

        await PutAsync(owner, projectId, notifyTelegram: true, threshold: 50);
        (await owner.PostAsync($"/api/analysis/{projectId}/analyze", null)).EnsureSuccessStatusCode(); // LOW_SCORE çözüldü
        await PutAsync(owner, projectId, notifyTelegram: true, threshold: 60);
        (await owner.PostAsync($"/api/analysis/{projectId}/analyze", null)).EnsureSuccessStatusCode(); // yeni uyarı

        var deliveries = (await ReadAsync(await owner.GetAsync($"/api/automation/{projectId}/deliveries")))
            .EnumerateArray().ToList();
        var delivery = Assert.Single(deliveries);
        Assert.Equal("telegram", delivery.GetProperty("channel").GetString());
        Assert.Equal("Pending", delivery.GetProperty("status").GetString());

        // Arka plan işçisi testte kapalı; yerine teslimat işleyiciyi doğrudan çalıştır
        var before = _factory.TelegramRequests.Count;
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<DeliveryProcessor>().ProcessDueAsync();

        var sent = _factory.TelegramRequests.Skip(before).Single();
        using var sentBody = JsonDocument.Parse(sent.Body);
        var text = sentBody.RootElement.GetProperty("text").GetString()!;
        Assert.Contains("LOW_SCORE", text);
        Assert.Contains("Telegram testi", text); // proje adı OpenAPI başlığından

        var after = (await ReadAsync(await owner.GetAsync($"/api/automation/{projectId}/deliveries?status=Succeeded")))
            .EnumerateArray().ToList();
        Assert.Single(after);
    }

    [Fact]
    public async Task Telegram_kapaliyken_uyari_teslimat_acmaz()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("tg-off@example.com");
        var projectId = await UploadProjectAsync(owner);
        await PutAsync(owner, projectId, notifyTelegram: false, threshold: 50);
        (await owner.PostAsync($"/api/analysis/{projectId}/analyze", null)).EnsureSuccessStatusCode();
        await PutAsync(owner, projectId, notifyTelegram: false, threshold: 60);
        (await owner.PostAsync($"/api/analysis/{projectId}/analyze", null)).EnsureSuccessStatusCode();

        var deliveries = (await ReadAsync(await owner.GetAsync($"/api/automation/{projectId}/deliveries")))
            .EnumerateArray().ToList();

        Assert.Empty(deliveries);
    }

    // ----- Sahiplik -----

    [Fact]
    public async Task Baska_kullanici_test_bildirimi_gonderemez_404()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("tg-owner@example.com");
        var attacker = await _factory.CreateAuthenticatedClientAsync("tg-attacker@example.com");
        var projectId = await UploadProjectAsync(owner);
        await PutAsync(owner, projectId, notifyTelegram: true);
        var before = _factory.TelegramRequests.Count;

        var response = await attacker.PostAsync($"/api/automation/{projectId}/test-notification", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, _factory.TelegramRequests.Count); // Telegram'a hiçbir şey gitmedi
    }

    [Fact]
    public async Task Tokensiz_test_bildirimi_401_alir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("tg-anon@example.com");
        var projectId = await UploadProjectAsync(owner);
        var before = _factory.TelegramRequests.Count;

        var response = await _factory.CreateClient().PostAsync($"/api/automation/{projectId}/test-notification", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, _factory.TelegramRequests.Count);
    }
}

/// <summary>Telegram bu sunucuda yapılandırılmamışken (varsayılan test fabrikası) davranış.</summary>
public class TelegramNotConfiguredApiTests : IClassFixture<ApiFactory>
{
    private const string OpenApiDocument = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Telegram yok testi", "version": "1.0.0" },
          "paths": { "/items": { "get": { "summary": "Listeler",
            "responses": { "200": { "description": "ok" }, "400": { "description": "hatalı" } } } } }
        }
        """;

    private readonly ApiFactory _factory;

    public TelegramNotConfiguredApiTests(ApiFactory factory) => _factory = factory;

    private async Task<int> UploadProjectAsync(HttpClient owner)
    {
        var upload = await owner.PostAsJsonAsync("/api/project/upload", new { openApiContent = OpenApiDocument });
        return (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("projectId").GetInt32();
    }

    [Fact]
    public async Task Telegram_yapilandirilmamissa_kullanilabilir_degil_gorunur()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("tg-none-get@example.com");
        var projectId = await UploadProjectAsync(owner);

        var settings = await owner.GetFromJsonAsync<JsonElement>($"/api/automation/{projectId}/settings");

        Assert.False(settings.GetProperty("telegramAvailable").GetBoolean());
    }

    [Fact]
    public async Task Telegram_yapilandirilmamissa_acilamaz_400_ve_ayar_degismez()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("tg-none-put@example.com");
        var projectId = await UploadProjectAsync(owner);

        var put = await owner.PutAsJsonAsync($"/api/automation/{projectId}/settings",
            new { scoreThreshold = 60, notifyOnMissingAuth = true, enabled = true, notifyTelegram = true, webhookUrl = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.Contains("yapılandırılmamış", await put.Content.ReadAsStringAsync());
        var settings = await owner.GetFromJsonAsync<JsonElement>($"/api/automation/{projectId}/settings");
        Assert.False(settings.GetProperty("notifyTelegram").GetBoolean());
    }
}

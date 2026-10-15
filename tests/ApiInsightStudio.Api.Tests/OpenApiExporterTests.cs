using System.Text.Json;
using System.Text.Json.Nodes;
using ApiInsightStudio.Api.Services;

namespace ApiInsightStudio.Api.Tests;

/// <summary>Dışa aktarıcı: yalnızca boş açıklamalara yazar, dokümanın geri kalanına dokunmaz, girdiyi değiştirmez.</summary>
public class OpenApiExporterTests
{
    private const string Doc = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Test", "version": "1.2.3", "description": "Bilgi" },
          "servers": [ { "url": "https://api.example.com/v1" } ],
          "paths": {
            "/items": {
              "parameters": [ { "name": "x", "in": "query", "schema": { "type": "string" } } ],
              "x-uzanti": { "responses": {} },
              "get":  { "responses": { "200": { "description": "ok" } } },
              "post": { "summary": "Yazarın kendi açıklaması", "responses": { "201": { "description": "oluştu" } } }
            },
            "/items/{id}": {
              "delete": { "description": "Yalnızca description var", "responses": { "204": { "description": "silindi" } } },
              "put":    { "summary": "   ", "responses": { "200": { "description": "ok" } } }
            }
          },
          "components": { "schemas": { "Item": { "type": "object", "properties": { "id": { "type": "integer", "format": "int64", "maximum": 9007199254740993 } } } } },
          "x-ozel": { "sayi": 1.50, "liste": [1, 2, 3] }
        }
        """;

    private static JsonNode Parse(string json) => JsonNode.Parse(json)!;

    private static (string, string, string)[] Approved(params (string Method, string Path, string Text)[] items) => items;

    [Fact]
    public void Bos_aciklamaya_yazar_isaret_koyar_dolu_olana_dokunmaz()
    {
        var result = OpenApiExporter.Export(Doc, Approved(
            ("GET", "/items", "Ürünleri listeler."),
            ("POST", "/items", "Yapay zekâ metni (yazılmamalı)."),
            ("DELETE", "/items/{id}", "Yapay zekâ metni (yazılmamalı)."),
            ("PUT", "/items/{id}", "Ürünü günceller.")));

        var paths = Parse(result.Json)["paths"]!;
        Assert.Equal("Ürünleri listeler.", (string?)paths["/items"]!["get"]!["summary"]);
        Assert.True((bool)paths["/items"]!["get"]![OpenApiExporter.ApprovedMarker]!);
        Assert.Equal("Ürünü günceller.", (string?)paths["/items/{id}"]!["put"]!["summary"]);   // yalnızca boşluk olan özet boş sayılır

        Assert.Equal("Yazarın kendi açıklaması", (string?)paths["/items"]!["post"]!["summary"]);
        Assert.Null(paths["/items"]!["post"]![OpenApiExporter.ApprovedMarker]);
        Assert.Null(paths["/items/{id}"]!["delete"]!["summary"]);                                // description varsa açıklama var sayılır
        Assert.Null(paths["/items/{id}"]!["delete"]![OpenApiExporter.ApprovedMarker]);

        Assert.Equal(2, result.Filled);
        Assert.Equal(2, result.SkippedExistingSummary);
        Assert.Equal(0, result.NotFoundInDocument);
    }

    [Fact]
    public void Degisen_islemlerin_disindaki_her_sey_aynen_kalir()
    {
        var result = OpenApiExporter.Export(Doc, Approved(("GET", "/items", "Ürünleri listeler.")));

        var exported = Parse(result.Json);
        var original = Parse(Doc);

        // Değişen tek yer: GET /items'ın summary'si ve işareti. Onları kaldırınca iki doküman birebir aynı olmalı.
        exported["paths"]!["/items"]!["get"]!.AsObject().Remove("summary");
        exported["paths"]!["/items"]!["get"]!.AsObject().Remove(OpenApiExporter.ApprovedMarker);
        Assert.True(JsonNode.DeepEquals(original, exported), "Dokümanın geri kalanı değişti.");
    }

    [Fact]
    public void Buyuk_sayilar_ve_ondalikli_degerler_bozulmaz()
    {
        var result = OpenApiExporter.Export(Doc, Approved(("GET", "/items", "Ürünleri listeler.")));

        Assert.Contains("9007199254740993", result.Json);   // double'a çevrilseydi 9007199254740992 olurdu
        Assert.Contains("1.50", result.Json);
    }

    [Fact]
    public void Turkce_karakterler_okunur_yazilir()
    {
        var result = OpenApiExporter.Export(Doc, Approved(("GET", "/items", "Şu ürünleri çeker: ğüşiöç İĞÜŞÖÇ.")));

        Assert.Contains("Şu ürünleri çeker: ğüşiöç İĞÜŞÖÇ.", result.Json);
        Assert.DoesNotContain("\\u", result.Json);
    }

    [Fact]
    public void Girdi_degistirilmez_ve_ayni_girdi_ayni_cikti_verir()
    {
        var copy = string.Copy(Doc);
        var approved = Approved(("GET", "/items", "Ürünleri listeler."));

        var first = OpenApiExporter.Export(Doc, approved);
        var second = OpenApiExporter.Export(Doc, approved);

        Assert.Equal(copy, Doc);
        Assert.Equal(first.Json, second.Json);
    }

    [Fact]
    public void Onaylanmis_aciklama_yoksa_dokuman_anlamca_ayni_kalir()
    {
        var result = OpenApiExporter.Export(Doc, Array.Empty<(string, string, string)>());

        Assert.Equal(0, result.Filled);
        Assert.True(JsonNode.DeepEquals(Parse(Doc), Parse(result.Json)));
    }

    [Theory]
    [InlineData("get")]
    [InlineData("Get")]
    [InlineData("GET")]
    public void Metot_buyuk_kucuk_harf_duyarsiz_eslesir(string method)
    {
        var result = OpenApiExporter.Export(Doc, Approved((method, "/items", "Ürünleri listeler.")));

        Assert.Equal(1, result.Filled);
    }

    [Fact]
    public void Dokumanda_olmayan_islem_ve_yol_sayilir_ama_hata_vermez()
    {
        var result = OpenApiExporter.Export(Doc, Approved(
            ("GET", "/yok", "Metin."),
            ("PATCH", "/items", "Metin."),
            ("GET", "/items/{ID}", "Metin.")));   // yol büyük/küçük harf duyarlıdır

        Assert.Equal(0, result.Filled);
        Assert.Equal(3, result.NotFoundInDocument);
    }

    [Theory]
    [InlineData("parameters")]
    [InlineData("summary")]
    [InlineData("foo")]
    [InlineData("x-uzanti")]   // nesne değerli bir uzantı anahtarı da işlem değildir
    public void Yol_seviyesi_anahtarlar_islem_sayilmaz(string notAMethod)
    {
        var result = OpenApiExporter.Export(Doc, Approved((notAMethod, "/items", "Metin.")));

        Assert.Equal(0, result.Filled);
        Assert.Equal(1, result.NotFoundInDocument);
        Assert.True(JsonNode.DeepEquals(Parse(Doc), Parse(result.Json)));
    }

    [Fact]
    public void Bos_veya_bosluk_metin_yazilmaz()
    {
        var result = OpenApiExporter.Export(Doc, Approved(("GET", "/items", "  "), ("PUT", "/items/{id}", "")));

        Assert.Equal(0, result.Filled);
        Assert.Null(Parse(result.Json)["paths"]!["/items"]!["get"]!["summary"]);
    }

    [Fact]
    public void Metin_kirpilarak_yazilir()
    {
        var result = OpenApiExporter.Export(Doc, Approved(("GET", "/items", "  Ürünleri listeler.  ")));

        Assert.Equal("Ürünleri listeler.", (string?)Parse(result.Json)["paths"]!["/items"]!["get"]!["summary"]);
    }

    [Fact]
    public void Swagger_2_0_dokumani_da_calisir()
    {
        const string swagger = """
            { "swagger": "2.0", "info": { "title": "t", "version": "1" },
              "paths": { "/a": { "get": { "responses": { "200": { "description": "ok" } } } } } }
            """;

        var result = OpenApiExporter.Export(swagger, Approved(("GET", "/a", "A'yı getirir.")));

        Assert.Equal(1, result.Filled);
        Assert.Equal("2.0", (string?)Parse(result.Json)["swagger"]);
    }

    [Fact]
    public void Yolsuz_dokuman_hata_vermeden_degismeden_doner()
    {
        var result = OpenApiExporter.Export("""{ "openapi": "3.0.0", "info": { "title": "t", "version": "1" } }""",
            Approved(("GET", "/a", "Metin.")));

        Assert.Equal(0, result.Filled);
        Assert.Equal(1, result.NotFoundInDocument);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bu json değil")]
    [InlineData("[1,2,3]")]
    [InlineData("\"metin\"")]
    public void Gecersiz_dokuman_JsonException_firlatir(string content)
    {
        Assert.ThrowsAny<JsonException>(() => OpenApiExporter.Export(content, Approved(("GET", "/a", "Metin."))));
    }

    [Fact]
    public void Ozel_karakterli_yollar_dogru_eslesir()
    {
        const string doc = """
            { "openapi": "3.0.0", "info": { "title": "t", "version": "1" },
              "paths": { "/a~b/{id}": { "get": { "responses": { "200": { "description": "ok" } } } },
                         "/c d/e\"f": { "get": { "responses": { "200": { "description": "ok" } } } } } }
            """;

        var result = OpenApiExporter.Export(doc, Approved(("GET", "/a~b/{id}", "Birinci."), ("GET", "/c d/e\"f", "İkinci.")));

        Assert.Equal(2, result.Filled);
    }

    [Fact]
    public void Ayni_islem_iki_kez_verilirse_ikincisi_atlanir_ilki_ezilmez()
    {
        var result = OpenApiExporter.Export(Doc, Approved(("GET", "/items", "İlk."), ("GET", "/items", "İkinci.")));

        Assert.Equal(1, result.Filled);
        Assert.Equal(1, result.SkippedExistingSummary);
        Assert.Equal("İlk.", (string?)Parse(result.Json)["paths"]!["/items"]!["get"]!["summary"]);
    }
}

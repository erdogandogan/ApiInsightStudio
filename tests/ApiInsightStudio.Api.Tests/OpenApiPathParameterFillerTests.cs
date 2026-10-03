using ApiInsightStudio.Api.TestRunner;

namespace ApiInsightStudio.Api.Tests;

/// <summary>Yol parametreleri, OpenAPI'deki tipe uygun "var olmayan kaynak" değerleriyle doldurulur.</summary>
public class OpenApiPathParameterFillerTests
{
    private const string Document = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Doldurucu testi", "version": "1.0.0" },
          "paths": {
            "/items/{id}": {
              "get": {
                "parameters": [ { "name": "id", "in": "path", "required": true, "schema": { "type": "integer" } } ],
                "responses": { "200": { "description": "ok" } }
              }
            },
            "/users/{userId}": {
              "get": {
                "parameters": [ { "name": "userId", "in": "path", "required": true, "schema": { "type": "string", "format": "uuid" } } ],
                "responses": { "200": { "description": "ok" } }
              }
            },
            "/reports/{day}": {
              "get": {
                "parameters": [ { "name": "day", "in": "path", "required": true, "schema": { "type": "string", "format": "date" } } ],
                "responses": { "200": { "description": "ok" } }
              }
            },
            "/events/{at}": {
              "get": {
                "parameters": [ { "name": "at", "in": "path", "required": true, "schema": { "type": "string", "format": "date-time" } } ],
                "responses": { "200": { "description": "ok" } }
              }
            },
            "/flags/{on}": {
              "get": {
                "parameters": [ { "name": "on", "in": "path", "required": true, "schema": { "type": "boolean" } } ],
                "responses": { "200": { "description": "ok" } }
              }
            },
            "/slugs/{slug}": {
              "get": {
                "parameters": [ { "name": "slug", "in": "path", "required": true, "schema": { "type": "string" } } ],
                "responses": { "200": { "description": "ok" } }
              }
            },
            "/prices/{amount}": {
              "get": {
                "parameters": [ { "name": "amount", "in": "path", "required": true, "schema": { "type": "number" } } ],
                "responses": { "200": { "description": "ok" } }
              }
            },
            "/shops/{shopId}/items/{itemId}": {
              "parameters": [ { "name": "shopId", "in": "path", "required": true, "schema": { "type": "string", "format": "uuid" } } ],
              "delete": {
                "parameters": [ { "name": "itemId", "in": "path", "required": true, "schema": { "type": "integer" } } ],
                "responses": { "200": { "description": "ok" } }
              }
            }
          }
        }
        """;

    private static readonly OpenApiPathParameterFiller Filler = OpenApiPathParameterFiller.FromOpenApi(Document);

    [Theory]
    [InlineData("GET", "/items/{id}", "/items/0")]
    [InlineData("GET", "/users/{userId}", "/users/00000000-0000-0000-0000-000000000000")]
    [InlineData("GET", "/reports/{day}", "/reports/1970-01-01")]
    [InlineData("GET", "/flags/{on}", "/flags/false")]
    [InlineData("GET", "/slugs/{slug}", "/slugs/nonexistent")]
    [InlineData("GET", "/prices/{amount}", "/prices/0")]
    public void Parametre_tipine_uygun_sahte_deger_kullanilir(string method, string path, string expected)
    {
        Assert.Equal(expected, Filler.Fill(method, path));
    }

    [Fact]
    public void Date_time_degeri_URL_icin_kacislanir()
    {
        Assert.Equal("/events/1970-01-01T00%3A00%3A00Z", Filler.Fill("GET", "/events/{at}"));
    }

    [Fact]
    public void Yol_duzeyi_ve_islem_duzeyi_parametreler_birlikte_kullanilir()
    {
        Assert.Equal(
            "/shops/00000000-0000-0000-0000-000000000000/items/0",
            Filler.Fill("DELETE", "/shops/{shopId}/items/{itemId}"));
    }

    [Fact]
    public void Metot_buyuk_kucuk_harf_duyarsizdir()
    {
        Assert.Equal("/items/0", Filler.Fill("get", "/items/{id}"));
    }

    [Fact]
    public void Dokumanda_olmayan_yol_veya_parametre_icin_sifir_kullanilir()
    {
        Assert.Equal("/unknown/0", Filler.Fill("GET", "/unknown/{x}"));
        Assert.Equal("/items/0", Filler.Fill("POST", "/items/{id}")); // bu metot dokümanda yok
    }

    [Fact]
    public void Yer_tutucu_olmayan_yol_degismez()
    {
        Assert.Equal("/items", Filler.Fill("GET", "/items"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bu bir openapi degil")]
    [InlineData("{ \"swagger\": ")]
    public void Okunamayan_dokuman_icin_bos_doldurucu_doner_ve_istisna_atmaz(string? content)
    {
        var filler = OpenApiPathParameterFiller.FromOpenApi(content);

        Assert.Equal("/items/0", filler.Fill("GET", "/items/{id}"));
    }

    [Fact]
    public void Bos_doldurucu_tum_parametreleri_sifirla_doldurur()
    {
        Assert.Equal("/a/0/b/0", OpenApiPathParameterFiller.Empty.Fill("GET", "/a/{x}/b/{y}"));
    }
}

using ApiInsightStudio.Api.Services;

namespace ApiInsightStudio.Api.Tests;

/// <summary>AnalysisService puanlama kurallarının mevcut davranışını sabitler.</summary>
public class AnalysisServiceScoringTests
{
    private static async Task<Api.Models.AnalysisResult> AnalyzeAsync(TestDb db, int projectId)
    {
        await using var context = db.CreateContext();
        return await new AnalysisService(context).AnalyzeProjectAsync(projectId);
    }

    [Fact]
    public async Task Temiz_endpoint_tam_puan_alir_ve_uyari_uretmez()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), new EndpointSpec());

        var result = await AnalyzeAsync(db, projectId);

        Assert.Equal(100, result.Score);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Aciklama_eksikse_10_puan_duser(string summary)
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), new EndpointSpec(Summary: summary));

        var result = await AnalyzeAsync(db, projectId);

        Assert.Equal(90, result.Score);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal("Endpoint açıklaması eksik", warning.Message);
        Assert.Equal("Quality", warning.Type);
    }

    [Fact]
    public async Task Hata_kodu_yoksa_15_puan_duser()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(),
            new EndpointSpec(StatusCodesOrNull: new[] { "200", "500" }));

        var result = await AnalyzeAsync(db, projectId);

        Assert.Equal(85, result.Score);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal("Hata durum kodları tanımlanmamış", warning.Message);
        Assert.Equal("Quality", warning.Type);
    }

    [Theory]
    [InlineData("400")]
    [InlineData("401")]
    [InlineData("404")]
    public async Task Bu_hata_kodlarindan_biri_varsa_ceza_yoktur(string statusCode)
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(),
            new EndpointSpec(StatusCodesOrNull: new[] { "200", statusCode }));

        var result = await AnalyzeAsync(db, projectId);

        Assert.Equal(100, result.Score);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("post")]
    public async Task Kritik_metotta_kimlik_dogrulama_yoksa_20_puan_duser(string method)
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), new EndpointSpec(Method: method, AuthType: null));

        var result = await AnalyzeAsync(db, projectId);

        Assert.Equal(80, result.Score);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal("Security", warning.Type);
        Assert.Equal("High", warning.Severity);
    }

    [Fact]
    public async Task GET_metodunda_kimlik_dogrulama_olmamasi_ceza_getirmez()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), new EndpointSpec(Method: "GET", AuthType: null));

        var result = await AnalyzeAsync(db, projectId);

        Assert.Equal(100, result.Score);
    }

    [Fact]
    public async Task DELETE_ve_id_parametresi_15_puan_duser()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(),
            new EndpointSpec(Method: "DELETE", Path: "/items/{id}"));

        var result = await AnalyzeAsync(db, projectId);

        Assert.Equal(85, result.Score);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal("Security", warning.Type);
        Assert.Contains("BOLA", warning.Message);
    }

    [Theory]
    [InlineData("/items/delete")]
    [InlineData("/items/remove")]
    [InlineData("/items/UPDATE")]
    public async Task GET_ile_kalici_islem_10_puan_duser(string path)
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), new EndpointSpec(Method: "GET", Path: path));

        var result = await AnalyzeAsync(db, projectId);

        Assert.Equal(90, result.Score);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal("Security", warning.Type);
        Assert.Equal("Medium", warning.Severity);
    }

    [Fact]
    public async Task Kurallar_birikir()
    {
        using var db = new TestDb();
        // açıklama yok (-10), hata kodu yok (-15), kimlik doğrulama yok (-20), DELETE+{id} (-15)
        var projectId = db.SeedProject(db.SeedUser(), new EndpointSpec(
            Method: "DELETE",
            Path: "/items/{id}",
            Summary: "",
            StatusCodesOrNull: new[] { "200" },
            AuthType: null));

        var result = await AnalyzeAsync(db, projectId);

        Assert.Equal(40, result.Score);
        Assert.Equal(4, result.Warnings.Count);
        Assert.Equal(2, result.Warnings.Count(w => w.Type == "Quality"));
        Assert.Equal(2, result.Warnings.Count(w => w.Type == "Security"));
    }

    [Fact]
    public async Task Puan_sifirin_altina_inmez()
    {
        using var db = new TestDb();
        var bad = new EndpointSpec(
            Method: "DELETE", Path: "/items/{id}", Summary: "",
            StatusCodesOrNull: new[] { "200" }, AuthType: null);
        var projectId = db.SeedProject(db.SeedUser(), bad, bad, bad);

        var result = await AnalyzeAsync(db, projectId);

        Assert.Equal(0, result.Score);
    }

    [Fact]
    public async Task Her_uyari_ilgili_endpointe_baglanir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(),
            new EndpointSpec(Path: "/ok"),
            new EndpointSpec(Path: "/broken", Summary: ""));

        var result = await AnalyzeAsync(db, projectId);

        var warning = Assert.Single(result.Warnings);
        await using var context = db.CreateContext();
        var endpoint = context.Endpoints.Single(e => e.Id == warning.EndpointId);
        Assert.Equal("/broken", endpoint.Path);
    }
}

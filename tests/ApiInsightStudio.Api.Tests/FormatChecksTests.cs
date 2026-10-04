using ApiInsightStudio.Eval;

namespace ApiInsightStudio.Api.Tests;

/// <summary>AI biçim ölçütlerinin kendisi doğrulanır: ölçüt yanlışsa tablodaki sayılar da anlamsızdır.</summary>
public class FormatChecksTests
{
    [Theory]
    [InlineData("GET", "Sistemdeki tüm ürünleri listeler.")]
    [InlineData("POST", "Yeni bir ürün kaydı oluşturur.")]
    [InlineData("PUT", "Mevcut bir ürünü günceller.")]
    [InlineData("PATCH", "Belirli bir ürünün alanlarını kısmen günceller.")]
    [InlineData("DELETE", "Belirtilen ürünü sistemden siler.")]
    public void Iyi_cikti_tum_olculerden_gecer(string method, string output)
    {
        var result = FormatChecks.Evaluate(output, method);

        Assert.True(result.All, $"{method}: {output}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n ")]
    public void Bos_cikti_hicbir_olcuden_gecmez(string? output)
    {
        var result = FormatChecks.Evaluate(output, "GET");

        Assert.False(result.NotEmpty);
        Assert.False(result.All);
    }

    [Fact]
    public void Iki_cumle_tek_cumle_olcusunu_bozar()
    {
        var result = FormatChecks.Evaluate("Ürünleri listeler. Ayrıca filtreleme yapar.", "GET");

        Assert.False(result.SingleSentence);
        Assert.False(result.All);
    }

    [Fact]
    public void Satir_sonu_veya_noktalamasiz_bitis_tek_cumle_olcusunu_bozar()
    {
        Assert.False(FormatChecks.Evaluate("Ürünleri listeler\nve filtreler.", "GET").SingleSentence);
        Assert.False(FormatChecks.Evaluate("Tüm ürünleri listeler", "GET").SingleSentence);
    }

    [Theory]
    [InlineData("Ürünleri listeler: `GET /items` ile çağrılır.")]
    [InlineData("Ürünleri listeler, bkz. https://example.com/docs.")]
    [InlineData("Örneğin ürünleri listeler ve getirir.")]
    [InlineData("GET /items isteğiyle tüm ürünleri listeler.")]
    public void Yasakli_icerik_yakalanir(string output)
    {
        var result = FormatChecks.Evaluate(output, "GET");

        Assert.False(result.NoForbiddenContent, output);
    }

    [Fact]
    public void Yol_parametresinin_cumlede_aynen_yazilmasi_yasakli_sayilmaz()
    {
        var result = FormatChecks.Evaluate("'/items/{id}' uç noktası, belirli bir öğeyi silmek için kullanılır.", "DELETE");

        Assert.True(result.NoForbiddenContent);
        Assert.True(result.All);
    }

    [Theory]
    [InlineData("Belirtilen kimlik numarasına ait kaydı siler: 通过调用带有DELETE方法的端点删除记录。")]
    [InlineData("Belirtilen kaydı siler ve удаляет запись.")]
    public void Latin_disi_yazi_dil_olcusunu_bozar(string output)
    {
        var result = FormatChecks.Evaluate(output, "DELETE");

        Assert.False(result.Turkish);
        Assert.False(result.All);
    }

    [Fact]
    public void Degisiklik_sozcugu_PATCH_icin_tutarli_sayilir()
    {
        Assert.True(FormatChecks.Evaluate("Belirli bir ürünün özelliklerinde küçük değişiklikler yapar.", "PATCH").MethodVerbConsistent);
    }

    [Fact]
    public void Ingilizce_cikti_Turkce_olcusunden_gecmez()
    {
        var result = FormatChecks.Evaluate("Returns the list of all products in the system.", "GET");

        Assert.False(result.Turkish);
        Assert.False(result.All);
    }

    [Fact]
    public void Turkce_harf_icermeyen_ama_Turkce_cumle_gecer()
    {
        // ç ğ ı ö ş ü yok; ama sık Türkçe sözcükler var
        Assert.True(FormatChecks.Evaluate("Bir urunu ve ilgili bilgileri listeler.", "GET").Turkish);
    }

    [Theory]
    [InlineData("Kısa.")]
    public void Cok_kisa_cikti_uzunluk_olcusunu_bozar(string output)
    {
        Assert.False(FormatChecks.Evaluate(output, "GET").LengthOk);
    }

    [Fact]
    public void Cok_uzun_cikti_uzunluk_olcusunu_bozar()
    {
        var output = "Ürünleri listeler ve " + string.Join(" ", Enumerable.Repeat("ayrıntılı", 60)) + " getirir.";

        Assert.False(FormatChecks.Evaluate(output, "GET").LengthOk);
    }

    [Theory]
    [InlineData("DELETE", "Tüm ürünleri listeler ve getirir.")]
    [InlineData("GET", "Belirtilen ürünü sistemden siler.")]
    [InlineData("POST", "Tüm ürünleri sistemden siler.")]
    public void Metotla_celisen_eylem_tutarlilik_olcusunu_bozar(string method, string output)
    {
        Assert.False(FormatChecks.Evaluate(output, method).MethodVerbConsistent, $"{method}: {output}");
    }

    [Fact]
    public void Bilinmeyen_metot_tutarlilik_olcusunden_gecmez()
    {
        Assert.False(FormatChecks.Evaluate("Ürünleri listeler ve getirir.", "TRACE").MethodVerbConsistent);
    }

    [Fact]
    public void Basarisiz_istek_sonucu_hicbir_olcuden_gecmez()
    {
        Assert.False(FormatResult.Failed.All);
        Assert.False(FormatResult.Failed.NotEmpty);
    }
}

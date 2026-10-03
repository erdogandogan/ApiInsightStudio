using ApiInsightStudio.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ApiInsightStudio.Api.Tests;

/// <summary>"Ai" bölümünün gerçek uygulama yapılandırmasından (appsettings.json) AiOptions'a bağlandığını doğrular.</summary>
public class AiConfigurationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AiConfigurationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public void Ai_ayarlari_appsettings_dosyasindan_baglanir()
    {
        using var scope = _factory.Services.CreateScope();

        var options = scope.ServiceProvider.GetRequiredService<IOptions<AiOptions>>().Value;

        Assert.Equal("http://localhost:11434", options.BaseUrl);
        Assert.Equal("qwen2.5:7b", options.Model);
        Assert.True(string.IsNullOrEmpty(options.ApiKey), "ApiKey dosyada tutulmamalı, user-secrets ile verilmeli.");
    }

    [Fact]
    public void AiService_yapilandirmadaki_modeli_kullanir()
    {
        using var scope = _factory.Services.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<AiService>();

        Assert.Equal("qwen2.5:7b", service.Model);
    }
}

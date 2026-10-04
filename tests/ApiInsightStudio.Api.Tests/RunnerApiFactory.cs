using ApiInsightStudio.Api.TestRunner;
using Microsoft.Extensions.DependencyInjection;

namespace ApiInsightStudio.Api.Tests;

/// <summary>
/// Test koşusunun hedef API'sini sahte bir işleyiciyle değiştirir (gerçek ağa çıkılmaz). Cevaplar
/// <see cref="Respond"/> ile değiştirilebilir; atılan istekler <see cref="TargetRequests"/> içinde görülür.
/// </summary>
public sealed class RunnerApiFactory : ApiFactory
{
    private readonly FakeTargetHandler _handler;

    public RunnerApiFactory()
    {
        _handler = new FakeTargetHandler(request => Respond(request));
    }

    /// <summary>Sonraki hedef isteklerine verilecek cevap; varsayılan tipik korumalı API.</summary>
    public Func<RecordedRequest, HttpResponseMessage> Respond { get; set; } = FakeTargetHandler.TypicalApi;

    public IReadOnlyList<RecordedRequest> TargetRequests => _handler.Requests;

    protected override void ConfigureExtraServices(IServiceCollection services)
    {
        services.AddHttpClient(TestRunExecutor.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => _handler);
    }
}

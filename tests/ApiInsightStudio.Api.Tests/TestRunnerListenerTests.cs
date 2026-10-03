using System.Net;
using System.Net.Sockets;
using System.Text;
using ApiInsightStudio.Api.Models;
using ApiInsightStudio.Api.Notifications;

namespace ApiInsightStudio.Api.Tests;

/// <summary>
/// Gerçek bir yerel HTTP sunucusu ve gerçek güvenli HTTP istemcisiyle (SSRF korumalı) test koşusunu uçtan uca doğrular:
/// hedefin gördüğü istekler, token davranışı, engellenen adresler ve yönlendirmenin izlenmemesi.
/// </summary>
public sealed class TestRunnerListenerTests
{
    private const string SecretToken = "tok-yerel-gizli-42";

    private sealed record Seen(string Method, string Path, string? Authorization, string Body);

    /// <summary>127.0.0.1'de rastgele portta dinleyen, gelen istekleri kaydeden küçük hedef API.</summary>
    private sealed class TargetServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Func<Seen, (int Status, string? Location)> _respond;
        private readonly List<Seen> _requests = new();

        public int Port { get; }

        public IReadOnlyList<Seen> Requests
        {
            get { lock (_requests) return _requests.ToList(); }
        }

        public TargetServer(Func<Seen, (int, string?)> respond)
        {
            _respond = respond;
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            _ = Task.Run(LoopAsync);
        }

        private async Task LoopAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch
                {
                    break;
                }

                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var seen = new Seen(
                    context.Request.HttpMethod,
                    context.Request.Url!.AbsolutePath,
                    context.Request.Headers["Authorization"],
                    await reader.ReadToEndAsync());
                lock (_requests)
                    _requests.Add(seen);

                var (status, location) = _respond(seen);
                context.Response.StatusCode = status;
                if (location is not null)
                    context.Response.RedirectLocation = location;
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            _listener.Close();
        }
    }

    /// <summary>Tipik korumalı API'nin gerçek HTTP karşılığı.</summary>
    private static (int, string?) TypicalApi(Seen r)
    {
        if (r.Path == "/items" && r.Method == "GET") return (200, null);
        if (r.Authorization != $"Bearer {SecretToken}") return (401, null);
        if (r.Path == "/items" && r.Method == "POST") return (400, null);
        return (404, null);
    }

    private static async Task<(TestDb Db, int RunId)> ProjectAsync(int port, bool allowMutating, string scheme = "http")
    {
        var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), TestRunFixtures.TypicalEndpoints());
        await TestRunFixtures.GenerateScenariosAsync(db, projectId);
        TestRunFixtures.SetSettings(db, projectId, target: $"{scheme}://127.0.0.1:{port}", token: SecretToken, allowMutating: allowMutating);
        return (db, TestRunFixtures.AddRun(db, projectId));
    }

    private static SocketsHttpHandler SafeHandler(params string[] allowed) => SafeHttp.CreateHandler(allowed);

    [Fact]
    public async Task Tam_kosu_gercek_sunucuya_karsi_beklenen_sonuclari_verir_ve_token_yalnizca_gerektiginde_gider()
    {
        using var server = new TargetServer(TypicalApi);
        var (db, runId) = await ProjectAsync(server.Port, allowMutating: false);
        using (db)
        {
            var run = await TestRunFixtures.ExecuteAsync(db, runId, SafeHandler("127.0.0.1"), allowedPrivateHosts: "127.0.0.1");

            Assert.Equal(TestRun.StatusCompleted, run.Status);
            Assert.Equal(3, run.Passed);
            Assert.Equal(0, run.Failed);
            Assert.Equal(8, run.Skipped);

            // Sunucunun gördüğü istekler: yalnızca GET, mutating yok
            var seen = server.Requests;
            Assert.Equal(3, seen.Count);
            Assert.All(seen, r => Assert.Equal("GET", r.Method));
            Assert.All(seen, r => Assert.Equal(string.Empty, r.Body));

            // Herkese açık uç noktaya token gitmedi; /items/0 için biri tokensiz (401 senaryosu), biri tokenlı (404 senaryosu)
            Assert.Null(seen.Single(r => r.Path == "/items").Authorization);
            var itemRequests = seen.Where(r => r.Path == "/items/0").ToList();
            Assert.Equal(2, itemRequests.Count);
            Assert.Single(itemRequests, r => r.Authorization is null);
            Assert.Single(itemRequests, r => r.Authorization == $"Bearer {SecretToken}");

            // Token sonuçlara ve hata metnine hiçbir yerde yazılmadı
            Assert.All(run.Results, r =>
            {
                Assert.DoesNotContain(SecretToken, r.Reason ?? "");
                Assert.DoesNotContain(SecretToken, r.RequestPath ?? "");
            });
        }
    }

    [Fact]
    public async Task Mutating_acikken_gercek_sunucu_yalnizca_olumsuz_mutating_istekleri_gorur()
    {
        using var server = new TargetServer(TypicalApi);
        var (db, runId) = await ProjectAsync(server.Port, allowMutating: true);
        using (db)
        {
            var run = await TestRunFixtures.ExecuteAsync(db, runId, SafeHandler("127.0.0.1"), allowedPrivateHosts: "127.0.0.1");

            Assert.Equal(7, run.Passed);
            Assert.Equal(0, run.Failed);
            var seen = server.Requests;
            Assert.Equal(7, seen.Count);

            var posts = seen.Where(r => r.Method == "POST").ToList();
            Assert.Equal(2, posts.Count);
            Assert.All(posts, p => Assert.Equal("{}", p.Body)); // boş JSON gövde, başka veri yok
            Assert.Equal(2, seen.Count(r => r.Method == "DELETE" && r.Path == "/items/0"));
            Assert.DoesNotContain(seen, r => r.Method == "DELETE" && r.Path != "/items/0"); // yalnızca sahte id
        }
    }

    [Fact]
    public async Task Izin_listesinde_olmayan_yerel_hedefe_hic_istek_ulasmaz_ve_neden_soylenir()
    {
        using var server = new TargetServer(TypicalApi);
        // https hedef: sözdizimi geçerli, ama 127.0.0.1 izin listesinde değil → bağlantı anında engellenir
        var (db, runId) = await ProjectAsync(server.Port, allowMutating: false, scheme: "https");
        using (db)
        {
            var run = await TestRunFixtures.ExecuteAsync(db, runId, SafeHandler(/* izin listesi boş */));

            Assert.Empty(server.Requests);
            Assert.Equal(0, run.Passed);
            var failed = run.Results.Where(r => r.Outcome == TestRunResult.OutcomeFailed).ToList();
            Assert.NotEmpty(failed);
            Assert.All(failed, r => Assert.Contains("güvenli değil", r.Reason));
            Assert.All(failed, r => Assert.Null(r.ActualStatusCode));
        }
    }

    [Fact]
    public async Task Hedef_yonlendirirse_yonlendirme_izlenmez_ve_koşu_basarisiz_sayar()
    {
        // Hedef, iç ağdaki başka bir adrese yönlendirmeye çalışıyor
        using var server = new TargetServer(_ => (302, "http://169.254.169.254/latest/meta-data/"));
        var (db, runId) = await ProjectAsync(server.Port, allowMutating: false);
        using (db)
        {
            var run = await TestRunFixtures.ExecuteAsync(db, runId, SafeHandler("127.0.0.1"), allowedPrivateHosts: "127.0.0.1");

            Assert.Equal(3, server.Requests.Count); // her senaryo için yalnızca ilk istek; yönlendirme hedefine gidilmedi
            Assert.DoesNotContain(server.Requests, r => r.Path.Contains("meta-data"));
            Assert.Equal(3, run.Failed);
            Assert.All(run.Results.Where(r => r.Outcome == TestRunResult.OutcomeFailed), r => Assert.Equal(302, r.ActualStatusCode));
        }
    }

    [Fact]
    public async Task Hedef_kapaliysa_baglanti_hatasi_raporlanir_ve_adres_hata_metnine_sizmaz()
    {
        int closedPort;
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        closedPort = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var (db, runId) = await ProjectAsync(closedPort, allowMutating: false);
        using (db)
        {
            var run = await TestRunFixtures.ExecuteAsync(db, runId, SafeHandler("127.0.0.1"), allowedPrivateHosts: "127.0.0.1");

            Assert.Equal(TestRun.StatusCompleted, run.Status);
            Assert.Equal(0, run.Passed);
            Assert.All(run.Results.Where(r => r.Outcome == TestRunResult.OutcomeFailed), r =>
            {
                Assert.Contains("Bağlantı hatası", r.Reason);
                Assert.DoesNotContain("127.0.0.1", r.Reason);
                Assert.DoesNotContain(closedPort.ToString(), r.Reason);
            });
        }
    }
}

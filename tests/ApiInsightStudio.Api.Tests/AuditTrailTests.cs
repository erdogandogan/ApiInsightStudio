using ApiInsightStudio.Api.Audit;
using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ApiInsightStudio.Api.Tests;

/// <summary>Denetim izi: zincirleme özet, ekleme sırası ve her türlü kurcalamanın doğrulamada yakalanması.</summary>
public class AuditTrailTests
{
    private static async Task AppendAsync(TestDb db, int projectId, params (string Action, string Details)[] entries)
    {
        await using var context = db.CreateContext();
        var trail = new AuditTrail(context, new TestClock());
        foreach (var (action, details) in entries)
            await trail.AppendAsync(projectId, 1, action, null, details);
        await context.SaveChangesAsync();
    }

    private static async Task<AuditVerification> VerifyAsync(TestDb db, int projectId)
    {
        await using var context = db.CreateContext();
        return await new AuditTrail(context, new TestClock()).VerifyAsync(projectId);
    }

    private static List<AuditLog> Logs(TestDb db, int projectId)
    {
        using var context = db.CreateContext();
        return context.AuditLogs.AsNoTracking().Where(a => a.ProjectId == projectId).OrderBy(a => a.Sequence).ToList();
    }

    private static (TestDb Db, int ProjectId) NewProject()
    {
        var db = new TestDb();
        return (db, db.SeedProject(db.SeedUser(), new EndpointSpec()));
    }

    [Fact]
    public async Task Bos_zincir_gecerlidir()
    {
        var (db, projectId) = NewProject();
        using (db)
        {
            var result = await VerifyAsync(db, projectId);

            Assert.True(result.Valid);
            Assert.Equal(0, result.EntryCount);
            Assert.Null(result.HeadHash);
        }
    }

    [Fact]
    public async Task Kayitlar_siralanir_birbirine_baglanir_ve_ilk_kayit_baslangic_ozetini_tasir()
    {
        var (db, projectId) = NewProject();
        using (db)
        {
            await AppendAsync(db, projectId, ("a.one", "1"), ("a.two", "2"), ("a.three", "3"));

            var logs = Logs(db, projectId);
            Assert.Equal(new[] { 1, 2, 3 }, logs.Select(l => l.Sequence));
            Assert.Equal(AuditLog.GenesisHash, logs[0].PrevHash);
            Assert.Equal(logs[0].Hash, logs[1].PrevHash);
            Assert.Equal(logs[1].Hash, logs[2].PrevHash);
            Assert.All(logs, l => Assert.Matches("^[0-9a-f]{64}$", l.Hash));

            var result = await VerifyAsync(db, projectId);
            Assert.True(result.Valid);
            Assert.Equal(3, result.EntryCount);
            Assert.Equal(logs[2].Hash, result.HeadHash);
        }
    }

    [Fact]
    public async Task Ayri_kaydetmelerde_de_zincir_surer()
    {
        var (db, projectId) = NewProject();
        using (db)
        {
            await AppendAsync(db, projectId, ("a.one", "1"));
            await AppendAsync(db, projectId, ("a.two", "2"));

            var logs = Logs(db, projectId);
            Assert.Equal(new[] { 1, 2 }, logs.Select(l => l.Sequence));
            Assert.Equal(logs[0].Hash, logs[1].PrevHash);
            Assert.True((await VerifyAsync(db, projectId)).Valid);
        }
    }

    [Fact]
    public async Task Projelerin_zincirleri_birbirinden_bagimsizdir()
    {
        using var db = new TestDb();
        var userId = db.SeedUser();
        var first = db.SeedProject(userId, new EndpointSpec());
        var second = db.SeedProject(userId, new EndpointSpec());

        await AppendAsync(db, first, ("a.one", "1"), ("a.two", "2"));
        await AppendAsync(db, second, ("a.one", "1"));

        Assert.Equal(new[] { 1, 2 }, Logs(db, first).Select(l => l.Sequence));
        Assert.Equal(new[] { 1 }, Logs(db, second).Select(l => l.Sequence));
        Assert.Equal(AuditLog.GenesisHash, Logs(db, second)[0].PrevHash);

        // Bir projenin zinciri bozulsa diğeri geçerli kalır
        await using (var context = db.CreateContext())
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE AuditLogs SET Details = 'x' WHERE ProjectId = {first} AND Sequence = 1");
        Assert.False((await VerifyAsync(db, first)).Valid);
        Assert.True((await VerifyAsync(db, second)).Valid);
    }

    [Theory]
    [InlineData("Details")]
    [InlineData("Action")]
    [InlineData("ActorUserId")]
    [InlineData("OccurredAt")]
    [InlineData("Subject")]
    public async Task Bir_kaydin_alanini_degistirmek_dogrulamada_yakalanir(string column)
    {
        var (db, projectId) = NewProject();
        using (db)
        {
            await AppendAsync(db, projectId, ("a.one", "1"), ("a.two", "2"), ("a.three", "3"));

            await using (var context = db.CreateContext())
            {
                var sql = column switch
                {
                    "Details" => "UPDATE AuditLogs SET Details = 'değişti' WHERE Sequence = 2",
                    "Action" => "UPDATE AuditLogs SET Action = 'a.forged' WHERE Sequence = 2",
                    "ActorUserId" => "UPDATE AuditLogs SET ActorUserId = 999 WHERE Sequence = 2",
                    "OccurredAt" => "UPDATE AuditLogs SET OccurredAt = '2020-01-01 00:00:00' WHERE Sequence = 2",
                    _ => "UPDATE AuditLogs SET Subject = 'suggestion:99' WHERE Sequence = 2"
                };
                await context.Database.ExecuteSqlRawAsync(sql);
            }

            var result = await VerifyAsync(db, projectId);

            Assert.False(result.Valid);
            Assert.Equal(2, result.FirstInvalidSequence);
            Assert.Equal(1, result.EntryCount);   // yalnızca ilk kayıt güvenilir
            Assert.Contains("değiştirilmiş", result.Reason);
        }
    }

    [Fact]
    public async Task Ozet_de_yeniden_yazilsa_bir_sonraki_kayit_zinciri_ele_verir()
    {
        var (db, projectId) = NewProject();
        using (db)
        {
            await AppendAsync(db, projectId, ("a.one", "1"), ("a.two", "2"), ("a.three", "3"));

            // Kurcalayan kişi hem içeriği hem o kaydın özetini "düzgünce" yeniden yazıyor
            await using (var context = db.CreateContext())
            {
                var entry = context.AuditLogs.Single(a => a.ProjectId == projectId && a.Sequence == 2);
                entry.Details = "sahte";
                entry.Hash = AuditTrail.ComputeHash(entry);
                await context.SaveChangesAsync();
            }

            var result = await VerifyAsync(db, projectId);

            Assert.False(result.Valid);
            Assert.Equal(3, result.FirstInvalidSequence);
            Assert.Contains("eşleşmiyor", result.Reason);
        }
    }

    [Fact]
    public async Task Ortadan_kayit_silmek_yakalanir()
    {
        var (db, projectId) = NewProject();
        using (db)
        {
            await AppendAsync(db, projectId, ("a.one", "1"), ("a.two", "2"), ("a.three", "3"));
            await using (var context = db.CreateContext())
                await context.Database.ExecuteSqlRawAsync("DELETE FROM AuditLogs WHERE Sequence = 2");

            var result = await VerifyAsync(db, projectId);

            Assert.False(result.Valid);
            Assert.Equal(3, result.FirstInvalidSequence);
            Assert.Contains("boşluk", result.Reason);
        }
    }

    [Fact]
    public async Task Ilk_kaydi_silmek_yakalanir()
    {
        var (db, projectId) = NewProject();
        using (db)
        {
            await AppendAsync(db, projectId, ("a.one", "1"), ("a.two", "2"));
            await using (var context = db.CreateContext())
                await context.Database.ExecuteSqlRawAsync("DELETE FROM AuditLogs WHERE Sequence = 1");

            var result = await VerifyAsync(db, projectId);

            Assert.False(result.Valid);
            Assert.Equal(2, result.FirstInvalidSequence);
        }
    }

    [Fact]
    public async Task Kayitlari_yer_degistirmek_yakalanir()
    {
        var (db, projectId) = NewProject();
        using (db)
        {
            await AppendAsync(db, projectId, ("a.one", "1"), ("a.two", "2"), ("a.three", "3"));

            // 2 ve 3'ün sıra numaralarını takas et (unique indeks için geçici değer)
            await using (var context = db.CreateContext())
            {
                await context.Database.ExecuteSqlRawAsync("UPDATE AuditLogs SET Sequence = 99 WHERE Sequence = 2");
                await context.Database.ExecuteSqlRawAsync("UPDATE AuditLogs SET Sequence = 2 WHERE Sequence = 3");
                await context.Database.ExecuteSqlRawAsync("UPDATE AuditLogs SET Sequence = 3 WHERE Sequence = 99");
            }

            Assert.False((await VerifyAsync(db, projectId)).Valid);
        }
    }

    [Fact]
    public async Task Son_kaydi_silmek_zincir_dogrulamasindan_gecer_ama_son_ozet_degisir()
    {
        // Bilinen sınır: sondaki kayıtların silinmesi tek başına anlaşılamaz. Bu yüzden doğrulama son özeti (HeadHash)
        // döndürür; kullanıcı onu dışarıda saklarsa silinme fark edilir.
        var (db, projectId) = NewProject();
        using (db)
        {
            await AppendAsync(db, projectId, ("a.one", "1"), ("a.two", "2"));
            var before = await VerifyAsync(db, projectId);

            await using (var context = db.CreateContext())
                await context.Database.ExecuteSqlRawAsync("DELETE FROM AuditLogs WHERE Sequence = 2");
            var after = await VerifyAsync(db, projectId);

            Assert.True(after.Valid);
            Assert.Equal(1, after.EntryCount);
            Assert.NotEqual(before.HeadHash, after.HeadHash);
        }
    }

    [Fact]
    public async Task Ayni_proje_ve_sira_numarasiyla_ikinci_kayit_veritabaninda_reddedilir()
    {
        var (db, projectId) = NewProject();
        using (db)
        {
            await AppendAsync(db, projectId, ("a.one", "1"));

            await using var context = db.CreateContext();
            context.AuditLogs.Add(new AuditLog { ProjectId = projectId, Sequence = 1, Action = "a.dup", Details = "", Hash = "x" });

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Uzun_ayrinti_kisaltilir_ve_zincir_gecerli_kalir()
    {
        var (db, projectId) = NewProject();
        using (db)
        {
            await AppendAsync(db, projectId, ("a.long", new string('x', 5000)));

            Assert.Equal(AuditLog.MaxDetailsLength, Logs(db, projectId)[0].Details.Length);
            Assert.True((await VerifyAsync(db, projectId)).Valid);
        }
    }

    [Fact]
    public void Ozet_alan_sinirlarini_kaydirarak_ayni_olmaz()
    {
        AuditLog Entry(string action, string? subject) => new()
        {
            ProjectId = 1, Sequence = 1, Action = action, Subject = subject, Details = "d", PrevHash = AuditLog.GenesisHash,
            OccurredAt = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc)
        };

        Assert.NotEqual(AuditTrail.ComputeHash(Entry("ab", "c")), AuditTrail.ComputeHash(Entry("a", "bc")));
        Assert.NotEqual(AuditTrail.ComputeHash(Entry("a", null)), AuditTrail.ComputeHash(Entry("a", "-")));   // boş ile "-" karışmaz
        Assert.Equal(AuditTrail.ComputeHash(Entry("a", "b")), AuditTrail.ComputeHash(Entry("a", "b")));
    }

    [Fact]
    public void Satir_sonu_iceren_ayrinti_alan_sinirlarini_kaydiramaz()
    {
        AuditLog Entry(string subject, string details) => new()
        {
            ProjectId = 1, Sequence = 1, Action = "a", Subject = subject, Details = details, PrevHash = AuditLog.GenesisHash,
            OccurredAt = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc)
        };

        // Ayırıcı tabanlı bir şemada bu ikisi aynı birleşik metni verirdi
        Assert.NotEqual(AuditTrail.ComputeHash(Entry("s\nx", "d")), AuditTrail.ComputeHash(Entry("s", "x\nd")));
    }

    [Fact]
    public async Task Zaman_saatten_gelir_ve_ozete_dahildir()
    {
        var (db, projectId) = NewProject();
        using (db)
        {
            var clock = new TestClock();
            await using (var context = db.CreateContext())
            {
                var trail = new AuditTrail(context, clock);
                await trail.AppendAsync(projectId, null, "a.one", null, "1");
                await context.SaveChangesAsync();
            }

            var entry = Logs(db, projectId).Single();
            Assert.Equal(clock.GetUtcNow().UtcDateTime, entry.OccurredAt);
            Assert.Null(entry.ActorUserId);   // sistem eylemi
        }
    }
}

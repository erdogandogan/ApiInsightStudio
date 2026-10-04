using System.Text;

namespace ApiInsightStudio.Eval;

public static class Report
{
    public static string Build(IReadOnlyList<ModelRun> runs, DateTime utc, string hardware, int endpointCount, int repeat)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# AI açıklaması biçim değerlendirmesi");
        sb.AppendLine();
        sb.AppendLine("Bu dosya `eval/AiEval` aracıyla üretilir; elle düzenlenmez. Ham çıktılar `ai-outputs.json` içindedir.");
        sb.AppendLine();
        sb.AppendLine($"- Ölçüm zamanı (UTC): {utc:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"- Donanım: {hardware}");
        sb.AppendLine($"- {endpointCount} benzersiz endpoint × {repeat} tekrar = model başına {runs.FirstOrDefault()?.Items.Count ?? 0} örnek (her modelde aynı komut ve aynı uç noktalar; model örnekleme yaptığı için tekrarlar farklı çıkabilir)");
        sb.AppendLine("- Süre: istek başına duvar saati; ilk (ısınma) istek hariç, model belleğe yüklendikten sonra");
        sb.AppendLine();
        sb.AppendLine("**Bu bir BİÇİM ölçümüdür, anlam doğruluğu değil.** Ölçütler, istenen biçime uyumu kontrol eder (aşağıda). \"Yöntemle tutarlı eylem\" ölçütü, açıklamada metoda uygun bir fiilin (GET için listeler/getirir, DELETE için siler, vb.) geçip geçmediğine bakan kaba bir vekil ölçüttür; cümlenin gerçekten doğru olduğunu kanıtlamaz.");
        sb.AppendLine();
        sb.AppendLine("## Sonuç");
        sb.AppendLine();
        sb.AppendLine("| Model | Hepsi geçti | Boş değil | Tek cümle | Yasaklı içerik yok | Türkçe | Uzunluk | Yöntemle tutarlı eylem | İstek hatası | Ort. süre | Medyan | En yavaş |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var run in runs)
        {
            var n = run.Items.Count;
            string P(Func<FormatResult, bool> f) => $"%{run.Items.Count(i => f(i.Checks)) * 100.0 / n:0}";
            var seconds = run.Items.Where(i => i.Error is null).Select(i => i.Seconds).OrderBy(x => x).ToList();
            var avg = seconds.Count == 0 ? double.NaN : seconds.Average();
            var median = seconds.Count == 0 ? double.NaN : seconds[seconds.Count / 2];
            var max = seconds.Count == 0 ? double.NaN : seconds[^1];
            sb.AppendLine($"| {run.Model} | {P(c => c.All)} ({run.Items.Count(i => i.Checks.All)}/{n}) | {P(c => c.NotEmpty)} | {P(c => c.SingleSentence)} | {P(c => c.NoForbiddenContent)} | {P(c => c.Turkish)} | {P(c => c.LengthOk)} | {P(c => c.MethodVerbConsistent)} | {run.Items.Count(i => i.Error is not null)} | {avg:0.0} sn | {median:0.0} sn | {max:0.0} sn |");
        }

        sb.AppendLine();
        sb.AppendLine("## Ölçütler");
        sb.AppendLine();
        sb.AppendLine("- **Boş değil**: kırpılmış cevap boş değil.");
        sb.AppendLine("- **Tek cümle**: satır sonu yok, cümle sınırı yok, noktalama ile bitiyor.");
        sb.AppendLine("- **Yasaklı içerik yok**: komutun yasakladığı şeyler yok (backtick, URL, \"Örneğin\", `GET /...` biçiminde istek satırı). Yol parametresinin (`{id}`) cümlede aynen yazılması yasaklı sayılmaz.");
        sb.AppendLine("- **Türkçe**: Türkçe'ye özgü harf veya sık Türkçe sözcük sayısı, sık İngilizce sözcük sayısından az değil (basit sezgi) ve Latin dışı yazı (Çince, Kiril, Arapça vb.) yok.");
        sb.AppendLine($"- **Uzunluk**: {FormatChecks.MinLength}-{FormatChecks.MaxLength} karakter.");
        sb.AppendLine("- **Yöntemle tutarlı eylem**: metoda uygun bir fiil kökü geçiyor (kaba vekil ölçüt).");
        sb.AppendLine();

        foreach (var run in runs)
        {
            var failed = run.Items.Where(i => !i.Checks.All).ToList();
            sb.AppendLine($"## {run.Model}: ölçütleri geçemeyenler ({failed.Count}/{run.Items.Count})");
            sb.AppendLine();
            if (failed.Count == 0)
                sb.AppendLine("Hepsi geçti.");
            foreach (var item in failed)
            {
                var broken = new List<string>();
                if (item.Error is not null)
                {
                    broken.Add($"istek hatası: {item.Error}");
                }
                else
                {
                    if (!item.Checks.NotEmpty) broken.Add("boş");
                    if (!item.Checks.SingleSentence) broken.Add("tek cümle değil");
                    if (!item.Checks.NoForbiddenContent) broken.Add("yasaklı içerik");
                    if (!item.Checks.Turkish) broken.Add("Türkçe değil");
                    if (!item.Checks.LengthOk) broken.Add("uzunluk");
                    if (!item.Checks.MethodVerbConsistent) broken.Add("yöntemle tutarsız");
                }

                sb.AppendLine($"- `{item.Method} {item.Path}`: {string.Join(", ", broken)}. Çıktı: \"{item.Output?.Replace("\n", " ")}\"");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}

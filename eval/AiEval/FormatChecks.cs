using System.Text.RegularExpressions;

namespace ApiInsightStudio.Eval;

/// <summary>Bir AI açıklamasının biçim ölçütleri. Anlam doğruluğunu DEĞİL, istenen biçime uyumu ölçer.</summary>
public sealed record FormatResult(
    bool NotEmpty, bool SingleSentence, bool NoForbiddenContent, bool Turkish, bool LengthOk, bool MethodVerbConsistent)
{
    public bool All => NotEmpty && SingleSentence && NoForbiddenContent && Turkish && LengthOk && MethodVerbConsistent;

    public static FormatResult Failed { get; } = new(false, false, false, false, false, false);
}

public static class FormatChecks
{
    public const int MinLength = 20;
    public const int MaxLength = 250;

    private static readonly string[] TurkishWords =
    {
        "ve", "bir", "için", "ile", "bu", "olan", "veya", "tüm", "belirli", "mevcut", "yeni", "göre", "kullanıcı",
        "listeler", "getirir", "döndürür", "oluşturur", "siler", "günceller", "ekler", "kaydeder", "sorgular", "alır", "verir"
    };

    private static readonly string[] EnglishWords =
    {
        "the", "a", "an", "is", "are", "to", "of", "for", "and", "this", "that", "returns", "retrieves", "creates",
        "deletes", "updates", "endpoint", "with", "from", "by", "in", "on", "used"
    };

    // Metoda göre beklenen eylem kökleri (küçük harfli, Türkçe kök eşleşmesi)
    private static readonly Dictionary<string, string[]> MethodVerbs = new()
    {
        ["GET"] = new[] { "listele", "getir", "döndür", "görüntüle", "al", "sorgula", "oku", "göster", "bul", "ver", "ulaş", "erişi", "çek", "dön" },
        ["POST"] = new[] { "oluştur", "ekle", "kaydet", "gönder", "başlat", "yarat", "yap", "tetikle", "doğrula", "giriş", "aç", "çalıştır", "kabul", "işle" },
        ["PUT"] = new[] { "güncelle", "değişiklik", "değiştir", "düzenle", "yenile", "ekle", "oluştur", "kaydet", "ayarla" },
        ["PATCH"] = new[] { "güncelle", "değişiklik", "değiştir", "düzenle", "yenile", "kısmen", "ayarla" },
        ["DELETE"] = new[] { "sil", "kaldır", "iptal", "temizle", "yok" }
    };

    private static readonly Regex Sentences = new(@"(?<=[.!?])\s+(?=\p{L})", RegexOptions.Compiled);
    private static readonly Regex Words = new(@"[\p{L}]+", RegexOptions.Compiled);
    // Latin dışı yazı sistemleri (Kiril, İbranice/Arapça, Japonca/Çince/Kore): modelin dil kayması
    private static readonly Regex NonLatinScript = new(@"[Ѐ-ӿ֐-ۿ぀-ヿ㐀-鿿가-힯]", RegexOptions.Compiled);
    private static readonly Regex HttpRequestLine = new(@"(^|\s)(GET|POST|PUT|PATCH|DELETE)\s+/", RegexOptions.Compiled);

    public static FormatResult Evaluate(string? output, string method)
    {
        var text = output?.Trim() ?? string.Empty;
        if (text.Length == 0)
            return FormatResult.Failed;

        var lower = text.ToLower(new System.Globalization.CultureInfo("tr-TR"));

        var singleSentence = !text.Contains('\n') && Sentences.Split(text).Length == 1 && ".!?".Contains(text[^1]);

        var forbidden = text.Contains('`')
                        || lower.Contains("http://") || lower.Contains("https://")
                        || lower.Contains("örneğin") || lower.StartsWith("örnek")
                        || HttpRequestLine.IsMatch(text);

        return new FormatResult(
            NotEmpty: true,
            SingleSentence: singleSentence,
            NoForbiddenContent: !forbidden,
            Turkish: LooksTurkish(lower) && !NonLatinScript.IsMatch(text),
            LengthOk: text.Length is >= MinLength and <= MaxLength,
            MethodVerbConsistent: MethodVerbs.TryGetValue(method.ToUpperInvariant(), out var stems)
                                  && stems.Any(stem => lower.Contains(stem)));
    }

    /// <summary>Basit sezgi: Türkçe'ye özgü harf veya sık Türkçe sözcük sayısı, sık İngilizce sözcük sayısından az olmamalı.</summary>
    public static bool LooksTurkish(string lower)
    {
        var words = Words.Matches(lower).Select(m => m.Value).ToList();
        var turkish = words.Count(w => TurkishWords.Contains(w) || w.Any(c => "çğışöü".Contains(c)));
        var english = words.Count(w => EnglishWords.Contains(w));
        return turkish >= 1 && turkish >= english;
    }
}

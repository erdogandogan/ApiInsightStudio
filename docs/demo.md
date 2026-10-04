# 60 saniyelik demo senaryosu

Amaç: tek bir akışta "yükle → otomatik uyarı → AI taslağı → insan onayı → denetim izi" zincirini göstermek. Her adımın yanında yaklaşık süre ve söylenecek tek cümle var.

## Hazırlık (kayıttan önce, süreye dahil değil)

- API (`http://localhost:5037`) ve web (`http://localhost:3000`) çalışıyor, Ollama açık (`qwen2.5:7b` yüklü ve ısınmış: bir kez deneme isteği atın).
- Telegram veya webhook ayarlı (tercihen Telegram: telefonda bildirimi göstermek etkileyici). `user-secrets` içinde, ekranda **gösterilmez**.
- Örnek doküman: kasten kusurlu, `POST` ve `DELETE /items/{id}` içeren, açıklaması ve hata kodları eksik küçük bir OpenAPI dosyası (örn. `eval/rules/cases/26-karma-coklu.json` içindeki `document` alanı).
- Tarayıcıda giriş yapılmış, panel açık. Başka sekme/terminalde token, anahtar veya `appsettings` açık değil.

## Akış

| Süre | Ekranda | Söylenecek |
|---|---|---|
| 0:00-0:08 | Panelde dosyayı yükle | "Bir OpenAPI dokümanı yüklüyorum. Sistem kural motoruyla kalite ve güvenlik skorunu hesaplıyor." |
| 0:08-0:18 | Rapor sayfası: düşük skor, güvenlik uyarıları (kimlik eksik, ID'li silme) | "Skor deterministik kurallardan geliyor, yapay zekâdan değil. Kuralların doğruluğunu 29 örnekle ölçtüm." |
| 0:18-0:25 | Telefonda/Telegram'da gelen bildirim | "Skor eşiğin altına düştüğü için uyarı açıldı ve imzalı bildirim gitti. Aynı uyarı tekrar tekrar gelmiyor: kenar tetiklemeli." |
| 0:25-0:35 | "AI ile Açıklama Üret" → "AI Önerileri" bölümünde taslak | "Eksik açıklamalar için yerel model taslak yazıyor. Bu taslak yayımlanmadı: model adı ve komut sürümüyle birlikte onay bekliyor." |
| 0:35-0:47 | "Düzenleyip onayla": metni düzelt, onayla. Skor aynı kalır | "Bir insan düzeltip onaylıyor. Yalnızca onaylanan metin yayımlanıyor ve kalite skoru değişmiyor; skor yalnızca yüklenen dokümanı yansıtıyor." |
| 0:47-0:58 | Denetim İzi sayfası → "Zinciri doğrula" → yeşil sonuç | "Kim ne zaman ne yaptı kayıtlı; her kayıt öncekinin özetini taşıyor. Biri veritabanında bir satırı değiştirirse doğrulama bozulduğu yeri gösterir. Token ve tam adres hiç yazılmaz." |
| 0:58-1:00 | README'deki değerlendirme tablosu | "Ölçtüğüm sayılar ve bilinen sınırlar README'de." |

## Gösterilmeyecekler

Token, `user-secrets`, Telegram bot adresi (adresin içinde token var), `appsettings` dosyaları, webhook sırrı (yalnızca ilk üretildiği cevapta görünür, kayda girmesin).

## Sık sorulacak sorular için kısa cevaplar

- **"AI burada ne yapıyor?"** Yalnızca eksik açıklama taslağı yazıyor; karar vermiyor, hiçbir şeyi kendi başına yayımlamıyor.
- **"Neden kural motoru yapay zekâ değil?"** Skor tekrarlanabilir ve açıklanabilir olmalı. Aynı doküman her seferinde aynı skoru alır.
- **"Denetim izi gerçekten değiştirilemez mi?"** Değiştirme, silme ve araya ekleme zincir doğrulamasında yakalanır. Veritabanına tam yetkisi olan biri zinciri baştan hesaplayabilir ve en sondaki kayıtların silinmesi tek başına anlaşılmaz; bunun için son özet dışarıda saklanabilir. Sınır README'de yazılı.
- **"Tek kullanıcılı sistemde 'onay' ne anlama geliyor?"** Dört göz ilkesi değil, insan onayının kaydı. Onaylayan kimliği saklanıyor, rol ayrımı eklenirse hazır.
- **"Model yavaş mı?"** Ölçülen süreler README'deki tabloda; 4 GB ekran kartında 7B model kısmen işlemciye taşar.

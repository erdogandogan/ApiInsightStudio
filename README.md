# ApiInsightStudio

OpenAPI/Swagger dokümanı yükleyip kalite ve güvenlik skoru, uyarılar ve otomatik test senaryoları üreten bir araç.

- `api/` — ASP.NET Core 8 Web API (EF Core, SQL Server, JWT)
- `web/` — Next.js frontend (isteğe bağlı Electron masaüstü sürümü)

## Kurulum

### API
1. SQL Server Express çalışıyor olmalı (`localhost\SQLEXPRESS`). Bağlantı dizesi `api/appsettings.json` içinde.
2. JWT anahtarı repoda **tutulmaz**. Kendi anahtarınızı `dotnet user-secrets` ile verin (`api/appsettings.example.json` yapıyı gösterir):
   ```bash
   cd api
   dotnet user-secrets set "Jwt:Key" "<en az 32 karakterlik rastgele değer>"
   ```
   Üretimde ortam değişkeni kullanın: `Jwt__Key`.
3. Veritabanını oluşturun ve çalıştırın:
   ```bash
   dotnet ef database update
   dotnet run --urls http://localhost:5037
   ```
   Swagger: http://localhost:5037/swagger

### Web
```bash
cd web
cp .env.example .env.local   # NEXT_PUBLIC_API_URL=http://localhost:5037/api
npm install
npm run dev                  # http://localhost:3000
```

### AI açıklaması (isteğe bağlı)
"AI ile açıklama üret" özelliği, OpenAI uyumlu bir sohbet uç noktası (`/v1/chat/completions`) sunan yerel bir sağlayıcı ister. Varsayılan [Ollama](https://ollama.com)'dır: `ollama pull qwen2.5:7b`. AI servisi yoksa bu özellik hata verir, diğer her şey çalışır.

Sağlayıcı ve model kod değişmeden, `api/appsettings.json` içindeki `Ai` bölümünden (veya `Ai__BaseUrl`, `Ai__Model` ortam değişkenlerinden) değiştirilir:

| Sağlayıcı | `Ai:BaseUrl` |
|---|---|
| Ollama (varsayılan) | `http://localhost:11434` |
| LM Studio | `http://localhost:1234` |
| llama.cpp server | `http://localhost:8080` |

`BaseUrl` "/v1" olmadan yazılır. Anahtar isteyen bir sağlayıcı için `Ai:ApiKey` yalnızca `dotnet user-secrets` ile verilir, dosyaya yazılmaz. Üretilen açıklama `Endpoint.AiSummary` alanına kaydedilir, kalite skorunu etkilemez.

### Uyarılar ve bildirimler (webhook)
Bir analiz bitince kurallar çalışır ve yeni bir uyarı açılırsa (skor eşiğin altında, kritik uç noktada kimlik doğrulama eksik) projeye tanımlı kanallara bildirim gider. Uyarılar kenar tetiklemelidir: aynı kural için Açık uyarı varken yenisi açılmaz, kural düzelince uyarı kendiliğinden Çözüldü olur.

| Uç nokta | Ne yapar |
|---|---|
| `GET/PUT /api/automation/{projectId}/settings` | Eşik, kural ve webhook adresi ayarları. PUT tam değiştirmedir (boş `webhookUrl` webhook'u kapatır) |
| `POST /api/automation/{projectId}/webhook/secret` | İmza sırrını yeniler |
| `GET /api/automation/{projectId}/alerts` | Uyarılar (`?status=Open\|Resolved`) |
| `GET /api/automation/{projectId}/deliveries` | Bildirim teslimatları (`?status=Pending\|Succeeded\|Dead`) |

**Webhook:** Adres ilk kez verildiğinde sunucu bir imza sırrı (`whsec_...`) üretir ve **yalnızca o cevapta bir kez** gösterir; veritabanında şifreli (Data Protection) saklanır, bir daha okunamaz. İstek, `POST` ile JSON gövde taşır ve şu başlıkları içerir:

- `X-Event-Id`: olayın benzersiz kimliği (alıcı tekrarı ayıklayabilir)
- `X-Timestamp`: Unix saniyesi
- `X-Signature`: `sha256=` + `HMAC-SHA256(sır, "{X-Timestamp}.{ham gövde}")` (küçük harfli onaltılık)

Alıcı, kendi sırrıyla aynı imzayı hesaplayıp karşılaştırmalı ve `X-Timestamp` çok eskiyse isteği reddetmelidir (tekrar saldırısı). 2xx dışındaki cevaplar, yönlendirmeler ve 5 saniyeyi aşan cevaplar başarısızdır.

**Yeniden deneme:** Başarısız teslimat 10, 20, 40, 80 saniye arayla yeniden denenir; 5. başarısızlıkta `Dead` olur. Deneme bilgisi veritabanında tutulur, uygulama yeniden başlasa da kaybolmaz. Arka plan işçisi (`Notifications:WorkerEnabled`, `Notifications:PollSeconds`) bunları işler.

**Güvenlik (SSRF):** Webhook adresi yalnızca `https` olabilir; kullanıcı adı/parola içeremez. Loopback, özel ağ (10/8, 172.16/12, 192.168/16), link-local (bulut metadata `169.254.169.254` dahil), CGNAT ve ayrılmış adreslere (IPv6 ve IPv4-gömülü biçimler dahil) bildirim gönderilmez. Kontrol hem kayıt anında hem de bağlantının kurulduğu anda, bağlanılacak IP üzerinde yapılır (DNS rebinding'e karşı); yönlendirmeler izlenmez, proxy kullanılmaz. Yalnızca geliştirme için, `Notifications:AllowedPrivateHosts` listesine yazılan adlar bu kuraldan muaf tutulur (bu adlar için düz `http` de kabul edilir); üretimde boş bırakın.

**Tek sunucu varsayımı:** Çakışmayı önlemek için olay ve teslimat işleme süreç içi kilitle serileştirilir. Birden fazla sunucuya ölçeklenirse veritabanı düzeyinde bir kilit gerekir.

#### Telegram kanalı
Uyarılar, sunucuda yapılandırılmış bir Telegram botu aracılığıyla bir sohbete de gönderilebilir. Bot token'ı ve sohbet kimliği **gizli bilgidir**: yalnızca kendi terminalinizde `dotnet user-secrets` ile verilir, dosyaya ve repoya yazılmaz.

1. Telegram'da `@BotFather`'a `/newbot` yazıp botu oluşturun; verdiği token'ı (`123456789:AAH...`) not alın.
2. Kendi botunuza gidip `/start` yazın (bot, siz yazmadan size mesaj atamaz).
3. Tarayıcıda `https://api.telegram.org/bot<TOKEN>/getUpdates` adresini açıp `"chat":{"id":...}` içindeki sayıyı alın (bu adres token içerir, paylaşmayın).
4. Terminalde:
   ```bash
   cd api
   dotnet user-secrets set "Telegram:BotToken" "<TOKEN>"
   dotnet user-secrets set "Telegram:ChatId" "<CHAT_ID>"
   ```

Sonra projede `PUT /api/automation/{projectId}/settings` ile `notifyTelegram: true` verin (Telegram sunucuda yapılandırılmamışsa 400 döner; ayarlar cevabındaki `telegramAvailable` bunu gösterir). `POST /api/automation/{projectId}/test-notification` gerçek bir uyarı kaydı oluşturmadan, "TEST" kuralıyla yapılandırılmış kanallara bir bildirim gönderir ve her kanalın sonucunu hemen döndürür.

Güvenlik notları: Token Telegram'ın istek adresinde yer aldığı için `HttpClient`'ın varsayılan günlükleri bu istemci için kapatılmıştır (aksi halde adres, yani token, günlüğe yazılırdı); hata metinleri ve veritabanı kayıtları token taşımaz. Token ve sohbet kimliği biçimi gönderilmeden önce doğrulanır. Mesajlar düz metindir (HTML/Markdown işlenmez). Telegram 400/401/403/404 döndürürse (yanlış token, sohbet bulunamadı, bot engellenmiş) teslimat yeniden denenmeden hemen `Dead` olur; 429 ve 5xx geçici hata sayılıp geri çekilmeyle yeniden denenir.

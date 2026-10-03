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

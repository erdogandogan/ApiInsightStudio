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
"AI ile açıklama üret" özelliği yerel [Ollama](https://ollama.com) ister: `ollama pull qwen2.5:7b`. Ollama yoksa bu özellik hata verir, diğer her şey çalışır.

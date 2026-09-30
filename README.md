# ApiInsightStudio

- `api/` — ASP.NET Core Web API (.NET, EF Core, SQL Server)
- `web/` — Next.js frontend

## Kurulum
- **API:** `api/appsettings.json` içindeki `Jwt:Key` ve `GeminiApiKey` değerlerini kendi değerlerinizle değiştirin (gerçek anahtarları commit etmeyin; `dotnet user-secrets` kullanabilirsiniz). Sonra `cd api && dotnet run`.
- **Web:** `web/.env.example` dosyasını `.env.local` olarak kopyalayıp API adresini ayarlayın. Sonra `cd web && npm install && npm run dev`.

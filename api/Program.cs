using System.Text;
using ApiInsightStudio.Api.Automation;
using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Events;
using ApiInsightStudio.Api.Notifications;
using ApiInsightStudio.Api.Services;
using ApiInsightStudio.Api.TestRunner;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ApiInsightStudio API",
        Version = "1.0.0"
    });
    options.CustomSchemaIds(type => type.FullName?.Replace("+", ".") ?? type.Name);
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "JWT token degerini Bearer olmadan buraya yapistirin."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new List<string>()
        }
    });
});
builder.Services.AddControllers();
builder.Services.AddScoped<AlertRuleEvaluator>();
builder.Services.AddScoped<IEventPublisher, OutboxEventPublisher>();
builder.Services.AddScoped<IEventHandler<AnalysisCompleted>, AnalysisCompletedHandler>();
builder.Services.AddScoped<OutboxDispatcher>();

// Bildirimler: olay işleyici (teslimat satırı açar), kanallar, teslimat işleyici ve arka plan işçisi
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection(NotificationOptions.SectionName));
builder.Services.AddDataProtection().SetApplicationName("ApiInsightStudio");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<WebhookSecretProtector>();
builder.Services.AddHttpClient(WebhookChannel.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(5))
    .ConfigurePrimaryHttpMessageHandler(sp =>
        SafeHttp.CreateHandler(sp.GetRequiredService<IOptions<NotificationOptions>>().Value.AllowedPrivateHosts));
builder.Services.AddScoped<INotificationChannel, WebhookChannel>();
builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection(TelegramOptions.SectionName));
builder.Services.AddTelegramChannel();
builder.Services.AddScoped<IEventHandler<AlertRaised>, AlertRaisedHandler>();
builder.Services.AddScoped<DeliveryProcessor>();

// Otomatik test koşusu: hedef API'ye güvenli (SSRF korumalı, yönlendirmesiz) istemciyle istek atar
builder.Services.AddHttpClient(TestRunExecutor.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
    .ConfigurePrimaryHttpMessageHandler(sp =>
        SafeHttp.CreateHandler(sp.GetRequiredService<IOptions<NotificationOptions>>().Value.AllowedPrivateHosts));
builder.Services.AddScoped<TestRunExecutor>();
builder.Services.AddScoped<TestRunProcessor>();
builder.Services.AddScoped<IEventHandler<TestRunCompleted>, TestRunCompletedHandler>();

if (builder.Configuration.GetValue(
        $"{NotificationOptions.SectionName}:{nameof(NotificationOptions.WorkerEnabled)}", true))
{
    builder.Services.AddHostedService<AutomationWorker>();
    builder.Services.AddHostedService<TestRunWorker>();
}

builder.Services.AddScoped<AnalysisService>();
builder.Services.AddScoped<TestGenerationService>();
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));
builder.Services.AddHttpClient<AiService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
});
var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowNextjs", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "app://localhost")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger(options =>
    {
        options.SerializeAsV2 = true;
    });
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.SwaggerEndpoint("v1/swagger.json", "ApiInsightStudio API v1");
    });
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseRouting();
app.UseCors("AllowNextjs");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// WebApplicationFactory<Program> ile entegrasyon testleri için gerekli.
public partial class Program { }

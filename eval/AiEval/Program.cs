using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ApiInsightStudio.Api.Services;
using ApiInsightStudio.Eval;
using Microsoft.Extensions.Options;

// Kullanım: dotnet run --project eval/AiEval -c Release -- --models qwen2.5:7b,qwen2.5:3b [--base-url http://localhost:11434]
//           [--max 30] [--repeat 3] [--out eval/results] [--hardware "..."]
var options = ParseArgs(args);
var models = options.GetValueOrDefault("models", "qwen2.5:7b").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var baseUrl = options.GetValueOrDefault("base-url", "http://localhost:11434");
var max = int.Parse(options.GetValueOrDefault("max", "30"));
var repeat = int.Parse(options.GetValueOrDefault("repeat", "1"));
var hardware = options.GetValueOrDefault("hardware", "belirtilmedi");

var root = FindRepoRoot();
var outDir = Path.Combine(root, options.GetValueOrDefault("out", Path.Combine("eval", "results")));
Directory.CreateDirectory(outDir);

var endpoints = LoadEndpoints(Path.Combine(root, "eval", "rules", "cases")).Take(max).ToList();
Console.WriteLine($"{endpoints.Count} endpoint, modeller: {string.Join(", ", models)}, sağlayıcı: {baseUrl}");

var allRuns = new List<ModelRun>();
foreach (var model in models)
{
    var service = new AiService(
        new HttpClient { Timeout = TimeSpan.FromMinutes(5) },
        Options.Create(new AiOptions { BaseUrl = baseUrl, Model = model }));

    // Isınma: ilk istek modeli belleğe yükler; süre istatistiğine katılmaz
    Console.Write($"[{model}] ısınma... ");
    var warm = Stopwatch.StartNew();
    try
    {
        await service.GenerateDescriptionAsync("GET", "/warmup");
        Console.WriteLine($"{warm.Elapsed.TotalSeconds:0.0} sn");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"HATA: {ex.GetType().Name}");
    }

    var items = new List<Item>();
    foreach (var (method, path) in endpoints.SelectMany(e => Enumerable.Repeat(e, repeat)))
    {
        var watch = Stopwatch.StartNew();
        string? output = null;
        string? error = null;
        try
        {
            output = await service.GenerateDescriptionAsync(method, path);
        }
        catch (Exception ex)
        {
            error = ex is HttpRequestException hre ? $"HTTP {(int?)hre.StatusCode}" : ex.GetType().Name;
        }

        watch.Stop();

        var result = error is null ? FormatChecks.Evaluate(output, method) : FormatResult.Failed;
        items.Add(new Item(method, path, output, error, Math.Round(watch.Elapsed.TotalSeconds, 2), result));
        Console.WriteLine($"[{model}] {method,-6} {path,-32} {watch.Elapsed.TotalSeconds,6:0.0} sn  {(result.All ? "OK " : "HATA")}  {output?.Replace('\n', ' ')}");
    }

    allRuns.Add(new ModelRun(model, items));
}

var utc = DateTime.UtcNow;
File.WriteAllText(Path.Combine(outDir, "ai-outputs.json"),
    JsonSerializer.Serialize(new { generatedUtc = utc, baseUrl, hardware, runs = allRuns },
        new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n",
    new UTF8Encoding(false));
File.WriteAllText(Path.Combine(outDir, "ai-format.md"), Report.Build(allRuns, utc, hardware, endpoints.Count, repeat), new UTF8Encoding(false));
Console.WriteLine($"Yazıldı: {Path.Combine(outDir, "ai-format.md")}");
return 0;

static Dictionary<string, string> ParseArgs(string[] args)
{
    var result = new Dictionary<string, string>();
    for (var i = 0; i + 1 < args.Length; i += 2)
        result[args[i].TrimStart('-')] = args[i + 1];
    return result;
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ApiInsightStudio.sln")))
        dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı; depo içinden çalıştırın.");
}

static IEnumerable<(string Method, string Path)> LoadEndpoints(string casesDir)
{
    var seen = new HashSet<string>();
    foreach (var file in Directory.GetFiles(casesDir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        if (!doc.RootElement.GetProperty("document").TryGetProperty("paths", out var paths))
            continue;
        foreach (var path in paths.EnumerateObject())
            foreach (var op in path.Value.EnumerateObject())
                if (seen.Add($"{op.Name.ToUpperInvariant()} {path.Name}"))
                    yield return (op.Name.ToUpperInvariant(), path.Name);
    }
}

public sealed record Item(string Method, string Path, string? Output, string? Error, double Seconds, FormatResult Checks);

public sealed record ModelRun(string Model, List<Item> Items);

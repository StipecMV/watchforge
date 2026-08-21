using System.Text.Json;
using Microsoft.AspNetCore.Cors.Infrastructure;
using WatchForge.Api;
using WatchForge.Interfaces.Library;
using WatchForge.Processing.Library;

// WatchForge API — REST kontajner 2 (S5-1 scaffold: ASP.NET Core MVC + CORS + error handling).
// Kontrakt: /api/v1/* (FR-09, architektúra §7); agent-friendly JSON v camelCase;
// chyby ako RFC 9457 ProblemDetails (500 výnimka, 404 neznáma cesta, 400 validácia).
// Konfigurácia: sekcia "WatchForge:Api" (appsettings.json / WatchForge__Api__* env vars).
//
// Poznámka: tento Program.cs nahrádza legacy monolitický server (/api/videos + ffmpeg
// konverzie) — legacy volal len legacy Angular UI (applications/web/WatchForge.UI),
// ktorý sa v S6 prepíše na nový /api/v1 kontrakt. Stará verzia je v git histórii.

var builder = WebApplication.CreateBuilder(args);

// Options pattern cez DI (lazy): IOptions<ApiOptions> sa vyhodnotí z finálnej
// konfigurácie pri prvom použití. Pozn.: WebApplicationFactory override config
// cez ConfigureAppConfiguration/UseSetting NEFUNGUJE pre minimal hosting
// (factory ich aplikuje do host configu, nie do app configu) — testy používajú
// env vars (CreateBuilder ich pridáva do app configu pri štarte).
builder.Services.AddOptions<ApiOptions>()
    .Bind(builder.Configuration.GetSection(ApiOptions.SectionName))
    .Validate(o => { o.Validate(); return true; })
    .ValidateOnStart();

// Error handling: RFC 9457 ProblemDetails pre neočakávané výnimky (+ traceId pre debug).
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
    };
});

// MVC + JSON: camelCase (frontend konvencia, konzistentné s Contracts DTO).
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    });

// CORS: explicitná policy pre web UI — nie AllowAnyOrigin (session cookie si vyžaduje
// AllowCredentials, ktoré s wildcard originom nejde skombinovať; NFR-05).
// Policy sa stavia LAZY cez ApiCorsPolicyProvider z live IConfiguration (sekcia
// WatchForge:Api) — AddPolicy(name, Action) by sa evaluoval eager počas registrácie.
builder.Services.AddSingleton<ICorsPolicyProvider, ApiCorsPolicyProvider>();

// DataProtection — podpisovanie session cookie (S5-2).
builder.Services.AddDataProtection();

// SQLite + repozitáre (lazy singleton — DbPath sa vyhodnotí z finálnej konfigurácie,
// testy ho môžu override cez env var pred prvým requestom).
builder.Services.AddSingleton(provider =>
{
    var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiOptions>>().Value;
    return WatchForgeDatabase.OpenAsync(options.DbPath).GetAwaiter().GetResult();
});
builder.Services.AddSingleton<IUserRepository>(provider =>
    new UserRepository(provider.GetRequiredService<Microsoft.Data.Sqlite.SqliteConnection>()));
builder.Services.AddSingleton<IJobRepository>(provider =>
    new JobRepository(provider.GetRequiredService<Microsoft.Data.Sqlite.SqliteConnection>()));
builder.Services.AddSingleton<IRecordingRepository>(provider =>
    new RecordingRepository(provider.GetRequiredService<Microsoft.Data.Sqlite.SqliteConnection>()));
builder.Services.AddSingleton<IDetectionRepository>(provider =>
    new DetectionRepository(provider.GetRequiredService<Microsoft.Data.Sqlite.SqliteConnection>()));
builder.Services.AddSingleton<ICameraRepository>(provider =>
    new CameraRepository(provider.GetRequiredService<Microsoft.Data.Sqlite.SqliteConnection>()));
builder.Services.AddSingleton<INvrRepository>(provider =>
    new NvrRepository(provider.GetRequiredService<Microsoft.Data.Sqlite.SqliteConnection>()));
builder.Services.AddSingleton<IClipRepository>(provider =>
    new ClipRepository(provider.GetRequiredService<Microsoft.Data.Sqlite.SqliteConnection>()));
builder.Services.AddSingleton<IRequestRepository>(provider =>
    new RequestRepository(provider.GetRequiredService<Microsoft.Data.Sqlite.SqliteConnection>()));
builder.Services.AddSingleton<IPersistRepository>(provider =>
    new PersistRepository(provider.GetRequiredService<Microsoft.Data.Sqlite.SqliteConnection>()));
builder.Services.AddSingleton<IAnnotationRepository>(provider =>
    new AnnotationRepository(provider.GetRequiredService<Microsoft.Data.Sqlite.SqliteConnection>()));
builder.Services.AddSingleton<IConfigVersionRepository>(provider =>
    new ConfigVersionRepository(provider.GetRequiredService<Microsoft.Data.Sqlite.SqliteConnection>()));
builder.Services.AddSingleton<IIdentityRepository>(provider =>
    new IdentityRepository(provider.GetRequiredService<Microsoft.Data.Sqlite.SqliteConnection>()));
builder.Services.AddSingleton<IFaceRepository>(provider =>
    new FaceRepository(provider.GetRequiredService<Microsoft.Data.Sqlite.SqliteConnection>()));
builder.Services.AddSingleton<WatchForge.MotionSentinel.Library.Detection.IFaceRecognizer>(provider =>
{
    // S19: ML pipeline (YuNet + SFace) namiesto LBPH — FR-05 povoľuje ML model
    var recognizer = new WatchForge.MotionSentinel.Library.Detection.OnnxFaceRecognizer
    {
        FaceRepository = provider.GetRequiredService<IFaceRepository>(),
    };
    return recognizer;
});
builder.Services.AddSingleton<WatchForge.Interfaces.Library.IClock, WatchForge.Interfaces.Library.SystemClock>();

// S19: live frame cache — persistentný OPMonitor stream na kameru (rýchle /frame snapshoty)
builder.Services.AddSingleton<LiveFrameCache>();

var app = builder.Build();

// 1. Neočakávané výnimky → 500 ProblemDetails (musí byť pred routingom).
app.UseExceptionHandler();

// 2. CORS middleware pred endpointmi.
app.UseCors(ApiOptions.DefaultCorsPolicyName);

// 3. Endpointy (MVC).
app.MapControllers();

// 4. Neznáme cesty → 404 ProblemDetails (API-only, žiadny SPA fallback).
app.MapFallback(() => Results.Problem(statusCode: StatusCodes.Status404NotFound));

app.Run();

// Entry point marker pre WebApplicationFactory (integračné testy — S5-8).
// Namiesto public partial class Program — Runner má vlastný top-level Program,
// globálny typ "Program" by bol v testoch ambiguous.
namespace WatchForge.Api
{
    public sealed class ApiEntryPoint { }
}

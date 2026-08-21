using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WatchForge.DVRIP.Library;
using WatchForge.Interfaces.Library;
using WatchForge.Processing.Library;
using WatchForge.Runner;

// WatchForge Runner — worker loop pre spracovanie jobov + internal HTTP endpointy (S4-6).
// Konfigurácia: WatchForge__Runner__* env vars alebo appsettings.json sekcia "WatchForge:Runner"
// (napr. WatchForge__Runner__MaxParallelAnalyses=2, WatchForge__Runner__DbPath=...,
//  WatchForge__Runner__HttpPort=8081, WatchForge__Runner__Download__TempDir=...).

var builder = WebApplication.CreateBuilder(args);

var runnerOptions = new RunnerOptions();
builder.Configuration
    .GetSection("WatchForge:Runner")
    .Bind(runnerOptions);
runnerOptions.Validate();
builder.Services.AddSingleton(runnerOptions);

var downloadOptions = new DownloadOptions();
builder.Configuration
    .GetSection("WatchForge:Runner:Download")
    .Bind(downloadOptions);
builder.Services.AddSingleton(downloadOptions);

var clipOptions = new ClipOptions();
builder.Configuration
    .GetSection("WatchForge:Runner:Clips")
    .Bind(clipOptions);
builder.Services.AddSingleton(clipOptions);

// Vytvára DVRIP klientov (NVR spojenie) pre synchronizáciu aj download joby
builder.Services.AddSingleton<Func<DvripClientOptions, IDvripClient>>(
    _ => opts => new DvripClient(opts));

builder.Services.AddSingleton(provider =>
{
    var options = provider.GetRequiredService<RunnerOptions>();
    var connection = WatchForgeDatabase.OpenAsync(options.DbPath).GetAwaiter().GetResult();
    return connection;
});

// Repositories
builder.Services.AddSingleton<IJobRepository>(provider =>
    new JobRepository(provider.GetRequiredService<SqliteConnection>()));
builder.Services.AddSingleton<INvrRepository>(provider =>
    new NvrRepository(provider.GetRequiredService<SqliteConnection>()));
builder.Services.AddSingleton<ICameraRepository>(provider =>
    new CameraRepository(provider.GetRequiredService<SqliteConnection>()));
builder.Services.AddSingleton<IRecordingRepository>(provider =>
    new RecordingRepository(provider.GetRequiredService<SqliteConnection>()));
builder.Services.AddSingleton<IDetectionRepository>(provider =>
    new DetectionRepository(provider.GetRequiredService<SqliteConnection>()));
builder.Services.AddSingleton<IClipRepository>(provider =>
    new ClipRepository(provider.GetRequiredService<SqliteConnection>()));
builder.Services.AddSingleton<IRequestRepository>(provider =>
    new RequestRepository(provider.GetRequiredService<SqliteConnection>()));

// Doména + handlers
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<NvrSynchronizer>();
builder.Services.AddSingleton(new WatchForge.MotionSentinel.Library.Detection.DetectionOptions());
builder.Services.AddSingleton<WatchForge.MotionSentinel.Library.Detection.IObjectDetector,
    WatchForge.MotionSentinel.Library.Detection.HogPersonDetector>();
builder.Services.AddSingleton<WatchForge.MotionSentinel.Library.Detection.IFaceRecognizer>(provider =>
{
    // S19: ML pipeline (YuNet detekcia + SFace 128-dim embedding) namiesto LBPH
    // klasickej CV — FR-05 povoľuje ML model; perzistencia do FACES (S10-2 vzor)
    var recognizer = new WatchForge.MotionSentinel.Library.Detection.OnnxFaceRecognizer
    {
        FaceRepository = provider.GetRequiredService<IFaceRepository>(),
    };
    return recognizer;
});
builder.Services.AddSingleton<IFaceRepository>(provider =>
    new FaceRepository(provider.GetRequiredService<SqliteConnection>()));
builder.Services.AddSingleton<IIdentityRepository>(provider =>
    new IdentityRepository(provider.GetRequiredService<SqliteConnection>()));
builder.Services.AddSingleton<NvrDiscovery>(provider =>
    new NvrDiscovery(
        provider.GetRequiredService<INvrRepository>(),
        provider.GetRequiredService<Func<DvripClientOptions, IDvripClient>>()));
builder.Services.AddSingleton<IJobHandler, SyncJobHandler>();
builder.Services.AddSingleton<IJobHandler, DownloadJobHandler>();
builder.Services.AddSingleton<IJobHandler, AnalyzeJobHandler>();
// S22l: Purge s konfiguráciou okien (WindowMinutes, RetentionWindows z options)
builder.Services.AddSingleton<IJobHandler>(provider =>
    new PurgeJobHandler(
        provider.GetRequiredService<IRecordingRepository>(),
        provider.GetRequiredService<IDetectionRepository>(),
        provider.GetRequiredService<IClipRepository>(),
        provider.GetRequiredService<IJobRepository>(),
        provider.GetRequiredService<IClock>(),
        provider.GetRequiredService<DownloadOptions>())
    {
        WindowMinutes = runnerOptions.WindowMinutes,
        RetentionWindows = runnerOptions.RetentionWindows,
    });
builder.Services.AddSingleton<IJobHandler, ClipExtractJobHandler>();
builder.Services.AddSingleton<IJobHandler, ExportRangeJobHandler>(); // S20
builder.Services.AddSingleton<IJobExecutor, JobExecutor>();
builder.Services.AddHostedService<RunnerService>();

var app = builder.Build();

// Internal endpointy (liveness + job stav) — nie sú verejné, len pre orchestráciu
RunnerHttpEndpoints.Map(app, runnerOptions);
app.Urls.Add($"http://0.0.0.0:{runnerOptions.HttpPort}");

await app.RunAsync();

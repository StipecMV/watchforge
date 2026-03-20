using WatchForge.MotionSentinel.Server.Service;
using WatchForge.MotionSentinel.Server.Service.Detection;
using WatchForge.MotionSentinel.Server.Service.FileAccess;
using WatchForge.MotionSentinel.Server.Service.Infrastructure.Logging;
using WatchForge.MotionSentinel.Server.Service.Infrastructure.Wrappers;
using WatchForge.MotionSentinel.Server.Service.VideoSources;

var builder = Host.CreateApplicationBuilder(args);

// Logging — configured before anything else
var logDir = builder.Configuration["LogDirectory"]
    ?? Path.Combine(AppContext.BaseDirectory, "logs");
Log4NetConfigurator.Configure(logDir);

// Configuration bindings
builder.Services.Configure<LocalFileServiceOptions>(
    builder.Configuration.GetSection("Files"));

builder.Services.Configure<DetectionOptions>(
    builder.Configuration.GetSection("Detection"));

// Core dependencies
builder.Services.AddSingleton<IFileAccessService, LocalFileService>();
builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
builder.Services.AddSingleton<IAppInfoProvider, AssemblyAppInfoProvider>();
builder.Services.AddSingleton<DetectionJsonSerializer>();

// Motion detector — options injected from config
builder.Services.AddSingleton<IMotionDetector>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<DetectionOptions>>().Value;
    return new OpticalFlowDetector
    {
        IntensityThreshold = opts.IntensityThreshold,
        MinContourArea     = opts.MinContourArea,
    };
});

// VideoSource factory — new instance per file
builder.Services.AddSingleton<Func<string, IVideoSource>>(
    _ => localPath => new FileVideoSource(localPath));

builder.Services.AddSingleton<MotionAnalysisOrchestrator>();
builder.Services.AddHostedService<MotionSentinelService>();

await builder.Build().RunAsync();

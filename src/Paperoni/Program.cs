using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Paperoni;
using Paperoni.Ai;
using Paperoni.Contract;
using Paperoni.Diagnostics;
using Paperoni.ImageProcessing;
using Paperoni.Telegram;
using Serilog;
using ActivityExtensions = Paperoni.Diagnostics.ActivityExtensions;

Console.WriteLine($"Paperoni starting...");

var lockFilePath = Path.Combine(Path.GetTempPath(), "Paperoni.lock");
FileStream? lockFile;
try
{
    lockFile = new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
}
catch (IOException)
{
    Console.Error.WriteLine("Another instance of Paperoni is already running.");
    return;
}

SplashScreen.Render();

using var _ = lockFile;

var builder = Host.CreateApplicationBuilder(args);
var smokeTestInPipeline = builder.Configuration.GetValue("SmokeTestInPipeline", false);

builder.Services.AddDiagnostics(builder.Configuration);
builder.Services.AddAiService(builder.Configuration);
builder.Services.AddAlbumProcessor(builder.Configuration);
builder.Services.AddImageProcessing();

if (!smokeTestInPipeline)
{
    builder.Services.AddTelegramPhotoAlbumCollector(builder.Configuration);
}
else
{
    builder.Services.AddSingleton<AlbumQueue>();
    builder.Services.AddSingleton<ITelegramReplier, NoOpTelegramReplier>();
    Console.WriteLine("SmokeTestInPipeline enabled: skipping Telegram bot registration.");
}

var workingDir = builder.Services.AddPaperoniWorkingDirectory(builder.Configuration);

builder.Services.AddSerilog(config =>
{
    config
        .Enrich.FromLogContext()
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss}] [{Level:u3}] [AlbumId={AlbumId}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            Path.Combine(workingDir.BasePath, "paperoni.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 7,
            outputTemplate:
            "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [AlbumId={AlbumId}] {Message:lj}{NewLine}{Exception}",
            shared: true);
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Paperoni", serviceVersion: VersionInfo.Version))
    .WithTracing(tracing => tracing
        .AddSource(ActivityExtensions.Tracer.Name)
        .AddProcessor(new BatchActivityExportProcessor(
            new TraceLogExporter(workingDir, workingDir.BasePath),
            maxQueueSize: 2048,
            scheduledDelayMilliseconds: 5000)));

var host = builder.Build();

await host.Services.ValidatePipelineScript();

host.Run();

using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Paperoni;
using Paperoni.Ai;
using Paperoni.AlbumProcessing;
using Paperoni.Contract;
using Paperoni.Diagnostics;
using Paperoni.ImageProcessing;
using Paperoni.Telegram;
using Serilog;

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

builder.Services.AddDiagnostics(builder.Configuration);
builder.Services.AddTelegramPhotoAlbumCollector(builder.Configuration);
builder.Services.AddAiService(builder.Configuration);
builder.Services.AddAlbumProcessor(builder.Configuration);
builder.Services.AddImageProcessing();

var workingDir = new AlbumWorkingDirectory();
var logDir = workingDir.BasePath;
Console.WriteLine($" > Album Working Directory: {workingDir.BasePath}");
Console.WriteLine("");

builder.Services.AddSingleton(workingDir);

builder.Services.AddSerilog(config =>
{
    config
        .Enrich.FromLogContext()
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss}] [{Level:u3}] [AlbumId={AlbumId}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            Path.Combine(logDir, "paperoni.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 7,
            outputTemplate:
            "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [AlbumId={AlbumId}] {Message:lj}{NewLine}{Exception}",
            shared: true);
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Paperoni", serviceVersion: VersionInfo.Version))
    .WithTracing(tracing => tracing
        .AddSource(Diagnostics.Tracer.Name)
        .AddProcessor(new BatchActivityExportProcessor(
            new TraceLogExporter(workingDir, logDir),
            maxQueueSize: 2048,
            scheduledDelayMilliseconds: 5000)));

var host = builder.Build();
host.Run();

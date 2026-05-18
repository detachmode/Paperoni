using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Paperoni.Ai;
using Paperoni.AlbumProcessing;
using Paperoni.Contract;
using Paperoni.Diagnostics;
using Paperoni.ImageProcessing;
using Paperoni.Telegram;
using Serilog;

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

using var _ = lockFile;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDiagnostics();
builder.Services.AddTelegramPhotoAlbumCollector();
builder.Services.AddAiService(builder.Configuration);
builder.Services.AddAlbumProcessor();
builder.Services.AddImageProcessing();

var workingDir = new AlbumWorkingDirectory();
var logDir = workingDir.BasePath;
builder.Services.AddSingleton(workingDir);

builder.Configuration
    .AddUserSecrets<Program>()
    .AddEnvironmentVariables();

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
    .ConfigureResource(r => r.AddService("Paperoni"))
    .WithTracing(tracing => tracing
        .AddSource(Diagnostics.Tracer.Name)
        .AddProcessor(new BatchActivityExportProcessor(
            new TraceLogExporter(workingDir, logDir),
            maxQueueSize: 2048,
            scheduledDelayMilliseconds: 5000)));

var host = builder.Build();
host.Run();

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

using var instanceLock = new Mutex(true, "Paperoni-8765", out var createdNew);
if (!createdNew)
{
    Console.Error.WriteLine("Another instance of Paperoni is already running.");
    return;
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDiagnostics();
builder.Services.AddTelegramPhotoAlbumCollector();
builder.Services.AddAiService();
builder.Services.AddAlbumProcessor();
builder.Services.AddImageProcessing();

var workingDir = new AlbumWorkingDirectory();
var logDir = workingDir.BasePath;
builder.Services.AddSingleton(workingDir);

builder.Configuration
    .AddUserSecrets<Program>()
    .AddEnvironmentVariables();

builder.Services.AddSerilog((config) =>
{
    config
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss}] [{Level:u3}] [AlbumId={AlbumId}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            Path.Combine(logDir, "paperoni.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 7,
            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [AlbumId={AlbumId}] {Message:lj}{NewLine}{Exception}");
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Paperoni"))
    .WithTracing(tracing => tracing
        .AddSource(Diagnostics.Tracer.Name)
        .AddConsoleExporter()
        .AddProcessor(new BatchActivityExportProcessor(
            new TraceLogExporter(workingDir, logDir),
            maxQueueSize: 2048,
            scheduledDelayMilliseconds: 5000)));

var host = builder.Build();
host.Run();

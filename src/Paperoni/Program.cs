using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Paperoni;
using Paperoni.Ai;
using Paperoni.AlbumProcessing;
using Paperoni.Contract;
using Paperoni.ImageProcessing;
using Paperoni.Telegram;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddTelegramPhotoAlbumCollector();
builder.Services.AddAiService();
builder.Services.AddAlbumProcessor();
builder.Services.AddImageProcessing();
builder.Services.AddSingleton<AlbumWorkingDirectory>();

builder.Configuration
    .AddUserSecrets<Program>()
    .AddEnvironmentVariables();

var logDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    "TelegramDownloads");

builder.Services.AddSerilog((config) =>
{
    config
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss}] [{Level:u3}] [MsgId={MsgId}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            Path.Combine(logDir, "paperoni.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 7,
            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [MsgId={MsgId}] {Message:lj}{NewLine}{Exception}");
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Paperoni"))
    .WithTracing(tracing => tracing
        .AddSource(Diagnostics.Tracer.Name)
        .AddConsoleExporter()
        .AddProcessor(new BatchActivityExportProcessor(
            new TraceLogExporter(logDir), 
            maxQueueSize: 2048, 
            scheduledDelayMilliseconds: 5000)));

var host = builder.Build();
host.Run();

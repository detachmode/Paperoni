using Paperoni.Ai;
using Paperoni.AlbumProcessing;
using Paperoni.Contract;
using Paperoni.ImageProcessing;
using Paperoni.Telegram;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddTelegramPhotoAlbumCollector();
builder.Services.AddAiService();
builder.Services.AddAlbumProcessor();
builder.Services.AddImageProcessing();
builder.Services.AddSingleton<AlbumWorkingDirectory>();

builder.Configuration
    .AddUserSecrets<Program>()
    .AddEnvironmentVariables();

var host = builder.Build();

using var cts = new CancellationTokenSource();

host.Run();

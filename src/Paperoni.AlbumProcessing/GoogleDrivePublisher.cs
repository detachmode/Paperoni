using Microsoft.Extensions.Configuration;
using Paperoni.Ai;
using Paperoni.Contract;

namespace Paperoni.AlbumProcessing;

internal class GoogleDrivePublisher(AlbumWorkingDirectory workingDirectory, IConfiguration configuration) : IGoogleDrivePublisher
{
    private readonly string _outputPath = configuration["GoogleDriveOutputPath"] ?? throw new InvalidOperationException("Configuration key 'GoogleDriveOutputPath' is not set");

    public async Task CopyToGoogleDrive(int msgId, CancellationToken stoppingToken)
    {
        var aiResult = await workingDirectory.RequireData<AiResult>(msgId, stoppingToken);
        var workingDir = workingDirectory.GetDownloadPath(msgId);
        var pdf = Directory.GetFiles(workingDir, "*.pdf", SearchOption.TopDirectoryOnly).FirstOrDefault();
        ArgumentNullException.ThrowIfNull(pdf);
        
        var googleDriveLocation = Path.Combine(_outputPath, $"{aiResult.Title}.pdf");
        Directory.CreateDirectory(_outputPath);
        File.Copy(pdf, googleDriveLocation, overwrite: true);
    }
}
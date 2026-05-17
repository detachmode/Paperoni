namespace Paperoni.Contract;

public class AlbumWorkingDirectory
{
    public string? DownloadBasePath { get; init; }

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public string GetDownloadPath(int messageId)
    {
        var baseDir = DownloadBasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "TelegramDownloads");

        var path = Path.Combine(baseDir, messageId.ToString());
        Directory.CreateDirectory(path);

        return path;
    }

    public async Task WriteData<T>(int messageId, T data, CancellationToken stoppingToken = default)
    {
        await _semaphore.WaitAsync(stoppingToken);
        try
        {
            var workDir = GetDownloadPath(messageId);
            var path = Path.Combine(workDir, typeof(T).Name + ".json");
            var json = System.Text.Json.JsonSerializer.Serialize(data,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json, stoppingToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<T> RequireData<T>(int messageId, CancellationToken stoppingToken = default) =>
        await GetData<T>(messageId, stoppingToken) ??
        throw new ArgumentException("Didn't find json for data of " + nameof(T));

    public async Task<T?> GetData<T>(int messageId, CancellationToken stoppingToken = default)
    {
        await _semaphore.WaitAsync(stoppingToken);
        try
        {
            var workDir = GetDownloadPath(messageId);
            var path = Path.Combine(workDir, typeof(T).Name + ".json");

            var json = await File.ReadAllTextAsync(path, stoppingToken);
            return System.Text.Json.JsonSerializer.Deserialize<T>(json);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
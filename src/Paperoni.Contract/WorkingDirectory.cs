using System.Text.Json;

namespace Paperoni.Contract;

public class WorkingDirectory
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    public string? PaperoniWorkingDirectory { private get; init; }

    public string BasePath =>
        PaperoniWorkingDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".paperoni");

    public string RequireWorkingDirectory(int messageId)
    {
        var path = Path.Combine(BasePath, messageId.ToString());
        Directory.CreateDirectory(path);

        return path;
    }

    public async Task WriteData<T>(int messageId, T data, CancellationToken stoppingToken = default)
    {
        await _semaphore.WaitAsync(stoppingToken);
        try
        {
            var workDir = RequireWorkingDirectory(messageId);
            var path = Path.Combine(workDir, typeof(T).Name + ".json");
            var json = JsonSerializer.Serialize(data,
                new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json, stoppingToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<T> RequireData<T>(int messageId, CancellationToken stoppingToken = default) where T : class =>
        await GetData<T>(messageId, stoppingToken) ??
        throw new ArgumentException("Didn't find json for data of " + typeof(T).Name);

    public async Task<T?> GetData<T>(int messageId, CancellationToken stoppingToken = default) where T : class
    {
        await _semaphore.WaitAsync(stoppingToken);
        try
        {
            var workDir = RequireWorkingDirectory(messageId);
            var path = Path.Combine(workDir, typeof(T).Name + ".json");

            if (!File.Exists(path))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(path, stoppingToken);
            return JsonSerializer.Deserialize<T>(json);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void DeleteAlbumDirectory(int messageId)
    {
        var path = Path.Combine(BasePath, messageId.ToString());
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
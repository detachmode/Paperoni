using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Paperoni.Contract;

public static class DependencyInjection
{
    public static WorkingDirectory AddPaperoniWorkingDirectory(this IServiceCollection collection, IConfiguration configuration)
    {
        var downloadBasePath = configuration["DownloadBasePath"];
        var workingDir = new WorkingDirectory { PaperoniWorkingDirectory = downloadBasePath };
        Console.WriteLine($" > Album Working Directory: {workingDir.BasePath}");
        collection.AddSingleton(workingDir);
        return workingDir;
    }
}

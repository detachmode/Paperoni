using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Paperoni.Contract;

public static class DependencyInjection
{
    public static WorkingDirectory AddPaperoniWorkingDirectory(this IServiceCollection collection,
        IConfiguration configuration)
    {
        collection.AddSingleton<AlbumQueue>();

        var paperoniWorkingDirectory = configuration["PaperoniWorkingDirectory"];
        var workingDir = new WorkingDirectory { PaperoniWorkingDirectory = paperoniWorkingDirectory };
        Console.WriteLine($" > Working Directory: {workingDir.BasePath}");
        collection.AddSingleton(workingDir);
        return workingDir;
    }
}

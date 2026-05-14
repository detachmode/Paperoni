using Microsoft.Extensions.DependencyInjection;

namespace Paperoni.AlbumProcessing;

public static class DependencyInjection
{
    public static IServiceCollection AddAlbumProcessor(this IServiceCollection collection)
    {
        collection.AddHostedService<AlbumProcessor>();
        collection.AddSingleton<IGoogleDrivePublisher, GoogleDrivePublisher>();

        return collection;
    }
}
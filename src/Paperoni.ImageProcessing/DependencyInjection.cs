using Microsoft.Extensions.DependencyInjection;

namespace Paperoni.ImageProcessing;

public static class DependencyInjection
{
    public static IServiceCollection AddImageProcessing(this IServiceCollection services)
    {
        services.AddSingleton<IPdfCreator, PdfCreator>();
        services.AddSingleton<PdfMerger>();
        return services;
    }
}

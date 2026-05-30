using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace Paperoni.ImageProcessing;

public static class DependencyInjection
{
    public static IServiceCollection AddImageProcessing(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddSingleton(_ => CroppingOptionsFrom(configuration));
        services.AddSingleton<ICropLlmDetector, LlmCropDetector>();
        services.AddSingleton<IPdfCreator, PdfCreator>();
        services.AddSingleton<PdfMerger>();
        return services;
    }

    private static CroppingOptions CroppingOptionsFrom(IConfiguration? configuration)
    {
        var section = configuration?.GetSection("Cropping");
        if (section is null || !section.Exists())
        {
            return new CroppingOptions();
        }

        return new CroppingOptions
        {
            Mode = Enum.TryParse<CroppingMode>(section["Mode"], ignoreCase: true, out var mode) ? mode : CroppingMode.Auto,
            HighConfidenceThreshold = double.TryParse(section["HighConfidenceThreshold"], CultureInfo.InvariantCulture, out var high) ? high : 0.75,
            MediumConfidenceThreshold = double.TryParse(section["MediumConfidenceThreshold"], CultureInfo.InvariantCulture, out var medium) ? medium : 0.45,
            LlmTimeoutSeconds = int.TryParse(section["LlmTimeoutSeconds"], out var timeout) ? timeout : 30,
            LlmMaxConcurrency = int.TryParse(section["LlmMaxConcurrency"], out var concurrency) ? concurrency : 2
        };
    }
}

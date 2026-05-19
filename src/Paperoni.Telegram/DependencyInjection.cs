using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Paperoni.Telegram.Album;
using Telegram.Bot;

namespace Paperoni.Telegram;

public static class DependencyInjection
{
    public static IServiceCollection AddTelegramPhotoAlbumCollector(this IServiceCollection collection,
        IConfiguration configuration)
    {
        collection.AddOptions<TelegramSettings>()
            .Bind(configuration.GetSection("Telegram"))
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.BotToken),
                "Telegram:BotToken is required")
            .ValidateOnStart();
        collection.PostConfigure<TelegramSettings>(settings =>
        {
            if (string.IsNullOrEmpty(settings.BotToken))
            {
                settings.BotToken = configuration["TELEGRAM_BOT_TOKEN"] ?? "";
            }
        });
        collection.AddSingleton<TelegramSettings>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TelegramSettings>>().Value;
            Console.WriteLine($"Telegram: BotToken={(!string.IsNullOrEmpty(settings.BotToken) ? "***" : "not set")}");
            return settings;
        });

        collection.AddSingleton<AlbumQueue>();
        collection.AddSingleton<ITelegramReplier, TelegramReplier>();

        collection.AddSingleton<TelegramBotClient>(sp =>
        {
            var settings = sp.GetRequiredService<TelegramSettings>();
            return new TelegramBotClient(settings.BotToken);
        });
        collection.AddSingleton<ITelegramBotClient>(sp => sp.GetRequiredService<TelegramBotClient>());

        collection.AddHostedService<TelegramPhotoAlbumCollector>();

        return collection;
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Paperoni.Telegram.Album;
using Telegram.Bot;

namespace Paperoni.Telegram;

public static class DependencyInjection
{
    public static IServiceCollection AddTelegramPhotoAlbumCollector(this IServiceCollection collection)
    {
        collection.AddSingleton<AlbumQueue>();
        collection.AddSingleton<ITelegramReplier, TelegramReplier>();

        collection.AddSingleton<TelegramBotClient>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var botToken = config["TELEGRAM_BOT_TOKEN"];
            if (string.IsNullOrEmpty(botToken))
            {
                throw new InvalidOperationException("Bot token is not configured");
            }

            return new TelegramBotClient(botToken);
        });
        collection.AddSingleton<ITelegramBotClient>(sp => sp.GetRequiredService<TelegramBotClient>());

        collection.AddHostedService<TelegramPhotoAlbumCollector>();

        return collection;
    }
}

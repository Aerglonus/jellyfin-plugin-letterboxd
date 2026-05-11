using System;
using System.Threading.Tasks;
using LetterboxdSync.Configuration;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace LetterboxdSync;

internal static class SyncHelpers
{
    public static async Task<LetterboxdClient?> CreateAuthenticatedClientAsync(Account account, string username, ILogger logger)
    {
        var client = new LetterboxdClient(logger);
        try
        {
            await client.AuthenticateAsync(account).ConfigureAwait(false);

            if (client.TokensRefreshed)
            {
                Plugin.Instance!.SaveConfiguration();
            }

            return client;
        }
        catch (Exception ex)
        {
            logger.LogError("Auth failed for {Username}: {Message}", username, ex.Message);
            client.Dispose();
            return null;
        }
    }

    public static double? GetLetterboxdRating(double? jellyfinRating)
    {
        if (!jellyfinRating.HasValue)
            return null;

        var mapped = Math.Round(jellyfinRating.Value / 2.0 * 2) / 2.0;
        return Math.Clamp(mapped, 0.5, 5.0);
    }

    public static async Task SyncFavoritesAsync(LetterboxdClient client, Account account, string filmId, bool isFavorite, string title, ILogger logger)
    {
        if (account.SyncFavorites)
        {
            try
            {
                await client.SetFilmLikeAsync(filmId, isFavorite).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Failed to sync like status for {Title} ({FilmId}): {Message}", title, filmId, ex.Message);
            }
        }
    }
}

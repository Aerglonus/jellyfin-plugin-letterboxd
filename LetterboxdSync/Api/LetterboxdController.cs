using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using LetterboxdSync.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace LetterboxdSync.Api;

[ApiController]
[Authorize]
[Route("Jellyfin.Plugin.LetterboxdSync")]
[Produces(MediaTypeNames.Application.Json)]
public class LetterboxdController : ControllerBase
{
    private const string ManualSource = "manual";
    private const string RetrySource = "retry";
    private const string ReviewSource = "review";

    private readonly ILogger<LetterboxdController> _logger;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;

    public LetterboxdController(ILogger<LetterboxdController> logger, IUserManager userManager, ILibraryManager libraryManager, IUserDataManager userDataManager)
    {
        _logger = logger;
        _userManager = userManager;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
    }

    private static PluginConfiguration Config => Plugin.Instance!.Configuration;

    [AllowAnonymous]
    [HttpGet("UserSettingsPage")]
    [Produces(MediaTypeNames.Text.Html)]
    public ActionResult GetUserSettingsPage()
    {
        return GetResource("userConfigPage.html");
    }

    [AllowAnonymous]
    [HttpGet("StatsPage")]
    [Produces(MediaTypeNames.Text.Html)]
    public ActionResult GetStatsPage()
    {
        return GetResource("statsPage.html");
    }

    [AllowAnonymous]
    [HttpGet("MenuScript")]
    [Produces("text/javascript")]
    public ActionResult GetMenuScript()
    {
        return GetResource("letterboxd-menu.js", "text/javascript");
    }

    private ActionResult GetResource(string fileName, string contentType = "text/html")
    {
        var resourcePath = $"LetterboxdSync.Web.{fileName}";
        using var stream = GetType().Assembly.GetManifestResourceStream(resourcePath);
        if (stream == null) return NotFound();
        using var reader = new System.IO.StreamReader(stream);
        var content = reader.ReadToEnd();
        return Content(content, contentType);
    }

    [HttpGet("Stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetStats()
    {
        var account = GetCurrentUserAccount();
        var userId = GetCurrentUserId();
        var usernames = GetCurrentHistoryUsernames(account);

        var (total, success, failed, skipped, rewatches) = string.IsNullOrEmpty(userId)
            ? SyncHistory.GetStats(usernames)
            : SyncHistory.GetStatsForUser(userId, usernames);
        return Ok(new
        {
            total,
            success,
            failed,
            skipped,
            rewatches
        });
    }

    [HttpGet("History")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetHistory([FromQuery] int count = 50)
    {
        var account = GetCurrentUserAccount();
        var userId = GetCurrentUserId();
        var usernames = GetCurrentHistoryUsernames(account);

        var events = string.IsNullOrEmpty(userId)
            ? SyncHistory.GetRecent(Math.Min(count, 200), usernames)
            : SyncHistory.GetRecentForUser(Math.Min(count, 200), userId, usernames);
        return Ok(events);
    }

    [HttpPost("RunSync")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> RunSync()
    {
        if (!TryGetCurrentUser(out var userId, out var user, out var errorResult))
            return errorResult!;

        var account = GetEnabledAccount(userId);
        if (account == null)
            return BadRequest(new { error = "No enabled Letterboxd account configured for this user" });

        var result = await RunSyncForUserAsync(user, account).ConfigureAwait(false);
        return Ok(result);
    }

    private IReadOnlyCollection<string> GetCurrentHistoryUsernames(Account? account)
    {
        var usernames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(account?.LetterboxdUsername))
        {
            usernames.Add(account.LetterboxdUsername);
        }

        var userId = User.Claims.FirstOrDefault(c => c.Type == "Jellyfin-UserId")?.Value;
        if (Guid.TryParse(userId, out var parsedUserId))
        {
            var user = _userManager.GetUserById(parsedUserId);
            if (!string.IsNullOrWhiteSpace(user?.Username))
            {
                usernames.Add(user.Username);
            }
        }

        return usernames;
    }

    private Account? GetCurrentUserAccount()
    {
        var userId = GetCurrentUserId();
        return string.IsNullOrEmpty(userId)
            ? null
            : Config.Accounts.FirstOrDefault(a => a.UserJellyfinId == userId);
    }

    private Account? GetEnabledAccount(string userId)
    {
        return Config.Accounts.FirstOrDefault(a => a.Enabled && a.UserJellyfinId == userId);
    }

    private bool TryGetCurrentUser(out string userId, out User user, out ActionResult? errorResult)
    {
        user = null!;
        userId = GetCurrentUserId() ?? string.Empty;
        if (string.IsNullOrEmpty(userId))
        {
            errorResult = BadRequest(new { error = "Could not determine user" });
            return false;
        }

        var currentUser = _userManager.GetUserById(new Guid(userId));
        if (currentUser == null)
        {
            errorResult = BadRequest(new { error = "Invalid Jellyfin user" });
            return false;
        }

        user = currentUser;
        errorResult = null;
        return true;
    }

    private static SyncEvent CreateSyncEvent(
        User user,
        BaseItem movie,
        int tmdbId,
        SyncStatus status,
        string source,
        DateTime? viewingDate,
        string filmSlug = "",
        string? error = null)
    {
        return new SyncEvent
        {
            FilmTitle = movie.Name,
            FilmSlug = filmSlug,
            TmdbId = tmdbId,
            UserId = user.Id.ToString("N"),
            Username = user.Username,
            Timestamp = DateTime.UtcNow,
            ViewingDate = viewingDate,
            Status = status,
            Error = error,
            Source = source
        };
    }

    private async Task<object> RunSyncForUserAsync(User user, Account account)
    {
        var movies = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie },
            IsVirtualItem = false,
            IsPlayed = true,
            OrderBy = new[] { (ItemSortBy.SortName, Jellyfin.Database.Implementations.Enums.SortOrder.Ascending) }
        }).ToList();

        if (account.EnableDateFilter)
        {
            var cutoff = DateTime.UtcNow.AddDays(-account.DateFilterDays);
            movies = movies.Where(movie =>
            {
                var userData = _userDataManager.GetUserData(user, movie);
                return userData?.LastPlayedDate.HasValue == true && userData.LastPlayedDate.Value >= cutoff;
            }).ToList();
        }

        using var client = await SyncHelpers.CreateAuthenticatedClientAsync(account, user.Username, _logger).ConfigureAwait(false);
        if (client == null)
        {
            return new { synced = 0, skipped = 0, failed = 0, message = "Authentication failed" };
        }

        var synced = 0;
        var skipped = 0;
        var failed = 0;
        var diaryTmdbIds = new HashSet<int>();

        try
        {
            var diaryList = await client.GetDiaryTmdbIdsAsync(account.LetterboxdUsername).ConfigureAwait(false);
            diaryTmdbIds = new HashSet<int>(diaryList);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to fetch Letterboxd diary for {Username}: {Message}. Falling back to individual checks.", user.Username, ex.Message);
        }

        foreach (var movie in movies)
        {
            var tmdbIdStr = movie.GetProviderId(MediaBrowser.Model.Entities.MetadataProvider.Tmdb);
            if (!int.TryParse(tmdbIdStr, out var tmdbId))
            {
                skipped++;
                continue;
            }

            var userData = _userDataManager.GetUserData(user, movie);
            var viewingDate = userData?.LastPlayedDate?.Date ?? DateTime.Now.Date;

            if (diaryTmdbIds.Contains(tmdbId))
            {
                SyncHistory.Record(CreateSyncEvent(user, movie, tmdbId, SyncStatus.Skipped, ManualSource, viewingDate));
                skipped++;
                continue;
            }

            FilmResult? film = null;
            try
            {
                film = await client.LookupFilmByTmdbIdAsync(tmdbId).ConfigureAwait(false);

                var diary = await client.GetDiaryInfoAsync(film.FilmId).ConfigureAwait(false);
                if (diary.IsWatched || diary.HasAnyEntry || (diary.LastDate != null && diary.LastDate.Value.Date == viewingDate))
                {
                    await SyncHelpers.SyncFavoritesAsync(client, account, film.FilmId, userData?.IsFavorite ?? false, movie.Name, _logger).ConfigureAwait(false);

                    SyncHistory.Record(CreateSyncEvent(user, movie, tmdbId, SyncStatus.Skipped, ManualSource, viewingDate, film.Slug));
                    skipped++;
                    continue;
                }

                var liked = account.SyncFavorites && (userData?.IsFavorite ?? false);
                var rating = SyncHelpers.GetLetterboxdRating(userData?.Rating);

                await client.MarkAsWatchedAsync(film.Slug, film.FilmId, userData?.LastPlayedDate, liked, film.ProductionId, false, rating).ConfigureAwait(false);
                await SyncHelpers.SyncFavoritesAsync(client, account, film.FilmId, userData?.IsFavorite ?? false, movie.Name, _logger).ConfigureAwait(false);

                _logger.LogInformation("Manually synced {Title} (TMDb:{TmdbId}) to Letterboxd for {Username} on {Date}",
                    movie.Name, tmdbId, user.Username, viewingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

                SyncHistory.Record(CreateSyncEvent(user, movie, tmdbId, SyncStatus.Success, ManualSource, viewingDate, film.Slug));
                synced++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to manually sync {Title} (TMDb:{TmdbId}) for {Username}", movie.Name, tmdbId, user.Username);

                SyncHistory.Record(CreateSyncEvent(user, movie, tmdbId, SyncStatus.Failed, ManualSource, viewingDate, film?.Slug ?? string.Empty, ex.Message));
                failed++;
            }
        }

        return new { synced, skipped, failed };
    }

    [HttpPost("Review")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> PostReview([FromBody] ReviewRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FilmSlug))
            return BadRequest(new { error = "filmSlug is required" });

        if (string.IsNullOrWhiteSpace(request.ReviewText) && !request.IsRewatch)
            return BadRequest(new { error = "reviewText is required unless logging a rewatch" });

        var account = GetCurrentUserAccount();
        if (account == null || !account.Enabled)
            return BadRequest(new { error = "No enabled Letterboxd account configured for this user" });
        var userId = GetCurrentUserId();

        using var client = new LetterboxdClient(_logger);
        try
        {
            await client.AuthenticateAsync(account).ConfigureAwait(false);

            if (client.TokensRefreshed)
            {
                Plugin.Instance!.SaveConfiguration();
            }

            int? tmdbId = null;
            var history = string.IsNullOrEmpty(userId)
                ? SyncHistory.GetRecent(500)
                : SyncHistory.GetRecentForUser(500, userId, GetCurrentHistoryUsernames(account));
            var historyMatch = history.FirstOrDefault(h => h.FilmSlug == request.FilmSlug && h.TmdbId > 0);
            if (historyMatch != null)
                tmdbId = historyMatch.TmdbId;

            if (!tmdbId.HasValue)
                return BadRequest(new { error = "Cannot find TMDb ID for this film. Please play the film briefly to sync it to your history first." });

            var film = await client.LookupFilmByTmdbIdAsync(tmdbId.Value).ConfigureAwait(false);

            await client.PostReviewAsync(film.FilmId, request.ReviewText, request.ContainsSpoilers, request.IsRewatch, request.Date, request.Rating)
                .ConfigureAwait(false);

            _logger.LogInformation("Posted review for {FilmSlug} by {Username}",
                request.FilmSlug, account.LetterboxdUsername);

            var status = request.IsRewatch ? SyncStatus.Rewatch : SyncStatus.Success;
            SyncHistory.Record(new SyncEvent
            {
                FilmTitle = request.FilmSlug.Replace("-", " "),
                FilmSlug = request.FilmSlug,
                UserId = userId ?? string.Empty,
                Username = account.LetterboxdUsername,
                Timestamp = DateTime.UtcNow,
                Status = status,
                Source = ReviewSource
            });

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to post review for {FilmSlug}: {Message}", request.FilmSlug, ex.Message);

            SyncHistory.Record(new SyncEvent
            {
                FilmTitle = request.FilmSlug.Replace("-", " "),
                FilmSlug = request.FilmSlug,
                UserId = userId ?? string.Empty,
                Username = account?.LetterboxdUsername ?? "unknown",
                Timestamp = DateTime.UtcNow,
                Status = SyncStatus.Failed,
                Error = ex.Message,
                Source = ReviewSource
            });

            return BadRequest(new { error = ex.Message });
        }
    }
    [HttpPost("Retry")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> PostRetry([FromBody] RetryRequest request)
    {
        if (request.TmdbId <= 0)
            return BadRequest(new { error = "tmdbId is required" });

        if (!TryGetCurrentUser(out var userId, out var user, out var errorResult))
            return errorResult!;

        var account = GetEnabledAccount(userId);
        if (account == null)
            return BadRequest(new { error = "No Letterboxd account configured for this user" });

        using var client = new LetterboxdClient(_logger);
        try
        {
            await client.AuthenticateAsync(account).ConfigureAwait(false);

            if (client.TokensRefreshed)
            {
                Plugin.Instance!.SaveConfiguration();
            }

            var movie = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                IsVirtualItem = false,
                Recursive = true
            }).FirstOrDefault(m => m.GetProviderId(MediaBrowser.Model.Entities.MetadataProvider.Tmdb) == request.TmdbId.ToString());

            if (movie == null)
                return NotFound(new { error = "Cannot find movie in Jellyfin library with that TMDb ID." });

            var film = await client.LookupFilmByTmdbIdAsync(request.TmdbId).ConfigureAwait(false);
            var diary = await client.GetDiaryInfoAsync(film.FilmId).ConfigureAwait(false);

            var userData = _userDataManager.GetUserData(user, movie);
            var viewingDate = userData?.LastPlayedDate?.Date ?? DateTime.Now.Date;

            bool isRewatch = diary.IsWatched || diary.HasAnyEntry;
            bool liked = account.SyncFavorites && (userData?.IsFavorite ?? false);

            var lbRating = SyncHelpers.GetLetterboxdRating(userData?.Rating);

            await client.MarkAsWatchedAsync(film.Slug, film.FilmId, DateTime.Now, liked,
                film.ProductionId, isRewatch, lbRating).ConfigureAwait(false);

            await SyncHelpers.SyncFavoritesAsync(client, account, film.FilmId, userData?.IsFavorite ?? false, movie.Name, _logger).ConfigureAwait(false);

            _logger.LogInformation("Manually retried sync for {Title} by {Username}",
                movie.Name, account.LetterboxdUsername);

            var status = isRewatch ? SyncStatus.Rewatch : SyncStatus.Success;
            SyncHistory.Record(new SyncEvent
            {
                FilmTitle = movie.Name,
                FilmSlug = film.Slug,
                TmdbId = request.TmdbId,
                UserId = userId,
                Username = account.LetterboxdUsername,
                Timestamp = DateTime.UtcNow,
                ViewingDate = viewingDate,
                Status = status,
                Source = RetrySource
            });

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to manually retry sync for TMDb {TmdbId}: {Message}", request.TmdbId, ex.Message);

            string filmTitle = $"TMDb ID: {request.TmdbId}";
            var oldHistory = SyncHistory.GetRecentForUser(500, userId, GetCurrentHistoryUsernames(account));
            var oldEvent = oldHistory.FirstOrDefault(e => e.TmdbId == request.TmdbId);
            if (oldEvent != null && !string.IsNullOrEmpty(oldEvent.FilmTitle) && !oldEvent.FilmTitle.StartsWith("TMDb ID"))
            {
                filmTitle = oldEvent.FilmTitle;
            }

            SyncHistory.Record(new SyncEvent
            {
                FilmTitle = filmTitle,
                TmdbId = request.TmdbId,
                UserId = userId,
                Username = account?.LetterboxdUsername ?? "unknown",
                Timestamp = DateTime.UtcNow,
                Status = SyncStatus.Failed,
                Error = ex.Message,
                Source = RetrySource
            });

            if (ex.Message.Contains("Could not find film matching"))
                return NotFound(new { error = ex.Message });

            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("UserConfig")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetUserConfig()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return BadRequest(new { error = "Could not determine user" });

        var account = Config.Accounts.FirstOrDefault(
            a => a.UserJellyfinId == userId);

        if (account == null)
            return NotFound(new { error = "No Letterboxd account configured" });

        return Ok(new {
            letterboxdUsername = account.LetterboxdUsername,
            hasPassword = !string.IsNullOrEmpty(account.LetterboxdPassword),
            enabled = account.Enabled,
            syncFavorites = account.SyncFavorites,
            enableDateFilter = account.EnableDateFilter,
            dateFilterDays = account.DateFilterDays,
            enableWatchlistSync = account.EnableWatchlistSync,
            enableDiaryImport = account.EnableDiaryImport
        });
    }

    [HttpPost("UserConfig")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult SaveUserConfig([FromBody] UserConfigRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return BadRequest(new { error = "Could not determine user" });

        var account = Config.Accounts.FirstOrDefault(
            a => a.UserJellyfinId == userId);

        if (account == null)
        {
            account = new Account { UserJellyfinId = userId };
            Config.Accounts.Add(account);
        }

        account.LetterboxdUsername = request.LetterboxdUsername;
        if (!string.IsNullOrEmpty(request.LetterboxdPassword))
            account.LetterboxdPassword = CryptoHelpers.Encrypt(request.LetterboxdPassword);

        account.Enabled = request.Enabled;
        account.SyncFavorites = request.SyncFavorites;
        account.EnableDateFilter = request.EnableDateFilter;
        account.DateFilterDays = request.DateFilterDays;
        account.EnableWatchlistSync = request.EnableWatchlistSync;
        account.EnableDiaryImport = request.EnableDiaryImport;
        account.ConfiguredByUser = true;

        account.AccessToken = null;
        account.RefreshToken = null;
        account.TokenExpiration = null;

        Plugin.Instance!.SaveConfiguration();
        return Ok(new { success = true });
    }

    [HttpDelete("UserConfig")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult DeleteUserConfig()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return BadRequest(new { error = "Could not determine user" });

        Config.Accounts.RemoveAll(a => a.UserJellyfinId == userId);
        Plugin.Instance!.SaveConfiguration();
        return Ok(new { success = true });
    }

    [HttpPost("TestConnection")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> TestConnection([FromBody] TestConnectionRequest request)
    {
        using var client = new LetterboxdClient(_logger);
        try
        {
            // Decrypt password if it was sent as empty indicating "use existing"
            var password = request.LetterboxdPassword;
            if (string.IsNullOrEmpty(password))
            {
                var userId = GetCurrentUserId();
                if (userId != null)
                {
                    var account = Config.Accounts.FirstOrDefault(a => a.UserJellyfinId == userId);
                    if (account != null && account.LetterboxdUsername == request.LetterboxdUsername)
                    {
                        password = CryptoHelpers.Decrypt(account.LetterboxdPassword);
                    }
                }
            }

            if (string.IsNullOrEmpty(password))
            {
                return BadRequest(new { error = "Password is required for testing connection." });
            }

            await client.AuthenticateAsync(request.LetterboxdUsername, password).ConfigureAwait(false);
            return Ok(new { success = true, message = "Successfully authenticated with Letterboxd." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test connection failed for {Username}", request.LetterboxdUsername);
            return BadRequest(new { error = $"Authentication failed: {ex.Message}" });
        }
    }

    private string? GetCurrentUserId()
    {
        return User.Claims
            .FirstOrDefault(c => c.Type == "Jellyfin-UserId")?.Value?
            .Replace("-", "");
    }
}

public class ReviewRequest
{
    public string FilmSlug { get; set; } = string.Empty;
    public string? ReviewText { get; set; }
    public bool ContainsSpoilers { get; set; }
    public bool IsRewatch { get; set; }
    public string? Date { get; set; }
    public double? Rating { get; set; }
}

public class RetryRequest
{
    public int TmdbId { get; set; }
}

public class UserConfigRequest
{
    public string LetterboxdUsername { get; set; } = string.Empty;
    public string? LetterboxdPassword { get; set; }
    public bool Enabled { get; set; } = true;
    public bool SyncFavorites { get; set; }
    public bool EnableDateFilter { get; set; }
    public int DateFilterDays { get; set; } = 7;
    public bool EnableWatchlistSync { get; set; }
    public bool EnableDiaryImport { get; set; }
}

public class TestConnectionRequest
{
    public string LetterboxdUsername { get; set; } = string.Empty;
    public string? LetterboxdPassword { get; set; }
}

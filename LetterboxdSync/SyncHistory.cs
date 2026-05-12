using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace LetterboxdSync;

public enum SyncStatus
{
    Success,
    Skipped,
    Failed,
    Rewatch
}

public class SyncEvent
{
    public string UserId { get; set; } = string.Empty;
    public string FilmTitle { get; set; } = string.Empty;
    public string FilmSlug { get; set; } = string.Empty;
    public int TmdbId { get; set; }
    public string Username { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public DateTime? ViewingDate { get; set; }
    public SyncStatus Status { get; set; }
    public string? Error { get; set; }
    public string? Source { get; set; } // "playback" or "scheduled"
}

public static class SyncHistory
{
    private static readonly object _lock = new();
    private static readonly Dictionary<string, List<SyncEvent>> _eventsByPath = new();
    private const int MaxEvents = 500;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string DataDirectory
    {
        get
        {
            // Store in the configurations directory — survives version upgrades
            // This is where LetterboxdSync.xml lives: /config/data/plugins/configurations/
            var assembly = typeof(SyncHistory).Assembly.Location;
            var pluginDir = Path.GetDirectoryName(assembly);
            if (!string.IsNullOrEmpty(pluginDir))
            {
                var configDir = Path.Combine(pluginDir, "..", "configurations");
                if (Directory.Exists(configDir))
                    return configDir;
            }

            // Fallback: next to the DLL
            if (!string.IsNullOrEmpty(pluginDir))
                return pluginDir;

            return ".";
        }
    }

    private static string SharedDataPath => Path.Combine(DataDirectory, "letterboxd-sync-history.json");

    private static string GetUserDataPath(string userId)
    {
        return Path.Combine(DataDirectory, $"letterboxd-sync-history-{SanitizeFileSegment(userId)}.json");
    }

    private static string GetDataPath(string? userId)
    {
        return string.IsNullOrWhiteSpace(userId)
            ? SharedDataPath
            : GetUserDataPath(userId);
    }

    private static string SanitizeFileSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value
            .Where(c => !invalid.Contains(c))
            .ToArray();

        return new string(chars);
    }

    private static List<SyncEvent> LoadEvents(string? userId = null)
    {
        var path = GetDataPath(userId);
        if (_eventsByPath.TryGetValue(path, out var cachedEvents))
        {
            return cachedEvents;
        }

        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var events = JsonSerializer.Deserialize<List<SyncEvent>>(json) ?? new List<SyncEvent>();
                _eventsByPath[path] = events;
                return events;
            }
        }
        catch { }

        var emptyEvents = new List<SyncEvent>();
        _eventsByPath[path] = emptyEvents;
        return emptyEvents;
    }

    private static void SaveEvents(string path, List<SyncEvent> events)
    {
        try
        {
            Console.WriteLine($"[LetterboxdSync] Saving sync history to: {path}");
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(events, JsonOptions);
            File.WriteAllText(path, json);
            Console.WriteLine($"[LetterboxdSync] Saved {events.Count} events to sync history");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LetterboxdSync] Failed to save sync history: {ex.Message}");
        }
    }

    public static void Record(SyncEvent evt)
    {
        lock (_lock)
        {
            var path = GetDataPath(evt.UserId);
            var events = LoadEvents(evt.UserId);
            events.Insert(0, evt);

            // Trim to max
            if (events.Count > MaxEvents)
                events.RemoveRange(MaxEvents, events.Count - MaxEvents);

            SaveEvents(path, events);
        }
    }

    public static List<SyncEvent> GetRecent(int count = 100, string? username = null)
    {
        lock (_lock)
        {
            var events = LoadEvents();
            if (!string.IsNullOrEmpty(username))
            {
                events = events.FindAll(e => string.Equals(e.Username, username, StringComparison.OrdinalIgnoreCase));
            }
            return events.GetRange(0, Math.Min(count, events.Count));
        }
    }

    public static List<SyncEvent> GetRecent(int count, IEnumerable<string> usernames)
    {
        lock (_lock)
        {
            var users = ToUsernameSet(usernames);
            var events = GetSharedEventsForUsers(users);

            return events.GetRange(0, Math.Min(count, events.Count));
        }
    }

    public static List<SyncEvent> GetRecentForUser(int count, string userId, IEnumerable<string> usernames)
    {
        lock (_lock)
        {
            var users = ToUsernameSet(usernames);

            var events = LoadEvents(userId)
                .Concat(GetLegacyEventsForUser(userId, users))
                .OrderByDescending(e => e.Timestamp)
                .Take(count)
                .ToList();

            return events;
        }
    }

    public static (int Total, int Success, int Failed, int Skipped, int Rewatches) GetStats(string? username = null)
    {
        lock (_lock)
        {
            var events = LoadEvents();
            if (!string.IsNullOrEmpty(username))
            {
                events = events.FindAll(e => string.Equals(e.Username, username, StringComparison.OrdinalIgnoreCase));
            }

            return CalculateStats(events);
        }
    }

    public static (int Total, int Success, int Failed, int Skipped, int Rewatches) GetStats(IEnumerable<string> usernames)
    {
        lock (_lock)
        {
            var users = ToUsernameSet(usernames);
            var events = GetSharedEventsForUsers(users);

            return CalculateStats(events);
        }
    }

    public static (int Total, int Success, int Failed, int Skipped, int Rewatches) GetStatsForUser(string userId, IEnumerable<string> usernames)
    {
        lock (_lock)
        {
            var users = ToUsernameSet(usernames);

            var events = LoadEvents(userId)
                .Concat(GetLegacyEventsForUser(userId, users))
                .ToList();

            return CalculateStats(events);
        }
    }

    private static HashSet<string> ToUsernameSet(IEnumerable<string> usernames)
    {
        return usernames
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static List<SyncEvent> GetSharedEventsForUsers(HashSet<string> usernames)
    {
        return usernames.Count == 0
            ? new List<SyncEvent>()
            : LoadEvents().FindAll(e => usernames.Contains(e.Username));
    }

    private static (int Total, int Success, int Failed, int Skipped, int Rewatches) CalculateStats(IEnumerable<SyncEvent> sourceEvents)
    {
        var events = sourceEvents as ICollection<SyncEvent> ?? sourceEvents.ToList();

        return (
            events.Count,
            events.Count(e => e.Status == SyncStatus.Success),
            events.Count(e => e.Status == SyncStatus.Failed),
            events.Count(e => e.Status == SyncStatus.Skipped),
            events.Count(e => e.Status == SyncStatus.Rewatch)
        );
    }

    private static IEnumerable<SyncEvent> GetLegacyEventsForUser(string userId, HashSet<string> usernames)
    {
        return LoadEvents()
            .Where(e =>
                string.Equals(e.UserId, userId, StringComparison.OrdinalIgnoreCase) ||
                (usernames.Count > 0 && usernames.Contains(e.Username)));
    }
}

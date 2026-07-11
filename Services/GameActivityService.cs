using System.Text.Json;

namespace GithubLauncher.Services
{
    public class PlaySession
    {
        public DateTime Start { get; set; }
        public long DurationSeconds { get; set; }
        public bool Estimated { get; set; }
    }

    public class GameActivity
    {
        public string Name { get; set; } = string.Empty;
        public List<PlaySession> Sessions { get; set; } = [];
    }

    public class ActivityDatabase
    {
        public Dictionary<string, GameActivity> Games { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, DateTime> OpenSessions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public enum ActivityPeriod
    {
        Day,
        Week,
        Month
    }

    public record ActivityBucket(DateTime PeriodStart, long TotalSeconds);

    public record GameRankingEntry(string Key, string Name, long TotalSeconds, int SessionCount, DateTime? LastPlayed);

    public class GameActivityService
    {
        private readonly string _databasePath;
        private readonly object _lock = new();
        private ActivityDatabase _database;

        public GameActivityService(string baseDirectory)
        {
            _databasePath = Path.Combine(baseDirectory, "activity.json");
            _database = Load(_databasePath);
        }

        private static ActivityDatabase Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<ActivityDatabase>(json) ?? new ActivityDatabase();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load activity.json: {ex.Message}");
            }

            return new ActivityDatabase();
        }

        private void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(_databasePath, JsonSerializer.Serialize(_database, options));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save activity.json: {ex.Message}");
            }
        }

        public void StartSession(string key, string name, DateTime start)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            lock (_lock)
            {
                _database.OpenSessions[key] = start;
                if (!_database.Games.ContainsKey(key))
                {
                    _database.Games[key] = new GameActivity { Name = name };
                }
                else
                {
                    _database.Games[key].Name = name;
                }

                Save();
            }
        }

        public void EndSession(string key, DateTime end)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            lock (_lock)
            {
                if (!_database.OpenSessions.TryGetValue(key, out var start))
                    return;

                _database.OpenSessions.Remove(key);
                AppendSession(key, start, end, estimated: false);
                Save();
            }
        }

        private static readonly TimeSpan MaxEstimatedSessionLength = TimeSpan.FromHours(4);

        public void FinalizeOrphanSessions()
        {
            lock (_lock)
            {
                if (_database.OpenSessions.Count == 0)
                    return;

                var now = DateTime.Now;
                var orphans = _database.OpenSessions.ToList();
                foreach (var (key, start) in orphans)
                {
                    _database.OpenSessions.Remove(key);

                    // No exit signal is available for an orphaned session (e.g. CloseAfterLaunch
                    // closed the launcher before the game exited). The only real data point we
                    // have is how much wall-clock time passed until the launcher was reopened, so
                    // use that as the estimate, capped to avoid absurd values if the launcher stayed
                    // closed for a long time.
                    var cappedNow = start.Add(MaxEstimatedSessionLength) < now
                        ? start.Add(MaxEstimatedSessionLength)
                        : now;

                    AppendSession(key, start, cappedNow, estimated: true);
                }

                Save();
            }
        }

        private void AppendSession(string key, DateTime start, DateTime end, bool estimated)
        {
            var durationSeconds = Math.Max(0, (long)(end - start).TotalSeconds);

            if (!_database.Games.TryGetValue(key, out var activity))
            {
                activity = new GameActivity();
                _database.Games[key] = activity;
            }

            activity.Sessions.Add(new PlaySession
            {
                Start = start,
                DurationSeconds = durationSeconds,
                Estimated = estimated
            });
        }

        public long GetTotalSeconds(string key)
        {
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(key) || !_database.Games.TryGetValue(key, out var activity))
                    return 0;

                return activity.Sessions.Sum(s => s.DurationSeconds);
            }
        }

        public int GetSessionCount(string key)
        {
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(key) || !_database.Games.TryGetValue(key, out var activity))
                    return 0;

                return activity.Sessions.Count;
            }
        }

        public PlaySession? GetLastSession(string key)
        {
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(key) || !_database.Games.TryGetValue(key, out var activity))
                    return null;

                return activity.Sessions.OrderByDescending(s => s.Start).FirstOrDefault();
            }
        }

        public List<PlaySession> GetSessions(string key)
        {
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(key) || !_database.Games.TryGetValue(key, out var activity))
                    return [];

                return activity.Sessions.OrderByDescending(s => s.Start).ToList();
            }
        }

        public List<GameRankingEntry> GetRanking()
        {
            lock (_lock)
            {
                return _database.Games
                    .Select(kvp => new GameRankingEntry(
                        kvp.Key,
                        kvp.Value.Name,
                        kvp.Value.Sessions.Sum(s => s.DurationSeconds),
                        kvp.Value.Sessions.Count,
                        kvp.Value.Sessions.Count > 0 ? kvp.Value.Sessions.Max(s => s.Start) : null))
                    .Where(entry => entry.TotalSeconds > 0)
                    .OrderByDescending(entry => entry.TotalSeconds)
                    .ToList();
            }
        }

        public long GetTotalSecondsAllGames()
        {
            lock (_lock)
            {
                return _database.Games.Values.Sum(g => g.Sessions.Sum(s => s.DurationSeconds));
            }
        }

        public int GetTotalSessionCountAllGames()
        {
            lock (_lock)
            {
                return _database.Games.Values.Sum(g => g.Sessions.Count);
            }
        }

        public List<ActivityBucket> GetAggregatedTotals(ActivityPeriod period, int bucketCount)
        {
            lock (_lock)
            {
                var now = DateTime.Now;
                var bucketStarts = new List<DateTime>();
                for (int i = bucketCount - 1; i >= 0; i--)
                {
                    bucketStarts.Add(period switch
                    {
                        ActivityPeriod.Day => now.Date.AddDays(-i),
                        ActivityPeriod.Week => StartOfWeek(now).AddDays(-7 * i),
                        ActivityPeriod.Month => new DateTime(now.Year, now.Month, 1).AddMonths(-i),
                        _ => now.Date.AddDays(-i)
                    });
                }

                var allSessions = _database.Games.Values.SelectMany(g => g.Sessions).ToList();
                var result = new List<ActivityBucket>();

                foreach (var bucketStart in bucketStarts)
                {
                    var bucketEnd = period switch
                    {
                        ActivityPeriod.Day => bucketStart.AddDays(1),
                        ActivityPeriod.Week => bucketStart.AddDays(7),
                        ActivityPeriod.Month => bucketStart.AddMonths(1),
                        _ => bucketStart.AddDays(1)
                    };

                    var total = allSessions
                        .Where(s => s.Start >= bucketStart && s.Start < bucketEnd)
                        .Sum(s => s.DurationSeconds);

                    result.Add(new ActivityBucket(bucketStart, total));
                }

                return result;
            }
        }

        private static DateTime StartOfWeek(DateTime date)
        {
            var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
            return date.Date.AddDays(-diff);
        }
    }
}

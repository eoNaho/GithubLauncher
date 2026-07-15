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

    public record GameStats(
        string Name,
        long TotalSeconds,
        int SessionCount,
        double AvgSeconds,
        long LongestSeconds,
        DateTime FirstPlayed,
        DateTime LastPlayed,
        int PlayDays);

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

        public List<ActivityBucket> GetAggregatedTotals(ActivityPeriod period, int bucketCount, string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return GetAggregatedTotals(period, bucketCount);

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

                var sessions = _database.Games.TryGetValue(key, out var activity) ? activity.Sessions : [];
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

                    var total = sessions
                        .Where(s => s.Start >= bucketStart && s.Start < bucketEnd)
                        .Sum(s => s.DurationSeconds);

                    result.Add(new ActivityBucket(bucketStart, total));
                }

                return result;
            }
        }

        public double GetAverageSessionSeconds()
        {
            lock (_lock)
            {
                var sessions = _database.Games.Values.SelectMany(g => g.Sessions).ToList();
                return sessions.Count == 0 ? 0 : sessions.Average(s => s.DurationSeconds);
            }
        }

        public int GetCurrentStreakDays()
        {
            lock (_lock)
            {
                var playDays = _database.Games.Values
                    .SelectMany(g => g.Sessions)
                    .Select(s => s.Start.Date)
                    .Distinct()
                    .OrderByDescending(d => d)
                    .ToList();

                if (playDays.Count == 0)
                    return 0;

                var today = DateTime.Now.Date;
                var expected = playDays[0] == today ? today
                    : playDays[0] == today.AddDays(-1) ? today.AddDays(-1)
                    : (DateTime?)null;

                if (expected == null)
                    return 0;

                var streak = 0;
                foreach (var day in playDays)
                {
                    if (day != expected)
                        break;

                    streak++;
                    expected = expected.Value.AddDays(-1);
                }

                return streak;
            }
        }

        public bool DeleteSession(string key, DateTime start, long durationSeconds)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            lock (_lock)
            {
                if (!_database.Games.TryGetValue(key, out var activity))
                    return false;

                var session = activity.Sessions.FirstOrDefault(s => s.Start == start && s.DurationSeconds == durationSeconds);
                if (session == null)
                    return false;

                activity.Sessions.Remove(session);
                Save();
                return true;
            }
        }

        public List<(DateTime Date, long Seconds)> GetDailyTotals(int days, string? key)
        {
            lock (_lock)
            {
                var sessions = string.IsNullOrWhiteSpace(key)
                    ? _database.Games.Values.SelectMany(g => g.Sessions).ToList()
                    : (_database.Games.TryGetValue(key, out var activity) ? activity.Sessions : []);

                var byDay = sessions
                    .GroupBy(s => s.Start.Date)
                    .ToDictionary(g => g.Key, g => g.Sum(s => s.DurationSeconds));

                var today = DateTime.Now.Date;
                var result = new List<(DateTime Date, long Seconds)>();
                for (int i = days - 1; i >= 0; i--)
                {
                    var date = today.AddDays(-i);
                    result.Add((date, byDay.TryGetValue(date, out var seconds) ? seconds : 0));
                }

                return result;
            }
        }

        public long[] GetDayOfWeekTotals(string? key)
        {
            lock (_lock)
            {
                var sessions = string.IsNullOrWhiteSpace(key)
                    ? _database.Games.Values.SelectMany(g => g.Sessions)
                    : (_database.Games.TryGetValue(key, out var activity) ? activity.Sessions : []);

                var totals = new long[7];
                foreach (var session in sessions)
                {
                    var index = ((int)session.Start.DayOfWeek + 6) % 7; // Monday = 0
                    totals[index] += session.DurationSeconds;
                }

                return totals;
            }
        }

        public long[] GetHourOfDayTotals(string? key)
        {
            lock (_lock)
            {
                var sessions = string.IsNullOrWhiteSpace(key)
                    ? _database.Games.Values.SelectMany(g => g.Sessions)
                    : (_database.Games.TryGetValue(key, out var activity) ? activity.Sessions : []);

                var totals = new long[24];
                foreach (var session in sessions)
                {
                    totals[session.Start.Hour] += session.DurationSeconds;
                }

                return totals;
            }
        }

        public GameStats? GetGameStats(string key)
        {
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(key) || !_database.Games.TryGetValue(key, out var activity) || activity.Sessions.Count == 0)
                    return null;

                var sessions = activity.Sessions;
                return new GameStats(
                    activity.Name,
                    sessions.Sum(s => s.DurationSeconds),
                    sessions.Count,
                    sessions.Average(s => s.DurationSeconds),
                    sessions.Max(s => s.DurationSeconds),
                    sessions.Min(s => s.Start),
                    sessions.Max(s => s.Start),
                    sessions.Select(s => s.Start.Date).Distinct().Count());
            }
        }

        private static DateTime StartOfWeek(DateTime date)
        {
            var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
            return date.Date.AddDays(-diff);
        }
    }
}

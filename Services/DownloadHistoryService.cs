using System.Text.Json;

namespace GithubLauncher.Services
{
    public record DownloadRecord(string Name, string Repository, string Version, DateTime CompletedAt, string Kind);

    public class DownloadHistoryService
    {
        private const int MaxRecords = 200;

        private readonly string _databasePath;
        private readonly object _lock = new();
        private List<DownloadRecord> _records;

        public DownloadHistoryService(string baseDirectory)
        {
            _databasePath = Path.Combine(baseDirectory, "downloads.json");
            _records = Load(_databasePath);
        }

        private static List<DownloadRecord> Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<List<DownloadRecord>>(json) ?? [];
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load downloads.json: {ex.Message}");
            }

            return [];
        }

        private void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(_databasePath, JsonSerializer.Serialize(_records, options));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save downloads.json: {ex.Message}");
            }
        }

        public void AddRecord(string name, string repository, string version, string kind)
        {
            lock (_lock)
            {
                _records.Insert(0, new DownloadRecord(name, repository, version, DateTime.Now, kind));

                if (_records.Count > MaxRecords)
                {
                    _records = _records.Take(MaxRecords).ToList();
                }

                Save();
            }
        }

        public List<DownloadRecord> GetRecords()
        {
            lock (_lock)
            {
                return [.. _records];
            }
        }

        public void ClearHistory()
        {
            lock (_lock)
            {
                _records.Clear();
                Save();
            }
        }
    }
}

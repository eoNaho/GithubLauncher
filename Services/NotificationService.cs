using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace GithubLauncher.Services
{
    public record NotificationRecord(string Title, string Body, DateTime Timestamp, string Kind);

    public class NotificationService
    {
        private const int MaxRecords = 200;

        private volatile bool _enabled;
        private readonly HashSet<string> _notifiedUpdateKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _historyPath;
        private readonly object _lock = new();
        private List<NotificationRecord> _records;
        private int _unreadCount;

        public event Action? Changed;

        public int UnreadCount
        {
            get { lock (_lock) return _unreadCount; }
        }

        public NotificationService(string baseDirectory, bool enabled)
        {
            _enabled = enabled;
            _historyPath = Path.Combine(baseDirectory, "notifications.json");
            _records = Load(_historyPath);
        }

        private static List<NotificationRecord> Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<List<NotificationRecord>>(json) ?? [];
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load notifications.json: {ex.Message}");
            }

            return [];
        }

        private void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(_historyPath, JsonSerializer.Serialize(_records, options));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save notifications.json: {ex.Message}");
            }
        }

        public List<NotificationRecord> GetHistory()
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

            Changed?.Invoke();
        }

        public void MarkAllRead()
        {
            lock (_lock)
            {
                if (_unreadCount == 0)
                    return;

                _unreadCount = 0;
            }

            Changed?.Invoke();
        }

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
        }

        public void NotifyDownloadComplete(string name, string kind)
        {
            var title = kind == "Update" ? "Update installed" : "Download complete";
            Send(title, name, "DownloadComplete");
        }

        public void NotifyDownloadError(string name, string reason)
        {
            Send($"Failed to install {name}", reason, "DownloadError");
        }

        public void NotifyUpdateAvailable(string name, string version)
        {
            var key = $"{name}|{version}";
            lock (_notifiedUpdateKeys)
            {
                if (!_notifiedUpdateKeys.Add(key))
                    return;
            }

            Send("Update available", $"{name} {version}", "UpdateAvailable");
        }

        public void NotifyStreakMilestone(int days)
        {
            Send("Streak milestone", $"{days} day{(days == 1 ? "" : "s")} in a row playing!", "Streak");
        }

        private void Send(string title, string body, string kind)
        {
            RecordHistory(title, body, kind);

            if (!_enabled)
                return;

            _ = Task.Run(() =>
            {
                try
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        SendLinux(title, body);
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        SendMac(title, body);
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        SendWindows(title, body);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to send notification: {ex.Message}");
                }
            });
        }

        private void RecordHistory(string title, string body, string kind)
        {
            lock (_lock)
            {
                _records.Insert(0, new NotificationRecord(title, body, DateTime.Now, kind));

                if (_records.Count > MaxRecords)
                {
                    _records = _records.Take(MaxRecords).ToList();
                }

                _unreadCount++;
                Save();
            }

            Changed?.Invoke();
        }

        private static void SendLinux(string title, string body)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "notify-send",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-a");
            psi.ArgumentList.Add("GithubLauncher");
            psi.ArgumentList.Add(title);
            psi.ArgumentList.Add(body);

            using var process = Process.Start(psi);
        }

        private static void SendMac(string title, string body)
        {
            var script = $"display notification {EscapeAppleScriptString(body)} with title {EscapeAppleScriptString(title)}";
            var psi = new ProcessStartInfo
            {
                FileName = "osascript",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add(script);

            using var process = Process.Start(psi);
        }

        private static string EscapeAppleScriptString(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static void SendWindows(string title, string body)
        {
            var script = $@"
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null
$template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02)
$textNodes = $template.GetElementsByTagName('text')
$textNodes.Item(0).AppendChild($template.CreateTextNode('{EscapePowerShellString(title)}')) > $null
$textNodes.Item(1).AppendChild($template.CreateTextNode('{EscapePowerShellString(body)}')) > $null
$toast = [Windows.UI.Notifications.ToastNotification]::new($template)
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('GithubLauncher').Show($toast)
";
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(script);

            using var process = Process.Start(psi);
        }

        private static string EscapePowerShellString(string value)
        {
            return value.Replace("'", "''");
        }
    }
}

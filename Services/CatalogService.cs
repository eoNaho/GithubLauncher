using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace GithubLauncher.Services
{
    public record CatalogEntry(string Name, string Repository, string FolderName, string AppIconUrl, string Category);

    public class CatalogService
    {
        private readonly HttpClient _httpClient;

        public CatalogService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<string> FetchLatestTagAsync(string repo, string? gitHubApiToken, CancellationToken ct)
        {
            string url = $"https://api.github.com/repos/{repo}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(gitHubApiToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("token", gitHubApiToken);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("tag_name", out var tag))
                return tag.GetString() ?? string.Empty;
            return string.Empty;
        }

        public async Task<string> FetchCatalogJsonAsync(string repo, string tag, string? gitHubApiToken, CancellationToken ct)
        {
            string url = $"https://github.com/{repo}/releases/download/{tag}/apps.json";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(gitHubApiToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("token", gitHubApiToken);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }

        public static List<(string Category, List<CatalogEntry> Entries)> ParseCatalogJson(string json)
        {
            // Parse errors (JsonException) propagate to the caller so an invalid
            // catalog is reported distinctly from a genuinely empty one.
            var result = new List<(string, List<CatalogEntry>)>();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var section in root.EnumerateObject())
                {
                    var entries = new List<CatalogEntry>();
                    if (section.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in section.Value.EnumerateArray())
                        {
                            string name = item.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                            string repo = item.TryGetProperty("repository", out var r) ? r.GetString() ?? string.Empty : string.Empty;
                            string folder = item.TryGetProperty("folderName", out var f) ? f.GetString() ?? string.Empty : string.Empty;
                            string icon = string.Empty;
                            if (item.TryGetProperty("gameIconUrl", out var gi)) icon = gi.GetString() ?? string.Empty;
                            else if (item.TryGetProperty("appIconUrl", out var ai)) icon = ai.GetString() ?? string.Empty;
                            string category = item.TryGetProperty("category", out var c) ? c.GetString() ?? string.Empty : string.Empty;

                            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(repo))
                                entries.Add(new CatalogEntry(name, repo, folder, icon, category));
                        }
                    }
                    if (entries.Count > 0)
                        result.Add((section.Name, entries));
                }
            }
            return result;
        }
    }
}

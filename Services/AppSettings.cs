using System;
using System.IO;
using System.Text.Json;
using GitHubLauncher.Core.Models;

namespace GithubLauncher
{
    public class AppSettings
    {
        public bool FirstStartup { get; set; } = true;
        public bool IconFill { get; set; } = true;
        public bool UseGridView { get; set; } = true;
        public float IconOpacity { get; set; } = 1.0f;
        public int IconSize { get; set; } = 220;
        public int IconMargin { get; set; } = 8;
        public int SlotTextMargin { get; set; } = 112;
        public int SlotSize { get; set; } = 220;
        public bool WindowBorderRounding { get; set; } = true;
        public bool ShowOSTopBar { get; set; } = false;
        public string PrimaryColor { get; set; } = "#18181b";
        public string SecondaryColor { get; set; } = "#404040";
        public string ThemeMode { get; set; } = "Dark";
        public ThemeSettings Theme { get; set; } = ThemeSettings.CreateDarkPreset();
        public TargetOS Platform { get; set; } = TargetOS.Auto;
        public List<string> HiddenApps { get; set; } = new List<string>();
        public List<string> ManuallyHiddenApps { get; set; } = new List<string>();
        public string AppsPath { get; set; } = string.Empty;
        public string GitHubApiToken { get; set; } = string.Empty;
        public string GitLabApiToken { get; set; } = string.Empty;
        public string SortBy { get; set; } = "LastPlayed";
        public bool StartFullscreen { get; set; } = false;
        public bool CloseAfterLaunch {  get; set; } = false;
        public string BackgroundImagePath { get; set; } = string.Empty;
        public string LauncherMusicPath { get; set; } = string.Empty;
        public float MusicVolume { get; set; } = 0.2f;
        public float BackgroundOpacity { get; set; } = 0.15f;
        public bool EnableGamepadInput { get; set; } = true;
        public bool EnableNotifications { get; set; } = true;
        public int LastNotifiedStreak { get; set; } = 0;
        public string LinuxWindowsLaunchCommand { get; set; } = string.Empty;
        public string AppListRepository { get; set; } = "SirDiabo/GHLAppList"; // legacy — kept for migration only
        public List<string> AppListRepositories { get; set; } = DefaultAppListRepositories();

        public static List<string> DefaultAppListRepositories() => new List<string> { "SirDiabo/GHLAppList", "eoNaho/AppList-custom" };
        public string AppListCachedVersion { get; set; } = string.Empty;
        private static readonly string SettingsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "settings.json"
        );

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    bool hasThemeObject = false;
                    try
                    {
                        using var document = JsonDocument.Parse(json);
                        hasThemeObject = document.RootElement.TryGetProperty(nameof(Theme), out _);
                    }
                    catch
                    {
                        hasThemeObject = false;
                    }

                    var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                    settings.NormalizeTheme(migrateLegacyColors: !hasThemeObject);

                    // Migration: if AppListRepositories not present in JSON, promote AppListRepository
                    bool hasReposList = false;
                    try
                    {
                        using var doc2 = JsonDocument.Parse(json);
                        hasReposList = doc2.RootElement.TryGetProperty(nameof(AppListRepositories), out _);
                    }
                    catch { }

                    if (!hasReposList)
                    {
                        settings.AppListRepositories = string.IsNullOrWhiteSpace(settings.AppListRepository)
                            ? DefaultAppListRepositories()
                            : new List<string> { settings.AppListRepository };
                    }
                    else if (settings.AppListRepositories == null || settings.AppListRepositories.Count == 0)
                    {
                        settings.AppListRepositories = DefaultAppListRepositories();
                    }

                    return settings;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            }

            var defaultSettings = new AppSettings();
            defaultSettings.NormalizeTheme();
            return defaultSettings;
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
                throw;
            }
        }

        public void NormalizeTheme(bool migrateLegacyColors = false)
        {
            if (migrateLegacyColors || Theme == null)
            {
                Theme = ThemeMode == "Light"
                    ? ThemeSettings.CreateLightPreset()
                    : ThemeSettings.CreateDarkPreset();
            }

            Theme.Mode = string.IsNullOrWhiteSpace(Theme.Mode) ? ThemeMode : Theme.Mode;
            Theme.PresetName = string.IsNullOrWhiteSpace(Theme.PresetName) ? "Custom" : Theme.PresetName;
            Theme.Tokens ??= ThemeTokens.CreateDark();
            Theme.Layout ??= new ThemeLayoutSettings();

            Theme.Tokens.EnsureDefaults();
            Theme.Layout.EnsureDefaults();

            if (!string.IsNullOrWhiteSpace(ThemeMode))
                Theme.Mode = ThemeMode;

            if (migrateLegacyColors && !string.IsNullOrWhiteSpace(PrimaryColor))
            {
                Theme.Tokens.BackgroundBase = PrimaryColor;
                Theme.Tokens.Surface = PrimaryColor;
            }

            if (migrateLegacyColors && !string.IsNullOrWhiteSpace(SecondaryColor))
                Theme.Tokens.Border = SecondaryColor;
        }
    }

    public class ThemeSettings
    {
        public string Mode { get; set; } = "Dark";
        public string PresetName { get; set; } = "GitHub Dark";
        public ThemeTokens Tokens { get; set; } = ThemeTokens.CreateDark();
        public ThemeLayoutSettings Layout { get; set; } = new();

        public static ThemeSettings CreateDarkPreset() => new()
        {
            Mode = "Dark",
            PresetName = "GitHub Dark",
            Tokens = ThemeTokens.CreateDark(),
            Layout = new ThemeLayoutSettings()
        };

        public static ThemeSettings CreateLightPreset() => new()
        {
            Mode = "Light",
            PresetName = "GitHub Light",
            Tokens = ThemeTokens.CreateLight(),
            Layout = new ThemeLayoutSettings()
        };

        public static ThemeSettings CreateMidnightPreset() => new()
        {
            Mode = "Dark",
            PresetName = "Midnight Purple",
            Tokens = new ThemeTokens
            {
                BackgroundBase = "#0f111a",
                SidebarBackground = "#151722",
                Surface = "#1e2130",
                SurfaceAlt = "#2a2d3e",
                InputBackground = "#1a1d27",
                Hover = "#262a3d",
                Border = "#252938",
                Accent = "#6941f5",
                AccentHover = "#5835d6",
                TextPrimary = "#ffffff",
                TextSecondary = "#8b949e",
                TextMuted = "#8b949e",
                StatusInstalled = "#2ea043",
                StatusUpdate = "#f59e0b",
                StatusDownload = "#8b949e",
                StatusNotInstalled = "#3b82f6"
            },
            Layout = new ThemeLayoutSettings()
        };
    }

    public class ThemeTokens
    {
        public string BackgroundBase { get; set; } = "#0f111a";
        public string SidebarBackground { get; set; } = "#151722";
        public string Surface { get; set; } = "#1e2130";
        public string SurfaceAlt { get; set; } = "#2a2d3e";
        public string InputBackground { get; set; } = "#1a1d27";
        public string Hover { get; set; } = "#262a3d";
        public string Border { get; set; } = "#252938";
        public string Accent { get; set; } = "#6941f5";
        public string AccentHover { get; set; } = "#5835d6";
        public string TextPrimary { get; set; } = "#ffffff";
        public string TextSecondary { get; set; } = "#8b949e";
        public string TextMuted { get; set; } = "#8b949e";
        public string StatusInstalled { get; set; } = "#2ea043";
        public string StatusUpdate { get; set; } = "#f59e0b";
        public string StatusDownload { get; set; } = "#8b949e";
        public string StatusNotInstalled { get; set; } = "#3b82f6";

        public static ThemeTokens CreateDark() => new();

        public static ThemeTokens CreateLight() => new()
        {
            BackgroundBase = "#f4f6fb",
            SidebarBackground = "#ffffff",
            Surface = "#ffffff",
            SurfaceAlt = "#edf1f7",
            InputBackground = "#f8fafc",
            Hover = "#e6ebf3",
            Border = "#cfd8e3",
            Accent = "#4f46e5",
            AccentHover = "#4338ca",
            TextPrimary = "#111827",
            TextSecondary = "#4b5563",
            TextMuted = "#6b7280",
            StatusInstalled = "#15803d",
            StatusUpdate = "#d97706",
            StatusDownload = "#64748b",
            StatusNotInstalled = "#2563eb"
        };

        public void EnsureDefaults()
        {
            var defaults = CreateDark();
            BackgroundBase = NormalizeHex(BackgroundBase, defaults.BackgroundBase);
            SidebarBackground = NormalizeHex(SidebarBackground, defaults.SidebarBackground);
            Surface = NormalizeHex(Surface, defaults.Surface);
            SurfaceAlt = NormalizeHex(SurfaceAlt, defaults.SurfaceAlt);
            InputBackground = NormalizeHex(InputBackground, defaults.InputBackground);
            Hover = NormalizeHex(Hover, defaults.Hover);
            Border = NormalizeHex(Border, defaults.Border);
            Accent = NormalizeHex(Accent, defaults.Accent);
            AccentHover = NormalizeHex(AccentHover, defaults.AccentHover);
            TextPrimary = NormalizeHex(TextPrimary, defaults.TextPrimary);
            TextSecondary = NormalizeHex(TextSecondary, defaults.TextSecondary);
            TextMuted = NormalizeHex(TextMuted, defaults.TextMuted);
            StatusInstalled = NormalizeHex(StatusInstalled, defaults.StatusInstalled);
            StatusUpdate = NormalizeHex(StatusUpdate, defaults.StatusUpdate);
            StatusDownload = NormalizeHex(StatusDownload, defaults.StatusDownload);
            StatusNotInstalled = NormalizeHex(StatusNotInstalled, defaults.StatusNotInstalled);
        }

        private static string NormalizeHex(string? value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            var trimmed = value.Trim();
            if (!trimmed.StartsWith("#", StringComparison.Ordinal))
                trimmed = "#" + trimmed;

            return trimmed.Length == 7 ? trimmed : fallback;
        }
    }

    public class ThemeLayoutSettings
    {
        public double CardRadius { get; set; } = 12;
        public double ControlRadius { get; set; } = 8;
        public double SurfaceOpacity { get; set; } = 1.0;
        public double SidebarWidth { get; set; } = 260;
        public double MainPadding { get; set; } = 28;
        public double CardDensity { get; set; } = 1.0;

        public void EnsureDefaults()
        {
            CardRadius = ClampOrDefault(CardRadius, 0, 32, 12);
            ControlRadius = ClampOrDefault(ControlRadius, 0, 24, 8);
            SurfaceOpacity = ClampOrDefault(SurfaceOpacity, 0.2, 1, 1);
            SidebarWidth = ClampOrDefault(SidebarWidth, 240, 360, 260);
            MainPadding = ClampOrDefault(MainPadding, 8, 48, 28);
            CardDensity = ClampOrDefault(CardDensity, 0.75, 1.35, 1);
        }

        private static double ClampOrDefault(double value, double min, double max, double fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return fallback;

            return Math.Min(max, Math.Max(min, value));
        }
    }
}

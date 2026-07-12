using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace GithubLauncher
{
    public static class ThemeService
    {
        public static Dictionary<string, object> BuildResources(ThemeSettings theme, ThemeVariant actualThemeVariant)
        {
            theme ??= ThemeSettings.CreateDarkPreset();
            theme.Tokens ??= ThemeTokens.CreateDark();
            theme.Layout ??= new ThemeLayoutSettings();
            theme.Tokens.EnsureDefaults();
            theme.Layout.EnsureDefaults();

            var tokens = theme.Tokens;
            var layout = theme.Layout;
            var surfaceOpacity = layout.SurfaceOpacity;

            var resources = new Dictionary<string, object>
            {
                ["BackgroundBase"] = Brush(tokens.BackgroundBase),
                ["SidebarBackground"] = Brush(tokens.SidebarBackground),
                ["Surface"] = Brush(tokens.Surface, surfaceOpacity),
                ["SurfaceAlt"] = Brush(tokens.SurfaceAlt, surfaceOpacity),
                ["InputBackground"] = Brush(tokens.InputBackground),
                ["Hover"] = Brush(tokens.Hover),
                ["Border"] = Brush(tokens.Border),
                ["Accent"] = Brush(tokens.Accent),
                ["AccentHover"] = Brush(tokens.AccentHover),
                ["TextPrimary"] = Brush(tokens.TextPrimary),
                ["TextSecondary"] = Brush(tokens.TextSecondary),
                ["TextMuted"] = Brush(tokens.TextMuted),
                ["StatusInstalled"] = Brush(tokens.StatusInstalled),
                ["StatusUpdate"] = Brush(tokens.StatusUpdate),
                ["StatusDownload"] = Brush(tokens.StatusDownload),
                ["StatusNotInstalled"] = Brush(tokens.StatusNotInstalled),
                ["CardRadius"] = new CornerRadius(layout.CardRadius),
                ["ControlRadius"] = new CornerRadius(layout.ControlRadius),
                ["SidebarWidth"] = new GridLength(layout.SidebarWidth),
                ["SidebarWidthValue"] = layout.SidebarWidth,
                ["MainContentPadding"] = new Thickness(layout.MainPadding, Math.Max(12, layout.MainPadding - 8), layout.MainPadding, layout.MainPadding),
                ["CardSpacing"] = new Thickness(10 * layout.CardDensity),
                ["CardBottomSpacing"] = new Thickness(0, 0, 0, 12 * layout.CardDensity)
            };

            AddLegacyAliases(resources);
            return resources;
        }

        public static ThemeSettings CreatePreset(string presetName, string currentMode)
        {
            var preset = presetName switch
            {
                "GitHub Light" => ThemeSettings.CreateLightPreset(),
                "Midnight Purple" => ThemeSettings.CreateMidnightPreset(),
                _ => ThemeSettings.CreateDarkPreset()
            };

            preset.Mode = string.IsNullOrWhiteSpace(currentMode) ? preset.Mode : currentMode;
            return preset;
        }

        private static void AddLegacyAliases(Dictionary<string, object> resources)
        {
            resources["ThemeBase"] = resources["Surface"];
            resources["ThemeLighter"] = resources["Hover"];
            resources["ThemeDarker"] = resources["BackgroundBase"];
            resources["ThemeBorder"] = resources["Border"];
            resources["ThemeText"] = resources["TextPrimary"];
            resources["ThemeTextSecondary"] = resources["TextSecondary"];
            resources["StatusInstalledBrush"] = resources["StatusInstalled"];
            resources["StatusUpdateBrush"] = resources["StatusUpdate"];
            resources["StatusDownloadingBrush"] = resources["StatusDownload"];
            resources["StatusNotInstalledBrush"] = resources["StatusNotInstalled"];
        }

        private static SolidColorBrush Brush(string hex, double opacity = 1)
        {
            try
            {
                return new SolidColorBrush(Color.Parse(hex)) { Opacity = opacity };
            }
            catch
            {
                return new SolidColorBrush(Colors.Transparent) { Opacity = opacity };
            }
        }
    }
}

using System.Drawing;

namespace NoFences.Theming
{
    /// <summary>
    /// Stable theme identifiers are persisted instead of localized display names.
    /// Third-party or future built-in themes can use their own unique identifier.
    /// </summary>
    public static class ThemeIds
    {
        public const string Windows11 = "windows11";
        public const string WindowsXp = "windowsxp";
        public const string Custom = "custom";
    }

    /// <summary>
    /// Extension point for new themes. Register an implementation with
    /// ThemeManager.RegisterTheme; no fence or dialog code needs to change.
    /// </summary>
    public interface IThemeProvider
    {
        string Id { get; }

        string DisplayName { get; }

        ThemeDefinition CreateTheme();
    }

    internal sealed class StaticThemeProvider : IThemeProvider
    {
        private readonly ThemeDefinition definition;

        public StaticThemeProvider(string id, string displayName, ThemeDefinition definition)
        {
            Id = id;
            DisplayName = displayName;
            this.definition = definition.Clone();
        }

        public string Id { get; }

        public string DisplayName { get; }

        public ThemeDefinition CreateTheme()
        {
            return definition.Clone();
        }
    }

    /// <summary>
    /// Factory for the bundled themes. Keeping preset construction in one place
    /// makes it straightforward to tune a style or add another provider later.
    /// </summary>
    public static class ThemePresets
    {
        public static ThemeDefinition CreateWindows11()
        {
            return new ThemeDefinition
            {
                Name = "Windows 11",
                FontFamilyName = "Segoe UI",
                MainPanelColorArgb = Color.FromArgb(32, 32, 32).ToArgb(),
                TitleBarColorArgb = Color.FromArgb(24, 24, 24).ToArgb(),
                TitleTextColorArgb = Color.White.ToArgb(),
                ItemTextColorArgb = Color.White.ToArgb(),
                ItemTextShadowColorArgb = Color.Black.ToArgb(),
                ItemHoverColorArgb = Color.FromArgb(61, 111, 158).ToArgb(),
                ItemSelectedColorArgb = Color.FromArgb(0, 103, 192).ToArgb(),
                BorderColorArgb = Color.FromArgb(139, 190, 241).ToArgb(),
                ScrollBarColorArgb = Color.FromArgb(133, 133, 133).ToArgb(),
                MenuBackgroundColorArgb = Color.FromArgb(32, 32, 32).ToArgb(),
                MenuTextColorArgb = Color.FromArgb(245, 245, 245).ToArgb(),
                MenuHighlightColorArgb = Color.FromArgb(61, 61, 61).ToArgb(),
                MenuHighlightTextColorArgb = Color.White.ToArgb(),
                DialogBackgroundColorArgb = Color.FromArgb(32, 32, 32).ToArgb(),
                DialogTextColorArgb = Color.FromArgb(245, 245, 245).ToArgb(),
                ControlBackgroundColorArgb = Color.FromArgb(45, 45, 48).ToArgb(),
                ControlTextColorArgb = Color.White.ToArgb(),
                AccentColorArgb = Color.FromArgb(96, 205, 255).ToArgb(),
                MainPanelOpacityPercent = 78,
                TitleBarOpacityPercent = 72,
                CornerRadius = 12,
                EnableBlur = true,
                PreferDarkNativeMenus = true,
                BackgroundImageLayout = ThemeImageLayout.Fill,
                BackgroundImageOpacityPercent = 35
            };
        }

        public static ThemeDefinition CreateWindowsXp()
        {
            return new ThemeDefinition
            {
                Name = "Windows XP",
                FontFamilyName = "Tahoma",
                MainPanelColorArgb = Color.FromArgb(236, 233, 216).ToArgb(),
                TitleBarColorArgb = Color.FromArgb(0, 84, 227).ToArgb(),
                TitleTextColorArgb = Color.White.ToArgb(),
                ItemTextColorArgb = Color.FromArgb(20, 20, 20).ToArgb(),
                ItemTextShadowColorArgb = Color.White.ToArgb(),
                ItemHoverColorArgb = Color.FromArgb(164, 201, 255).ToArgb(),
                ItemSelectedColorArgb = Color.FromArgb(49, 106, 197).ToArgb(),
                BorderColorArgb = Color.FromArgb(127, 157, 185).ToArgb(),
                ScrollBarColorArgb = Color.FromArgb(172, 168, 153).ToArgb(),
                MenuBackgroundColorArgb = Color.FromArgb(245, 244, 234).ToArgb(),
                MenuTextColorArgb = Color.Black.ToArgb(),
                MenuHighlightColorArgb = Color.FromArgb(49, 106, 197).ToArgb(),
                MenuHighlightTextColorArgb = Color.White.ToArgb(),
                DialogBackgroundColorArgb = Color.FromArgb(236, 233, 216).ToArgb(),
                DialogTextColorArgb = Color.Black.ToArgb(),
                ControlBackgroundColorArgb = Color.White.ToArgb(),
                ControlTextColorArgb = Color.Black.ToArgb(),
                AccentColorArgb = Color.FromArgb(0, 84, 227).ToArgb(),
                MainPanelOpacityPercent = 100,
                TitleBarOpacityPercent = 100,
                CornerRadius = 0,
                EnableBlur = false,
                PreferDarkNativeMenus = false,
                BackgroundImageLayout = ThemeImageLayout.Fill,
                BackgroundImageOpacityPercent = 25
            };
        }

        public static ThemeDefinition CreateDefaultCustom()
        {
            var custom = CreateWindows11();
            custom.Name = "Custom";
            return custom;
        }
    }
}

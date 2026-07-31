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
    /// Extension point for new visual styles.  Color mode is an input instead of
    /// part of the provider ID, so registering one new style automatically fits
    /// the application's independent Light/Dark switch.
    /// </summary>
    public interface IThemeProvider
    {
        string Id { get; }

        string DisplayName { get; }

        ThemeDefinition CreateTheme(ThemeColorMode colorMode);
    }

    /// <summary>
    /// Simple provider used by bundled styles.  Both variants have the same style
    /// semantics (font, radius, opacity and effects); only their palettes differ.
    /// </summary>
    internal sealed class StaticThemeProvider : IThemeProvider
    {
        private readonly ThemeDefinition lightDefinition;
        private readonly ThemeDefinition darkDefinition;

        public StaticThemeProvider(
            string id,
            string displayName,
            ThemeDefinition lightDefinition,
            ThemeDefinition darkDefinition)
        {
            Id = id;
            DisplayName = displayName;
            this.lightDefinition = lightDefinition.Clone();
            this.darkDefinition = darkDefinition.Clone();
        }

        public string Id { get; }

        public string DisplayName { get; }

        public ThemeDefinition CreateTheme(ThemeColorMode colorMode)
        {
            return colorMode == ThemeColorMode.Dark
                ? darkDefinition.Clone()
                : lightDefinition.Clone();
        }
    }

    /// <summary>
    /// Factory for bundled styles and their independent color-mode variants.
    ///
    /// Windows 11 values follow the semantic WinUI resources: light surfaces use
    /// SolidBackgroundFillColorBase (#F3F3F3), dark surfaces use #202020, and the
    /// associated foreground/control surfaces keep readable contrast.  Windows XP
    /// light values follow the Luna palette; its dark variant retains Luna's blue
    /// accents and square geometry while using dark, high-contrast surfaces.
    /// </summary>
    public static class ThemePresets
    {
        /// <summary>
        /// Compatibility helper and default preset.  Windows 11 is a visual style,
        /// not an alias for dark mode, so the parameterless form is always light.
        /// </summary>
        public static ThemeDefinition CreateWindows11()
        {
            return CreateWindows11(ThemeColorMode.Light);
        }

        public static ThemeDefinition CreateWindows11(ThemeColorMode colorMode)
        {
            return colorMode == ThemeColorMode.Dark
                ? CreateWindows11Dark()
                : CreateWindows11Light();
        }

        public static ThemeDefinition CreateWindows11Light()
        {
            return new ThemeDefinition
            {
                Name = "Windows 11",
                FontFamilyName = "Segoe UI",
                MainPanelColorArgb = Color.FromArgb(243, 243, 243).ToArgb(),
                TitleBarColorArgb = Color.FromArgb(249, 249, 249).ToArgb(),
                TitleTextColorArgb = Color.FromArgb(26, 26, 26).ToArgb(),
                ItemTextColorArgb = Color.FromArgb(26, 26, 26).ToArgb(),
                ItemTextShadowColorArgb = Color.White.ToArgb(),
                ItemHoverColorArgb = Color.FromArgb(229, 243, 255).ToArgb(),
                ItemSelectedColorArgb = Color.FromArgb(0, 103, 192).ToArgb(),
                BorderColorArgb = Color.FromArgb(117, 117, 117).ToArgb(),
                ScrollBarColorArgb = Color.FromArgb(138, 138, 138).ToArgb(),
                MenuBackgroundColorArgb = Color.FromArgb(249, 249, 249).ToArgb(),
                MenuTextColorArgb = Color.FromArgb(26, 26, 26).ToArgb(),
                MenuHighlightColorArgb = Color.FromArgb(229, 243, 255).ToArgb(),
                MenuHighlightTextColorArgb = Color.FromArgb(26, 26, 26).ToArgb(),
                DialogBackgroundColorArgb = Color.FromArgb(243, 243, 243).ToArgb(),
                DialogTextColorArgb = Color.FromArgb(26, 26, 26).ToArgb(),
                ControlBackgroundColorArgb = Color.White.ToArgb(),
                ControlTextColorArgb = Color.FromArgb(26, 26, 26).ToArgb(),
                AccentColorArgb = Color.FromArgb(0, 103, 192).ToArgb(),
                MainPanelOpacityPercent = 86,
                TitleBarOpacityPercent = 82,
                CornerRadius = 12,
                EnableBlur = true,
                BackgroundImageLayout = ThemeImageLayout.Fill,
                BackgroundImageOpacityPercent = 35
            };
        }

        public static ThemeDefinition CreateWindows11Dark()
        {
            // Start with the light style so color mode can never accidentally
            // change Win11 geometry, opacity, font or background-image behavior.
            var theme = CreateWindows11Light();
            theme.MainPanelColorArgb = Color.FromArgb(32, 32, 32).ToArgb();
            theme.TitleBarColorArgb = Color.FromArgb(28, 28, 28).ToArgb();
            theme.TitleTextColorArgb = Color.White.ToArgb();
            theme.ItemTextColorArgb = Color.White.ToArgb();
            theme.ItemTextShadowColorArgb = Color.Black.ToArgb();
            theme.ItemHoverColorArgb = Color.FromArgb(58, 58, 58).ToArgb();
            theme.ItemSelectedColorArgb = Color.FromArgb(0, 95, 184).ToArgb();
            theme.BorderColorArgb = Color.FromArgb(117, 117, 117).ToArgb();
            theme.ScrollBarColorArgb = Color.FromArgb(157, 157, 157).ToArgb();
            theme.MenuBackgroundColorArgb = Color.FromArgb(44, 44, 44).ToArgb();
            theme.MenuTextColorArgb = Color.White.ToArgb();
            theme.MenuHighlightColorArgb = Color.FromArgb(69, 69, 69).ToArgb();
            theme.MenuHighlightTextColorArgb = Color.White.ToArgb();
            theme.DialogBackgroundColorArgb = Color.FromArgb(32, 32, 32).ToArgb();
            theme.DialogTextColorArgb = Color.White.ToArgb();
            theme.ControlBackgroundColorArgb = Color.FromArgb(45, 45, 45).ToArgb();
            theme.ControlTextColorArgb = Color.White.ToArgb();
            theme.AccentColorArgb = Color.FromArgb(96, 205, 255).ToArgb();
            return theme;
        }

        public static ThemeDefinition CreateWindowsXp()
        {
            return CreateWindowsXp(ThemeColorMode.Light);
        }

        public static ThemeDefinition CreateWindowsXp(ThemeColorMode colorMode)
        {
            return colorMode == ThemeColorMode.Dark
                ? CreateWindowsXpDark()
                : CreateWindowsXpLight();
        }

        public static ThemeDefinition CreateWindowsXpLight()
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
                BackgroundImageLayout = ThemeImageLayout.Fill,
                BackgroundImageOpacityPercent = 25
            };
        }

        public static ThemeDefinition CreateWindowsXpDark()
        {
            // Windows XP had no official dark mode.  This variant preserves Luna's
            // Tahoma font, square corners and blue interaction colors, while using
            // the modern dark surface/text relationship required by the app-wide
            // color-mode switch.
            var theme = CreateWindowsXpLight();
            theme.MainPanelColorArgb = Color.FromArgb(32, 32, 32).ToArgb();
            theme.TitleBarColorArgb = Color.FromArgb(0, 60, 116).ToArgb();
            theme.TitleTextColorArgb = Color.White.ToArgb();
            theme.ItemTextColorArgb = Color.White.ToArgb();
            theme.ItemTextShadowColorArgb = Color.Black.ToArgb();
            theme.ItemHoverColorArgb = Color.FromArgb(31, 59, 83).ToArgb();
            theme.ItemSelectedColorArgb = Color.FromArgb(49, 106, 197).ToArgb();
            theme.BorderColorArgb = Color.FromArgb(104, 140, 175).ToArgb();
            theme.ScrollBarColorArgb = Color.FromArgb(114, 131, 141).ToArgb();
            theme.MenuBackgroundColorArgb = Color.FromArgb(44, 44, 44).ToArgb();
            theme.MenuTextColorArgb = Color.White.ToArgb();
            theme.MenuHighlightColorArgb = Color.FromArgb(49, 106, 197).ToArgb();
            theme.MenuHighlightTextColorArgb = Color.White.ToArgb();
            theme.DialogBackgroundColorArgb = Color.FromArgb(32, 32, 32).ToArgb();
            theme.DialogTextColorArgb = Color.White.ToArgb();
            theme.ControlBackgroundColorArgb = Color.FromArgb(69, 69, 69).ToArgb();
            theme.ControlTextColorArgb = Color.White.ToArgb();
            theme.AccentColorArgb = Color.FromArgb(69, 214, 250).ToArgb();
            return theme;
        }

        public static ThemeDefinition CreateDefaultCustom()
        {
            return CreateDefaultCustom(ThemeColorMode.Light);
        }

        public static ThemeDefinition CreateDefaultCustom(ThemeColorMode colorMode)
        {
            var custom = CreateWindows11(colorMode);
            custom.Name = "Custom";
            return custom;
        }
    }
}

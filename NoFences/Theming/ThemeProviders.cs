using System.Drawing;

namespace NoFences.Theming
{
    /// <summary>
    /// 主题配置持久化时使用稳定标识，而不是可能随语言变化的显示名称。
    /// 后续内置主题或第三方主题只需增加自己的唯一标识即可。
    /// </summary>
    public static class ThemeIds
    {
        public const string Default = "default";
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
    /// 创建程序内置主题及其独立的明暗模式变体。
    /// “默认”主题精确还原主题功能加入前的黑色半透明栅栏；Windows 11 使用
    /// WinUI 语义色，Windows XP 使用 Luna 配色。所有预置都集中在这里，
    /// 新增主题时不需要在窗口绘制代码中增加主题名称判断。
    /// </summary>
    public static class ThemePresets
    {
        /// <summary>
        /// 返回程序最初的经典半透明主题。无参数重载代表默认颜色模式。
        /// </summary>
        public static ThemeDefinition CreateDefault()
        {
            return CreateDefault(ThemeColorMode.Light);
        }

        /// <summary>
        /// 创建经典半透明主题。栅栏主体在两种颜色模式下均保留最初的黑色
        /// 半透明外观；独立的黑暗模式仅调整菜单、设置页等辅助界面的明度，
        /// 因而不会把该主题与全局黑暗模式绑定在一起。
        /// </summary>
        public static ThemeDefinition CreateDefault(ThemeColorMode colorMode)
        {
            var theme = new ThemeDefinition
            {
                Name = "Default",
                FontFamilyName = "Segoe UI",

                // 主题功能加入前直接使用 Color.FromArgb(100, Color.Black)
                // 和 Color.FromArgb(50, Color.Black)。百分比取最接近的 39%/20%。
                MainPanelColorArgb = Color.Black.ToArgb(),
                TitleBarColorArgb = Color.Black.ToArgb(),
                MainPanelOpacityPercent = 39,
                TitleBarOpacityPercent = 20,

                TitleTextColorArgb = Color.White.ToArgb(),
                ItemTextColorArgb = Color.White.ToArgb(),
                ItemTextShadowColorArgb = Color.FromArgb(15, 15, 15).ToArgb(),
                ItemHoverColorArgb = SystemColors.ActiveCaption.ToArgb(),
                ItemSelectedColorArgb = SystemColors.GradientInactiveCaption.ToArgb(),
                BorderColorArgb = SystemColors.ActiveBorder.ToArgb(),
                ScrollBarColorArgb = Color.Black.ToArgb(),

                // 右键菜单和设置页同样使用经典深色表面，避免栅栏切换到
                // 半透明主题后仍弹出与主题无关的系统灰白色菜单。
                MenuBackgroundColorArgb = Color.FromArgb(45, 45, 48).ToArgb(),
                MenuTextColorArgb = Color.White.ToArgb(),
                MenuHighlightColorArgb = Color.FromArgb(61, 111, 158).ToArgb(),
                MenuHighlightTextColorArgb = Color.White.ToArgb(),
                DialogBackgroundColorArgb = Color.FromArgb(45, 45, 48).ToArgb(),
                DialogTextColorArgb = Color.White.ToArgb(),
                ControlBackgroundColorArgb = Color.FromArgb(63, 63, 70).ToArgb(),
                ControlTextColorArgb = Color.White.ToArgb(),
                AccentColorArgb = Color.FromArgb(96, 205, 255).ToArgb(),
                MenuStyle = ThemeMenuStyle.Standard,

                CornerRadius = 0,
                EnableBlur = true,
                BackgroundImageLayout = ThemeImageLayout.Fill,
                BackgroundImageOpacityPercent = 35
            };

            if (colorMode == ThemeColorMode.Dark)
            {
                // 黑暗模式是单独开关：只把辅助界面进一步压暗，不能改变
                // 默认主题作为“经典半透明栅栏”的风格身份和透明参数。
                theme.MenuBackgroundColorArgb = Color.FromArgb(32, 32, 32).ToArgb();
                theme.MenuHighlightColorArgb = Color.FromArgb(61, 61, 61).ToArgb();
                theme.DialogBackgroundColorArgb = Color.FromArgb(32, 32, 32).ToArgb();
                theme.ControlBackgroundColorArgb = Color.FromArgb(45, 45, 48).ToArgb();
            }

            return theme;
        }

        /// <summary>
        /// Windows 11 兼容重载。Windows 11 只是视觉风格，不代表黑暗模式，
        /// 因此无参数重载始终返回浅色变体。
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
                // Win11 菜单悬停采用低对比度中性灰，而不是 XP 式蓝色高亮。
                MenuHighlightColorArgb = Color.FromArgb(238, 238, 238).ToArgb(),
                MenuHighlightTextColorArgb = Color.FromArgb(26, 26, 26).ToArgb(),
                DialogBackgroundColorArgb = Color.FromArgb(243, 243, 243).ToArgb(),
                DialogTextColorArgb = Color.FromArgb(26, 26, 26).ToArgb(),
                ControlBackgroundColorArgb = Color.White.ToArgb(),
                ControlTextColorArgb = Color.FromArgb(26, 26, 26).ToArgb(),
                AccentColorArgb = Color.FromArgb(0, 103, 192).ToArgb(),
                MenuStyle = ThemeMenuStyle.Windows11,
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
            theme.MenuHighlightColorArgb = Color.FromArgb(61, 61, 61).ToArgb();
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
                MenuBackgroundColorArgb = Color.FromArgb(236, 233, 216).ToArgb(),
                MenuTextColorArgb = Color.Black.ToArgb(),
                MenuHighlightColorArgb = Color.FromArgb(49, 106, 197).ToArgb(),
                MenuHighlightTextColorArgb = Color.White.ToArgb(),
                DialogBackgroundColorArgb = Color.FromArgb(236, 233, 216).ToArgb(),
                DialogTextColorArgb = Color.Black.ToArgb(),
                ControlBackgroundColorArgb = Color.White.ToArgb(),
                ControlTextColorArgb = Color.Black.ToArgb(),
                AccentColorArgb = Color.FromArgb(0, 84, 227).ToArgb(),
                MenuStyle = ThemeMenuStyle.WindowsXp,
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

using System.Drawing;
using System.Windows.Forms;

namespace NoFences.Theming
{
    /// <summary>
    /// 将主题中的菜单风格标识解析成一组只读布局与绘制参数。
    ///
    /// ThemeDefinition 负责可持久化的颜色和风格选择，本类负责运行时尺寸。
    /// 这样新增菜单风格时只需在工厂中增加一个 Profile，不必修改
    /// FenceWindow、设置窗口或每个 ContextMenuStrip 的事件代码。
    /// 所有像素值均为 96 DPI 下的逻辑像素，由 WinForms 随菜单字体缩放。
    /// </summary>
    internal sealed class MenuThemeProfile
    {
        private MenuThemeProfile()
        {
        }

        public ThemeMenuStyle Style { get; private set; }

        public string FontFamilyName { get; private set; }

        public float FontSize { get; private set; }

        /// <summary>
        /// 字体可见字形相对其行框中心的视觉补偿量。不同字体的 ascent/descent
        /// 比例不同，即使 TextRenderer 居中行框，字形本身也可能看起来偏上。
        /// </summary>
        public int TextVerticalOffset { get; private set; }

        public int MinimumWidth { get; private set; }

        public int ItemHeight { get; private set; }

        public int SeparatorHeight { get; private set; }

        public int ContainerCornerRadius { get; private set; }

        public int ItemCornerRadius { get; private set; }

        public int ItemHorizontalInset { get; private set; }

        public int ItemVerticalInset { get; private set; }

        public int SeparatorInset { get; private set; }

        public Padding MenuPadding { get; private set; }

        public Padding ItemPadding { get; private set; }

        public Size ImageScalingSize { get; private set; }

        /// <summary>
        /// 菜单项内容相对容器左右边缘的额外内缩。该值同时作用于图标、文字、
        /// 勾选、快捷键和子菜单箭头，避免只移动图标后破坏内部对齐关系。
        /// </summary>
        public int ContentHorizontalInset { get; private set; }

        public int ImageHorizontalOffset { get; private set; }

        public bool DrawClassicThreeDimensionalBorder { get; private set; }

        public bool DrawChevronArrow { get; private set; }

        public bool DrawEmbossedDisabledText { get; private set; }

        public bool DropShadowEnabled { get; private set; }

        public double Opacity { get; private set; }

        public Color ImageMarginColor { get; private set; }

        public Color BorderOuterColor { get; private set; }

        public Color BorderHighlightColor { get; private set; }

        public Color BorderShadowColor { get; private set; }

        public Color SeparatorPrimaryColor { get; private set; }

        public Color SeparatorSecondaryColor { get; private set; }

        public Color DisabledTextColor { get; private set; }

        public Color PressedColor { get; private set; }

        /// <summary>
        /// 根据主题创建确定性的菜单 Profile。XP 与 Win11 的数值直接对应
        /// UI 规格；Standard 用于经典半透明默认主题和未知的未来兼容主题。
        /// </summary>
        public static MenuThemeProfile Create(ThemeDefinition theme)
        {
            bool dark = ThemeDrawing.IsDark(theme.MenuBackgroundColor);
            switch (theme.MenuStyle)
            {
                case ThemeMenuStyle.WindowsXp:
                    return CreateWindowsXp(theme, dark);
                case ThemeMenuStyle.Windows11:
                    return CreateWindows11(theme, dark);
                default:
                    return CreateStandard(theme, dark);
            }
        }

        private static MenuThemeProfile CreateWindowsXp(ThemeDefinition theme, bool dark)
        {
            return new MenuThemeProfile
            {
                Style = ThemeMenuStyle.WindowsXp,
                FontFamilyName = "Tahoma",
                FontSize = 8.25f,
                TextVerticalOffset = 0,
                MinimumWidth = 150,
                ItemHeight = 22,
                SeparatorHeight = 4,
                ContainerCornerRadius = 0,
                ItemCornerRadius = 0,
                ItemHorizontalInset = 2,
                ItemVerticalInset = 0,
                SeparatorInset = 3,
                MenuPadding = new Padding(2),
                ItemPadding = new Padding(4, 0, 6, 0),
                ImageScalingSize = new Size(16, 16),
                ContentHorizontalInset = 0,
                ImageHorizontalOffset = 0,
                DrawClassicThreeDimensionalBorder = true,
                DrawChevronArrow = false,
                DrawEmbossedDisabledText = true,
                DropShadowEnabled = true,
                Opacity = 1d,
                ImageMarginColor = theme.MenuBackgroundColor,
                BorderOuterColor = dark
                    ? theme.BorderColor
                    : Color.FromArgb(113, 111, 100),
                BorderHighlightColor = dark
                    ? ThemeDrawing.Mix(theme.MenuBackgroundColor, Color.White, 0.20f)
                    : Color.White,
                BorderShadowColor = dark
                    ? ThemeDrawing.Mix(theme.MenuBackgroundColor, Color.Black, 0.35f)
                    : Color.FromArgb(172, 168, 153),
                SeparatorPrimaryColor = dark
                    ? ThemeDrawing.Mix(theme.MenuBackgroundColor, Color.Black, 0.35f)
                    : Color.FromArgb(172, 168, 153),
                SeparatorSecondaryColor = dark
                    ? ThemeDrawing.Mix(theme.MenuBackgroundColor, Color.White, 0.20f)
                    : Color.White,
                DisabledTextColor = dark
                    ? ThemeDrawing.Mix(theme.MenuBackgroundColor, theme.MenuTextColor, 0.42f)
                    : Color.FromArgb(172, 168, 153),
                PressedColor = theme.MenuHighlightColor
            };
        }

        private static MenuThemeProfile CreateWindows11(ThemeDefinition theme, bool dark)
        {
            return new MenuThemeProfile
            {
                Style = ThemeMenuStyle.Windows11,
                FontFamilyName = "Segoe UI Variable Text",
                FontSize = 10.5f,
                TextVerticalOffset = 0,
                MinimumWidth = 170,
                ItemHeight = 34,
                SeparatorHeight = 9,
                ContainerCornerRadius = 8,
                ItemCornerRadius = 4,
                ItemHorizontalInset = 8,
                ItemVerticalInset = 2,
                SeparatorInset = 8,
                MenuPadding = new Padding(4, 6, 4, 6),
                ItemPadding = new Padding(8, 0, 12, 0),
                ImageScalingSize = new Size(16, 16),
                ContentHorizontalInset = 8,
                ImageHorizontalOffset = 0,
                DrawClassicThreeDimensionalBorder = false,
                DrawChevronArrow = true,
                DrawEmbossedDisabledText = false,
                DropShadowEnabled = true,
                // ToolStripDropDown 的 Opacity 会连同文字一起透明，因此采用
                // 接近不透明的 0.98，表达轻微材质感同时保持文字清晰。
                Opacity = 0.98d,
                ImageMarginColor = theme.MenuBackgroundColor,
                BorderOuterColor = ThemeDrawing.Mix(
                    theme.MenuBackgroundColor,
                    dark ? Color.White : Color.Black,
                    dark ? 0.10f : 0.08f),
                BorderHighlightColor = Color.Transparent,
                BorderShadowColor = Color.Transparent,
                SeparatorPrimaryColor = ThemeDrawing.Mix(
                    theme.MenuBackgroundColor,
                    dark ? Color.White : Color.Black,
                    0.08f),
                SeparatorSecondaryColor = Color.Transparent,
                DisabledTextColor = ThemeDrawing.Mix(
                    theme.MenuBackgroundColor,
                    theme.MenuTextColor,
                    0.36f),
                PressedColor = ThemeDrawing.Mix(
                    theme.MenuHighlightColor,
                    dark ? Color.White : Color.Black,
                    0.04f)
            };
        }

        private static MenuThemeProfile CreateStandard(ThemeDefinition theme, bool dark)
        {
            return new MenuThemeProfile
            {
                Style = ThemeMenuStyle.Standard,
                FontFamilyName = theme.FontFamilyName,
                FontSize = 9f,
                TextVerticalOffset = 0,
                MinimumWidth = 170,
                ItemHeight = 26,
                SeparatorHeight = 7,
                ContainerCornerRadius = MathMin(theme.CornerRadius, 8),
                ItemCornerRadius = theme.CornerRadius > 0 ? 4 : 0,
                ItemHorizontalInset = 8,
                ItemVerticalInset = 1,
                SeparatorInset = 8,
                MenuPadding = new Padding(2, 3, 2, 3),
                ItemPadding = new Padding(5, 0, 8, 0),
                ImageScalingSize = new Size(16, 16),
                ContentHorizontalInset = 8,
                ImageHorizontalOffset = 0,
                DrawClassicThreeDimensionalBorder = false,
                DrawChevronArrow = false,
                DrawEmbossedDisabledText = false,
                DropShadowEnabled = true,
                // ToolStripDropDown 的透明度会连同文字一起生效。默认半透明
                // 面板使用 0.92，在保留桌面材质感的同时确保菜单文字清晰。
                Opacity = theme.EnableBlur && theme.MainPanelOpacityPercent < 100
                    ? 0.92d
                    : 1d,
                ImageMarginColor = ThemeDrawing.Mix(
                    theme.MenuBackgroundColor,
                    theme.ControlBackgroundColor,
                    0.25f),
                BorderOuterColor = theme.BorderColor,
                BorderHighlightColor = Color.Transparent,
                BorderShadowColor = Color.Transparent,
                SeparatorPrimaryColor = ThemeDrawing.Mix(
                    theme.MenuBackgroundColor,
                    theme.BorderColor,
                    0.55f),
                SeparatorSecondaryColor = Color.Transparent,
                DisabledTextColor = ThemeDrawing.Mix(
                    theme.MenuBackgroundColor,
                    theme.MenuTextColor,
                    dark ? 0.42f : 0.48f),
                PressedColor = ThemeDrawing.Mix(
                    theme.MenuHighlightColor,
                    theme.AccentColor,
                    0.20f)
            };
        }

        private static int MathMin(int first, int second)
        {
            return first < second ? first : second;
        }
    }
}

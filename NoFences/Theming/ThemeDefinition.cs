using System;
using System.Drawing;
using System.Xml.Serialization;

namespace NoFences.Theming
{
    /// <summary>
    /// Controls how a theme background image is fitted into the fence panel.
    /// This enum is intentionally owned by the theming module instead of using
    /// WinForms ImageLayout so the persisted format is independent of a UI control.
    /// </summary>
    public enum ThemeImageLayout
    {
        Fill,
        Fit,
        Stretch,
        Center,
        Tile
    }

    /// <summary>
    /// Application-wide color mode.  It is deliberately separate from a theme
    /// provider: Windows 11, Windows XP, and future visual styles can all expose
    /// both a light and a dark palette without duplicating their style identity.
    /// </summary>
    public enum ThemeColorMode
    {
        Light,
        Dark
    }

    /// <summary>
    /// 应用自有右键菜单的结构风格。颜色仍由 <see cref="ThemeDefinition"/>
    /// 提供，此枚举只决定尺寸、留白、边框、箭头和选中区域等几何语义。
    /// </summary>
    public enum ThemeMenuStyle
    {
        Standard,
        Windows11,
        WindowsXp
    }

    /// <summary>
    /// Contains every visual value consumed by the application.
    ///
    /// Colors are stored as ARGB integers because <see cref="XmlSerializer"/>
    /// cannot reliably round-trip all <see cref="Color"/> instances.  The
    /// convenience Color properties are ignored by XML and should be used by
    /// drawing code. Adding a new visual property here is backward compatible:
    /// older theme files simply retain the default property initializer.
    /// </summary>
    [Serializable]
    public sealed class ThemeDefinition
    {
        public string Name { get; set; } = "Windows 11";

        public string FontFamilyName { get; set; } = "Segoe UI";

        public int MainPanelColorArgb { get; set; } = Color.FromArgb(243, 243, 243).ToArgb();

        public int TitleBarColorArgb { get; set; } = Color.FromArgb(249, 249, 249).ToArgb();

        public int TitleTextColorArgb { get; set; } = Color.FromArgb(26, 26, 26).ToArgb();

        public int ItemTextColorArgb { get; set; } = Color.FromArgb(26, 26, 26).ToArgb();

        public int ItemTextShadowColorArgb { get; set; } = Color.White.ToArgb();

        public int ItemHoverColorArgb { get; set; } = Color.FromArgb(229, 243, 255).ToArgb();

        public int ItemSelectedColorArgb { get; set; } = Color.FromArgb(0, 103, 192).ToArgb();

        public int BorderColorArgb { get; set; } = Color.FromArgb(117, 117, 117).ToArgb();

        public int ScrollBarColorArgb { get; set; } = Color.FromArgb(138, 138, 138).ToArgb();

        public int MenuBackgroundColorArgb { get; set; } = Color.FromArgb(249, 249, 249).ToArgb();

        public int MenuTextColorArgb { get; set; } = Color.FromArgb(26, 26, 26).ToArgb();

        public int MenuHighlightColorArgb { get; set; } = Color.FromArgb(229, 243, 255).ToArgb();

        public int MenuHighlightTextColorArgb { get; set; } = Color.FromArgb(26, 26, 26).ToArgb();

        public int DialogBackgroundColorArgb { get; set; } = Color.FromArgb(243, 243, 243).ToArgb();

        public int DialogTextColorArgb { get; set; } = Color.FromArgb(26, 26, 26).ToArgb();

        public int ControlBackgroundColorArgb { get; set; } = Color.White.ToArgb();

        public int ControlTextColorArgb { get; set; } = Color.FromArgb(26, 26, 26).ToArgb();

        public int AccentColorArgb { get; set; } = Color.FromArgb(0, 103, 192).ToArgb();

        /// <summary>
        /// 右键菜单的结构风格。作为主题定义的一部分持久化，使“基于此主题
        /// 自定义”能够同时复制对应的菜单密度和交互视觉。
        /// </summary>
        public ThemeMenuStyle MenuStyle { get; set; } = ThemeMenuStyle.Standard;

        /// <summary>
        /// Opacity of the body background only. Icons and labels stay fully
        /// opaque so a transparent panel remains readable.
        /// </summary>
        public int MainPanelOpacityPercent { get; set; } = 78;

        /// <summary>
        /// Opacity of the title background. The title text is never faded.
        /// </summary>
        public int TitleBarOpacityPercent { get; set; } = 72;

        /// <summary>
        /// Corner radius in 96-DPI logical pixels.
        /// </summary>
        public int CornerRadius { get; set; } = 12;

        public bool EnableBlur { get; set; } = true;

        /// <summary>
        /// Legacy serialized field retained so settings written by the first
        /// theming version round-trip safely.  It is intentionally ignored:
        /// native menus now follow ThemeSettings.DarkModeEnabled, keeping color
        /// mode independent from the selected visual style.
        /// </summary>
        public bool PreferDarkNativeMenus { get; set; }

        public string BackgroundImagePath { get; set; } = string.Empty;

        public ThemeImageLayout BackgroundImageLayout { get; set; } = ThemeImageLayout.Fill;

        public int BackgroundImageOpacityPercent { get; set; } = 35;

        [XmlIgnore]
        public Color MainPanelColor => Color.FromArgb(MainPanelColorArgb);

        [XmlIgnore]
        public Color TitleBarColor => Color.FromArgb(TitleBarColorArgb);

        [XmlIgnore]
        public Color TitleTextColor => Color.FromArgb(TitleTextColorArgb);

        [XmlIgnore]
        public Color ItemTextColor => Color.FromArgb(ItemTextColorArgb);

        [XmlIgnore]
        public Color ItemTextShadowColor => Color.FromArgb(ItemTextShadowColorArgb);

        [XmlIgnore]
        public Color ItemHoverColor => Color.FromArgb(ItemHoverColorArgb);

        [XmlIgnore]
        public Color ItemSelectedColor => Color.FromArgb(ItemSelectedColorArgb);

        [XmlIgnore]
        public Color BorderColor => Color.FromArgb(BorderColorArgb);

        [XmlIgnore]
        public Color ScrollBarColor => Color.FromArgb(ScrollBarColorArgb);

        [XmlIgnore]
        public Color MenuBackgroundColor => Color.FromArgb(MenuBackgroundColorArgb);

        [XmlIgnore]
        public Color MenuTextColor => Color.FromArgb(MenuTextColorArgb);

        [XmlIgnore]
        public Color MenuHighlightColor => Color.FromArgb(MenuHighlightColorArgb);

        [XmlIgnore]
        public Color MenuHighlightTextColor => Color.FromArgb(MenuHighlightTextColorArgb);

        [XmlIgnore]
        public Color DialogBackgroundColor => Color.FromArgb(DialogBackgroundColorArgb);

        [XmlIgnore]
        public Color DialogTextColor => Color.FromArgb(DialogTextColorArgb);

        [XmlIgnore]
        public Color ControlBackgroundColor => Color.FromArgb(ControlBackgroundColorArgb);

        [XmlIgnore]
        public Color ControlTextColor => Color.FromArgb(ControlTextColorArgb);

        [XmlIgnore]
        public Color AccentColor => Color.FromArgb(AccentColorArgb);

        /// <summary>
        /// Returns a detached copy. Theme consumers receive copies so an editor
        /// cannot accidentally mutate the active theme before the user applies it.
        /// </summary>
        public ThemeDefinition Clone()
        {
            return (ThemeDefinition)MemberwiseClone();
        }

        /// <summary>
        /// Clamps user-editable values loaded from disk. This protects drawing
        /// code from hand-edited or future-version configuration files.
        /// </summary>
        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(Name))
                Name = "Custom";
            if (string.IsNullOrWhiteSpace(FontFamilyName))
                FontFamilyName = "Segoe UI";
            if (BackgroundImagePath == null)
                BackgroundImagePath = string.Empty;

            MainPanelOpacityPercent = Clamp(MainPanelOpacityPercent, 20, 100);
            TitleBarOpacityPercent = Clamp(TitleBarOpacityPercent, 20, 100);
            BackgroundImageOpacityPercent = Clamp(BackgroundImageOpacityPercent, 0, 100);
            CornerRadius = Clamp(CornerRadius, 0, 48);

            if (!Enum.IsDefined(typeof(ThemeImageLayout), BackgroundImageLayout))
                BackgroundImageLayout = ThemeImageLayout.Fill;
            if (!Enum.IsDefined(typeof(ThemeMenuStyle), MenuStyle))
                MenuStyle = ThemeMenuStyle.Standard;
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}

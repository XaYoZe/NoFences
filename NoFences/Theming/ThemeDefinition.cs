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

        public int MainPanelColorArgb { get; set; } = Color.FromArgb(32, 32, 32).ToArgb();

        public int TitleBarColorArgb { get; set; } = Color.FromArgb(24, 24, 24).ToArgb();

        public int TitleTextColorArgb { get; set; } = Color.White.ToArgb();

        public int ItemTextColorArgb { get; set; } = Color.White.ToArgb();

        public int ItemTextShadowColorArgb { get; set; } = Color.Black.ToArgb();

        public int ItemHoverColorArgb { get; set; } = Color.FromArgb(61, 111, 158).ToArgb();

        public int ItemSelectedColorArgb { get; set; } = Color.FromArgb(0, 103, 192).ToArgb();

        public int BorderColorArgb { get; set; } = Color.FromArgb(139, 190, 241).ToArgb();

        public int ScrollBarColorArgb { get; set; } = Color.FromArgb(133, 133, 133).ToArgb();

        public int MenuBackgroundColorArgb { get; set; } = Color.FromArgb(32, 32, 32).ToArgb();

        public int MenuTextColorArgb { get; set; } = Color.FromArgb(245, 245, 245).ToArgb();

        public int MenuHighlightColorArgb { get; set; } = Color.FromArgb(61, 61, 61).ToArgb();

        public int MenuHighlightTextColorArgb { get; set; } = Color.White.ToArgb();

        public int DialogBackgroundColorArgb { get; set; } = Color.FromArgb(32, 32, 32).ToArgb();

        public int DialogTextColorArgb { get; set; } = Color.FromArgb(245, 245, 245).ToArgb();

        public int ControlBackgroundColorArgb { get; set; } = Color.FromArgb(45, 45, 48).ToArgb();

        public int ControlTextColorArgb { get; set; } = Color.White.ToArgb();

        public int AccentColorArgb { get; set; } = Color.FromArgb(96, 205, 255).ToArgb();

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
        /// Controls the best-effort theme of native shell context menus. The
        /// application-owned ContextMenuStrip is always rendered by our custom
        /// renderer and therefore supports all custom colors.
        /// </summary>
        public bool PreferDarkNativeMenus { get; set; } = true;

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
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}

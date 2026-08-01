using NoFences.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace NoFences.Theming
{
    /// <summary>
    /// XML document stored separately from FenceInfo metadata. Theme selection and
    /// color mode are application-wide, while FenceInfo remains backward compatible
    /// with every existing user's per-fence XML file.
    /// </summary>
    [Serializable]
    public sealed class ThemeSettings
    {
        public const int CurrentSchemaVersion = 2;

        /// <summary>
        /// 主题配置结构版本。版本 2 首次加入独立的“默认”主题，用于区分旧版
        /// 隐式 Windows 11 默认值与用户在新版中主动选择的 Windows 11。
        /// </summary>
        public int ThemeSchemaVersion { get; set; } = CurrentSchemaVersion;

        public string SelectedThemeId { get; set; } = ThemeIds.Default;

        /// <summary>
        /// Independent application color-mode switch.  It must never be inferred
        /// from SelectedThemeId; Windows 11 and Windows XP both support both modes.
        /// </summary>
        public bool DarkModeEnabled { get; set; }

        /// <summary>
        /// Light custom variant.  The XML name remains CustomTheme so settings from
        /// the first theming release remain readable without a migration schema.
        /// </summary>
        public ThemeDefinition CustomTheme { get; set; } =
            ThemePresets.CreateDefaultCustom(ThemeColorMode.Light);

        /// <summary>
        /// Dark custom variant.  Keeping a variant per mode lets users edit exact
        /// colors instead of applying a lossy automatic inversion to their palette.
        /// </summary>
        public ThemeDefinition CustomDarkTheme { get; set; } =
            ThemePresets.CreateDefaultCustom(ThemeColorMode.Dark);
    }

    /// <summary>
    /// Central registry, persistence service, and change notification source for
    /// themes. UI code asks only for CurrentTheme and listens for ThemeChanged.
    /// Theme identity and Light/Dark mode are stored and resolved independently.
    /// </summary>
    public sealed class ThemeManager
    {
        private const string SettingsFileName = "__theme_settings.xml";
        private readonly object syncRoot = new object();
        private readonly Dictionary<string, IThemeProvider> providers =
            new Dictionary<string, IThemeProvider>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> providerOrder = new List<string>();
        private readonly string settingsPath;
        private ThemeSettings settings;

        private ThemeManager()
        {
            // 注册顺序就是配置面板中的显示顺序。“默认”必须排在第一项，
            // 同时也是新安装、损坏配置和未知主题标识的统一回退主题。
            RegisterThemeInternal(new StaticThemeProvider(
                ThemeIds.Default,
                ThemeText.DefaultTheme,
                ThemePresets.CreateDefault(ThemeColorMode.Light),
                ThemePresets.CreateDefault(ThemeColorMode.Dark)));
            RegisterThemeInternal(new StaticThemeProvider(
                ThemeIds.Windows11,
                "Windows 11",
                ThemePresets.CreateWindows11(ThemeColorMode.Light),
                ThemePresets.CreateWindows11(ThemeColorMode.Dark)));
            RegisterThemeInternal(new StaticThemeProvider(
                ThemeIds.WindowsXp,
                "Windows XP",
                ThemePresets.CreateWindowsXp(ThemeColorMode.Light),
                ThemePresets.CreateWindowsXp(ThemeColorMode.Dark)));

            var basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NoFences");
            settingsPath = Path.Combine(basePath, SettingsFileName);
            settings = LoadSettings();

            // Native shell menus cannot use arbitrary application colors. They
            // follow the independent global mode rather than a theme definition.
            WindowUtil.TrySetPreferredAppMode(settings.DarkModeEnabled);
        }

        public static ThemeManager Instance { get; } = new ThemeManager();

        /// <summary>
        /// Explicit startup hook used by Program before any windows are created.
        /// Accessing the singleton also loads and validates the saved selection.
        /// </summary>
        public static void Initialize()
        {
            WindowUtil.TrySetPreferredAppMode(Instance.DarkModeEnabled);
        }

        public event EventHandler ThemeChanged;

        public string SelectedThemeId
        {
            get
            {
                lock (syncRoot)
                    return settings.SelectedThemeId;
            }
        }

        public bool DarkModeEnabled
        {
            get
            {
                lock (syncRoot)
                    return settings.DarkModeEnabled;
            }
        }

        public ThemeColorMode CurrentColorMode =>
            DarkModeEnabled ? ThemeColorMode.Dark : ThemeColorMode.Light;

        public ThemeDefinition CurrentTheme
        {
            get
            {
                lock (syncRoot)
                {
                    return ResolveThemeInternal(
                        settings.SelectedThemeId,
                        ToColorMode(settings.DarkModeEnabled),
                        settings.CustomTheme,
                        settings.CustomDarkTheme);
                }
            }
        }

        /// <summary>
        /// Backward-compatible alias for the light custom variant.
        /// </summary>
        public ThemeDefinition CustomTheme => CustomLightTheme;

        public ThemeDefinition CustomLightTheme
        {
            get
            {
                lock (syncRoot)
                    return settings.CustomTheme.Clone();
            }
        }

        public ThemeDefinition CustomDarkTheme
        {
            get
            {
                lock (syncRoot)
                    return settings.CustomDarkTheme.Clone();
            }
        }

        /// <summary>
        /// Returns registered preset providers in deterministic registration order.
        /// A provider always returns a fresh definition, so callers may safely edit it.
        /// </summary>
        public IList<IThemeProvider> GetThemeProviders()
        {
            lock (syncRoot)
                return providerOrder.Select(id => providers[id]).ToList();
        }

        /// <summary>
        /// Registers (or replaces) a preset. This is the only integration point a
        /// future theme module needs. "custom" is reserved for the user's settings.
        /// </summary>
        public void RegisterTheme(IThemeProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));
            if (string.IsNullOrWhiteSpace(provider.Id))
                throw new ArgumentException("A theme provider must have an ID.", nameof(provider));
            if (string.Equals(provider.Id, ThemeIds.Custom, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The custom theme ID is reserved.", nameof(provider));

            lock (syncRoot)
                RegisterThemeInternal(provider);
        }

        /// <summary>
        /// Resolves a style using the currently selected color mode.
        /// </summary>
        public ThemeDefinition GetTheme(string themeId)
        {
            lock (syncRoot)
            {
                return ResolveThemeInternal(
                    themeId,
                    ToColorMode(settings.DarkModeEnabled),
                    settings.CustomTheme,
                    settings.CustomDarkTheme);
            }
        }

        /// <summary>
        /// Resolves any style/mode combination for configuration previews without
        /// changing application state.
        /// </summary>
        public ThemeDefinition GetTheme(string themeId, ThemeColorMode colorMode)
        {
            lock (syncRoot)
            {
                return ResolveThemeInternal(
                    themeId,
                    colorMode,
                    settings.CustomTheme,
                    settings.CustomDarkTheme);
            }
        }

        /// <summary>
        /// Atomically saves the selected style, independent color mode, and both
        /// custom variants. Saving both custom variants even when a preset is active
        /// preserves edits when the user later switches back to Custom.
        /// </summary>
        public void ApplySelection(
            string themeId,
            ThemeDefinition customLightTheme,
            ThemeDefinition customDarkTheme,
            bool darkModeEnabled)
        {
            lock (syncRoot)
            {
                if (!IsKnownThemeInternal(themeId))
                    themeId = ThemeIds.Default;

                if (customLightTheme != null)
                    settings.CustomTheme = PrepareCustomTheme(customLightTheme);
                if (customDarkTheme != null)
                    settings.CustomDarkTheme = PrepareCustomTheme(customDarkTheme);

                settings.SelectedThemeId = themeId;
                settings.DarkModeEnabled = darkModeEnabled;
                SaveSettings(settings);
            }

            NotifyThemeChanged(darkModeEnabled);
        }

        /// <summary>
        /// Changes only the global color mode. This is used by the right-click menu
        /// switch and intentionally leaves the selected visual style untouched.
        /// </summary>
        public void SetDarkMode(bool enabled)
        {
            lock (syncRoot)
            {
                if (settings.DarkModeEnabled == enabled)
                    return;

                settings.DarkModeEnabled = enabled;
                SaveSettings(settings);
            }

            NotifyThemeChanged(enabled);
        }

        private void NotifyThemeChanged(bool darkModeEnabled)
        {
            WindowUtil.TrySetPreferredAppMode(darkModeEnabled);
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }

        private static ThemeColorMode ToColorMode(bool darkModeEnabled)
        {
            return darkModeEnabled ? ThemeColorMode.Dark : ThemeColorMode.Light;
        }

        private static ThemeDefinition PrepareCustomTheme(ThemeDefinition source)
        {
            var result = source.Clone();
            result.Name = "Custom";
            result.Normalize();
            return result;
        }

        private void RegisterThemeInternal(IThemeProvider provider)
        {
            if (!providers.ContainsKey(provider.Id))
                providerOrder.Add(provider.Id);
            providers[provider.Id] = provider;
        }

        private bool IsKnownThemeInternal(string themeId)
        {
            return string.Equals(themeId, ThemeIds.Custom, StringComparison.OrdinalIgnoreCase) ||
                   (!string.IsNullOrWhiteSpace(themeId) && providers.ContainsKey(themeId));
        }

        private ThemeDefinition ResolveThemeInternal(
            string themeId,
            ThemeColorMode colorMode,
            ThemeDefinition customLightTheme,
            ThemeDefinition customDarkTheme)
        {
            ThemeDefinition result;
            if (string.Equals(themeId, ThemeIds.Custom, StringComparison.OrdinalIgnoreCase))
            {
                var custom = colorMode == ThemeColorMode.Dark
                    ? customDarkTheme
                    : customLightTheme;
                result = custom != null
                    ? custom.Clone()
                    : ThemePresets.CreateDefaultCustom(colorMode);
            }
            else if (!string.IsNullOrWhiteSpace(themeId) &&
                     providers.TryGetValue(themeId, out var provider))
            {
                result = provider.CreateTheme(colorMode);
            }
            else
            {
                result = ThemePresets.CreateDefault(colorMode);
            }

            result.Normalize();
            return result;
        }

        private ThemeSettings LoadSettings()
        {
            try
            {
                if (!File.Exists(settingsPath))
                    return new ThemeSettings();

                // Reading the XML text first lets us distinguish a genuinely saved
                // false value from a legacy file that predates DarkModeEnabled.
                string serializedSettings = File.ReadAllText(settingsPath);
                bool hasIndependentColorMode = serializedSettings.IndexOf(
                    "<DarkModeEnabled>",
                    StringComparison.OrdinalIgnoreCase) >= 0;
                bool hasThemeSchemaVersion = serializedSettings.IndexOf(
                    "<ThemeSchemaVersion>",
                    StringComparison.OrdinalIgnoreCase) >= 0;

                var serializer = new XmlSerializer(typeof(ThemeSettings));
                ThemeSettings loaded;
                using (var reader = new StringReader(serializedSettings))
                    loaded = serializer.Deserialize(reader) as ThemeSettings;

                if (loaded == null)
                    return new ThemeSettings();

                if (loaded.CustomTheme == null)
                    loaded.CustomTheme = ThemePresets.CreateDefaultCustom(ThemeColorMode.Light);

                if (!hasIndependentColorMode)
                {
                    // Version 1 stored one mostly-dark custom palette because its
                    // Windows 11 preset implicitly meant dark. Preserve that palette
                    // as the new dark custom variant and create a true light variant.
                    bool legacyCustomWasDark =
                        ThemeDrawing.IsDark(loaded.CustomTheme.DialogBackgroundColor);
                    if (legacyCustomWasDark)
                    {
                        loaded.CustomDarkTheme = loaded.CustomTheme.Clone();
                        loaded.CustomTheme = ThemePresets.CreateDefaultCustom(ThemeColorMode.Light);
                    }
                    else
                    {
                        loaded.CustomDarkTheme =
                            ThemePresets.CreateDefaultCustom(ThemeColorMode.Dark);
                    }

                    // 第一版主题功能曾把程序最初的半透明外观误命名为
                    // Windows 11。迁移时改用新的“默认”标识，既保持用户看到的
                    // 原始外观，又让真正的 Win11 风格继续独立于黑暗模式。
                    if (string.Equals(
                        loaded.SelectedThemeId,
                        ThemeIds.Windows11,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        loaded.SelectedThemeId = ThemeIds.Default;
                        loaded.DarkModeEnabled = false;
                    }
                    else
                    {
                        loaded.DarkModeEnabled =
                            string.Equals(
                                loaded.SelectedThemeId,
                                ThemeIds.Custom,
                                StringComparison.OrdinalIgnoreCase) && legacyCustomWasDark;
                    }
                }
                else if (loaded.CustomDarkTheme == null)
                {
                    loaded.CustomDarkTheme =
                        ThemePresets.CreateDefaultCustom(ThemeColorMode.Dark);
                }

                // 加入“默认”主题之前，Windows 11 同时承担隐式默认值。只对没有
                // 新版本标记的旧配置执行一次迁移；迁移后用户若主动选回 Win11，
                // 保存的版本标记会确保后续启动尊重该选择。XP 和自定义不受影响。
                if ((!hasThemeSchemaVersion ||
                     loaded.ThemeSchemaVersion < ThemeSettings.CurrentSchemaVersion) &&
                    string.Equals(
                        loaded.SelectedThemeId,
                        ThemeIds.Windows11,
                        StringComparison.OrdinalIgnoreCase))
                {
                    loaded.SelectedThemeId = ThemeIds.Default;
                }
                loaded.ThemeSchemaVersion = ThemeSettings.CurrentSchemaVersion;

                loaded.CustomTheme = PrepareCustomTheme(loaded.CustomTheme);
                loaded.CustomDarkTheme = PrepareCustomTheme(loaded.CustomDarkTheme);
                if (!IsKnownThemeInternal(loaded.SelectedThemeId))
                    loaded.SelectedThemeId = ThemeIds.Default;
                return loaded;
            }
            catch
            {
                // A malformed or inaccessible optional settings file must never
                // prevent fences from loading. Defaults remain usable and the old
                // file is left untouched until the user explicitly applies a theme.
                return new ThemeSettings();
            }
        }

        private void SaveSettings(ThemeSettings value)
        {
            string temporaryPath = settingsPath + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                var serializer = new XmlSerializer(typeof(ThemeSettings));
                using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    serializer.Serialize(stream, value);

                if (File.Exists(settingsPath))
                {
                    try
                    {
                        File.Replace(temporaryPath, settingsPath, null);
                    }
                    catch
                    {
                        // File.Replace is not supported by every filesystem. Copying
                        // the fully-written temporary file is the compatible fallback.
                        File.Copy(temporaryPath, settingsPath, true);
                        File.Delete(temporaryPath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, settingsPath);
                }
            }
            catch
            {
                // Applying a theme is still useful for the current session when
                // LocalAppData is read-only. Persistence failure is deliberately
                // non-fatal, matching the application's lightweight startup model.
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch
                {
                }
            }
        }
    }
}

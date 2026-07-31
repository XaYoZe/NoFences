using NoFences.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace NoFences.Theming
{
    /// <summary>
    /// XML document stored separately from FenceInfo metadata. Theme selection is
    /// application-wide, while FenceInfo remains backward compatible with every
    /// existing user's per-fence XML file.
    /// </summary>
    [Serializable]
    public sealed class ThemeSettings
    {
        public string SelectedThemeId { get; set; } = ThemeIds.Windows11;

        public ThemeDefinition CustomTheme { get; set; } = ThemePresets.CreateDefaultCustom();
    }

    /// <summary>
    /// Central registry, persistence service, and change notification source for
    /// themes. UI code asks only for CurrentTheme and listens for ThemeChanged.
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
            RegisterThemeInternal(new StaticThemeProvider(
                ThemeIds.Windows11,
                "Windows 11",
                ThemePresets.CreateWindows11()));
            RegisterThemeInternal(new StaticThemeProvider(
                ThemeIds.WindowsXp,
                "Windows XP",
                ThemePresets.CreateWindowsXp()));

            var basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NoFences");
            settingsPath = Path.Combine(basePath, SettingsFileName);
            settings = LoadSettings();

            // Native shell menus cannot use arbitrary application colors. We can
            // still request their closest dark/light system rendering.
            WindowUtil.TrySetPreferredAppMode(CurrentTheme.PreferDarkNativeMenus);
        }

        public static ThemeManager Instance { get; } = new ThemeManager();

        /// <summary>
        /// Explicit startup hook used by Program before any windows are created.
        /// Accessing the singleton also loads and validates the saved selection.
        /// </summary>
        public static void Initialize()
        {
            WindowUtil.TrySetPreferredAppMode(Instance.CurrentTheme.PreferDarkNativeMenus);
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

        public ThemeDefinition CurrentTheme
        {
            get
            {
                lock (syncRoot)
                    return ResolveThemeInternal(settings.SelectedThemeId, settings.CustomTheme);
            }
        }

        public ThemeDefinition CustomTheme
        {
            get
            {
                lock (syncRoot)
                    return settings.CustomTheme.Clone();
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

        public ThemeDefinition GetTheme(string themeId)
        {
            lock (syncRoot)
                return ResolveThemeInternal(themeId, settings.CustomTheme);
        }

        /// <summary>
        /// Atomically updates both the saved custom definition and active theme.
        /// Saving the custom definition even when a preset is selected preserves
        /// edits when the user switches back to Custom later.
        /// </summary>
        public void ApplySelection(string themeId, ThemeDefinition customTheme)
        {
            ThemeDefinition activeTheme;
            lock (syncRoot)
            {
                if (!IsKnownThemeInternal(themeId))
                    themeId = ThemeIds.Windows11;

                if (customTheme != null)
                {
                    customTheme = customTheme.Clone();
                    customTheme.Name = "Custom";
                    customTheme.Normalize();
                    settings.CustomTheme = customTheme;
                }

                settings.SelectedThemeId = themeId;
                SaveSettings(settings);
                activeTheme = ResolveThemeInternal(themeId, settings.CustomTheme);
            }

            WindowUtil.TrySetPreferredAppMode(activeTheme.PreferDarkNativeMenus);
            ThemeChanged?.Invoke(this, EventArgs.Empty);
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

        private ThemeDefinition ResolveThemeInternal(string themeId, ThemeDefinition customTheme)
        {
            ThemeDefinition result;
            if (string.Equals(themeId, ThemeIds.Custom, StringComparison.OrdinalIgnoreCase))
            {
                result = customTheme != null
                    ? customTheme.Clone()
                    : ThemePresets.CreateDefaultCustom();
            }
            else if (!string.IsNullOrWhiteSpace(themeId) && providers.TryGetValue(themeId, out var provider))
            {
                result = provider.CreateTheme();
            }
            else
            {
                result = ThemePresets.CreateWindows11();
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

                var serializer = new XmlSerializer(typeof(ThemeSettings));
                using (var stream = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var loaded = serializer.Deserialize(stream) as ThemeSettings;
                    if (loaded == null)
                        return new ThemeSettings();

                    if (loaded.CustomTheme == null)
                        loaded.CustomTheme = ThemePresets.CreateDefaultCustom();
                    loaded.CustomTheme.Normalize();
                    if (!IsKnownThemeInternal(loaded.SelectedThemeId))
                        loaded.SelectedThemeId = ThemeIds.Windows11;
                    return loaded;
                }
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

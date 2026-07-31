using System.Globalization;

namespace NoFences.Theming
{
    /// <summary>
    /// Small runtime string table for the new theme UI. Existing legacy forms keep
    /// using their resx resources; this avoids modifying generated resource files.
    /// </summary>
    internal static class ThemeText
    {
        public static bool IsChinese =>
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";

        public static string Get(string chinese, string english)
        {
            return IsChinese ? chinese : english;
        }

        public static string ThemeMenu => Get("主题风格...", "Theme...");
    }
}

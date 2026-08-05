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

        public static string DarkMode => Get("黑暗模式", "Dark mode");

        public static string DefaultTheme => Get("默认", "Default");

        public static string IconManagement => Get("图标管理...", "Manage icons...");

        public static string IconManagementHint => Get("选择要移除的图标", "Select icons to remove");

        public static string SelectedItemCount(int count)
        {
            return Get("已选择 " + count + " 项", count + " selected");
        }

        public static string Confirm => Get("确定", "Confirm");

        public static string Cancel => Get("取消", "Cancel");

        public static string RemoveItemsTitle => Get("移除图标", "Remove icons");

        public static string RemoveItemsFailed => Get(
            "部分图标无法恢复到桌面，失败项目仍保留在栅栏中：",
            "Some icons could not be restored to the desktop and remain in the fence:");
    }
}

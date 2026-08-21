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

        public static string NativeMenuTitle => Get("文件菜单", "File menu");

        public static string NativeMenuFailed =>
            Get("无法打开该项目的 Windows 右键菜单。", "Unable to open the Windows context menu for this item.");

        public static string NativeMenuSyncFailed =>
            Get("文件操作已完成，但无法同步该快捷方式的托管记录。", "The file operation completed, but its managed shortcut record could not be synchronized.");

        public static string PersistenceTitle => Get("保存分区", "Save fence");

        public static string PersistenceFailed =>
            Get("无法保存分区设置。本次运行仍可继续，但部分更改可能在重启后丢失。", "Unable to save the fence settings. You can continue this session, but some changes may be lost after restart.");

        public static string RecoveryTitle => Get("恢复桌面快捷方式", "Restore desktop shortcuts");

        public static string RecoveryRunning =>
            Get("NoFences 正在运行。请先退出程序，再执行恢复。", "NoFences is running. Exit it before starting recovery.");

        public static string RecoveryCompleted =>
            Get("所有可恢复的桌面快捷方式均已解除托管。", "All recoverable desktop shortcuts have been released.");

        public static string RecoveryFailed =>
            Get("部分桌面快捷方式无法恢复。没有文件被覆盖。", "Some desktop shortcuts could not be restored. No files were overwritten.");

        public static string DefaultFenceName => Get("第一个分区", "First fence");

        public static string NewFenceName => Get("新建分区", "New fence");

        public static string StartupTitle => Get("NoFences 启动", "NoFences startup");

        public static string StartupWarning =>
            Get("部分桌面快捷方式操作未能完成。没有文件被覆盖。", "Some desktop shortcut operations could not be completed. No files were overwritten.");

        public static string RemoveFenceTitle => Get("移除分区", "Remove fence");

        public static string RemoveFenceQuestion =>
            Get("确定要移除这个分区吗？", "Really remove this fence?");

        public static string RemoveFenceFailed =>
            Get("无法恢复全部桌面快捷方式。为保护文件，分区已保留。", "Unable to restore all desktop shortcuts. The fence was kept to protect its files.");

        public static string ExitTitle => Get("退出 NoFences", "Exit NoFences");

        public static string ExitFailed =>
            Get("部分桌面快捷方式无法恢复。没有文件被覆盖，程序将保持运行。", "Some desktop shortcuts could not be restored. No files were overwritten and the application will remain open.");

        public static string RemoveItemTitle => Get("移除项目", "Remove item");

        public static string RemoveItemFailed =>
            Get("无法恢复桌面快捷方式。该项目仍保留在分区中。", "Unable to restore the desktop shortcut. The item remains in the fence.");

        public static string AddItemsTitle => Get("添加项目", "Add items");

        public static string AddItemsFailed =>
            Get("部分项目无法添加：", "Some items could not be added:");

        public static string RemoveItemsFailed => Get(
            "部分图标无法恢复到桌面，失败项目仍保留在栅栏中：",
            "Some icons could not be restored to the desktop and remain in the fence:");
    }
}

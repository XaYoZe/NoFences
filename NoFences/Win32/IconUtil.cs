using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace NoFences.Win32
{
    /// <summary>
    /// 系统图标工具类。通过 Shell32 SHGetStockIconInfo API
    /// 获取系统级库存图标（如文件夹大图标）。
    /// 参考：https://stackoverflow.com/a/59129804/7702748
    /// </summary>
    public static class IconUtil
    {
        /// <summary>延迟初始化的文件夹大图标缓存</summary>
        private static Icon folderIcon;

        /// <summary>
        /// 获取系统文件夹大图标（带缓存）。
        /// 使用 null 合并运算符实现延迟初始化，只调用一次 SHGetStockIconInfo。
        /// </summary>
        public static Icon FolderLarge => folderIcon ?? (folderIcon = GetStockIcon(SHSIID_FOLDER, SHGSI_LARGEICON));

        /// <summary>
        /// 通过 SHGetStockIconInfo 获取指定类型的库存图标。
        /// </summary>
        private static Icon GetStockIcon(uint type, uint size)
        {
            var info = new SHSTOCKICONINFO();
            info.cbSize = (uint)Marshal.SizeOf(info);

            SHGetStockIconInfo(type, SHGSI_ICON | size, ref info);

            // 克隆一份以便安全释放原始句柄（防止资源泄漏）
            var icon = (Icon)Icon.FromHandle(info.hIcon).Clone();
            DestroyIcon(info.hIcon);

            return icon;
        }

        /// <summary>SHGetStockIconInfo 参数结构体。</summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHSTOCKICONINFO
        {
            public uint cbSize;       // 结构体大小
            public IntPtr hIcon;      // 图标句柄
            public int iSysIconIndex; // 系统图标列表索引
            public int iIcon;         // 图标索引
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szPath;     // 图标文件路径
        }

        [DllImport("shell32.dll")]
        public static extern int SHGetStockIconInfo(uint siid, uint uFlags, ref SHSTOCKICONINFO psii);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr handle);

        // 文件夹图标常量
        private const uint SHSIID_FOLDER = 0x3;
        private const uint SHGSI_ICON = 0x100;
        private const uint SHGSI_LARGEICON = 0x0;
        private const uint SHGSI_SMALLICON = 0x1;
    }
}

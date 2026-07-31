using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace NoFences.Win32
{
    /// <summary>
    /// 系统图标工具类。通过 Shell API 获取文件、文件夹及库存图标，
    /// 并保留快捷方式等 Shell 覆盖层。
    /// </summary>
    public static class IconUtil
    {
        /// <summary>延迟初始化的文件夹大图标缓存</summary>
        private static Icon folderIcon;
        private static Icon linkOverlayIcon;
        private static readonly object iconCacheLock = new object();
        private static readonly IDictionary<string, Icon> largeIconCache =
            new Dictionary<string, Icon>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 获取系统文件夹大图标（带缓存）。
        /// 使用 null 合并运算符实现延迟初始化，只调用一次 SHGetStockIconInfo。
        /// </summary>
        public static Icon FolderLarge => folderIcon ?? (folderIcon = GetStockIcon(SHSIID_FOLDER, SHGSI_LARGEICON));

        /// <summary>系统快捷方式箭头覆盖层。</summary>
        private static Icon LinkOverlay =>
            linkOverlayIcon ?? (linkOverlayIcon = GetStockIcon(SHSIID_LINK, SHGSI_LARGEICON));

        /// <summary>
        /// 通过 Shell 图像工厂获取指定路径的目标尺寸图标并缓存，
        /// 快捷方式会重新叠加系统箭头覆盖层。
        /// </summary>
        public static Icon GetLargeIcon(string path, int targetSize)
        {
            string cacheKey = targetSize + "|" + path;
            lock (iconCacheLock)
            {
                Icon cachedIcon;
                if (largeIconCache.TryGetValue(cacheKey, out cachedIcon))
                    return cachedIcon;
            }

            Icon icon = GetImageFactoryIcon(path, targetSize) ?? GetFallbackIcon(path);
            lock (iconCacheLock)
            {
                Icon cachedIcon;
                if (largeIconCache.TryGetValue(cacheKey, out cachedIcon))
                    return cachedIcon;

                largeIconCache[cacheKey] = icon;
                return icon;
            }
        }

        /// <summary>通过 SHGetFileInfo 快速获取用于异步加载前占位的 Shell 图标。</summary>
        public static Icon GetFallbackIcon(string path)
        {
            var info = new SHFILEINFO();
            IntPtr result = SHGetFileInfo(
                path,
                0,
                ref info,
                (uint)Marshal.SizeOf(typeof(SHFILEINFO)),
                SHGFI_ICON | SHGFI_LARGEICON | SHGFI_ADDOVERLAYS);

            if (result != IntPtr.Zero && info.hIcon != IntPtr.Zero)
            {
                try
                {
                    return (Icon)Icon.FromHandle(info.hIcon).Clone();
                }
                finally
                {
                    DestroyIcon(info.hIcon);
                }
            }

            if (Directory.Exists(path))
                return FolderLarge;

            try
            {
                return Icon.ExtractAssociatedIcon(path) ?? SystemIcons.Application;
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        /// <summary>
        /// 通过 IShellItemImageFactory 按目标尺寸提取高分辨率图标。
        /// 此方法可能访问磁盘，应从后台线程调用。
        /// </summary>
        private static Icon GetImageFactoryIcon(string path, int targetSize)
        {
            IShellItemImageFactory imageFactory = null;
            IntPtr bitmapHandle = IntPtr.Zero;

            try
            {
                var interfaceId = IID_IShellItemImageFactory;
                if (SHCreateItemFromParsingName(
                    path,
                    IntPtr.Zero,
                    ref interfaceId,
                    out imageFactory) < 0 || imageFactory == null)
                {
                    return null;
                }

                var requestedSize = new SIZE
                {
                    Width = targetSize,
                    Height = targetSize
                };
                if (imageFactory.GetImage(
                    requestedSize,
                    SIIGBF_BIGGERSIZEOK | SIIGBF_ICONONLY | SIIGBF_SCALEUP,
                    out bitmapHandle) < 0 || bitmapHandle == IntPtr.Zero)
                {
                    return null;
                }

                using (var bitmap = CreateBitmapWithAlpha(bitmapHandle))
                {
                    if (bitmap == null)
                        return null;

                    ApplyShortcutOverlay(path, bitmap);

                    IntPtr iconHandle = bitmap.GetHicon();
                    try
                    {
                        return (Icon)Icon.FromHandle(iconHandle).Clone();
                    }
                    finally
                    {
                        DestroyIcon(iconHandle);
                    }
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                if (bitmapHandle != IntPtr.Zero)
                    DeleteObject(bitmapHandle);
                if (imageFactory != null && Marshal.IsComObject(imageFactory))
                {
                    try
                    {
                        Marshal.FinalReleaseComObject(imageFactory);
                    }
                    catch (InvalidComObjectException)
                    {
                    }
                }
            }
        }

        /// <summary>为快捷方式高分辨率图标重新叠加系统箭头覆盖层。</summary>
        private static void ApplyShortcutOverlay(string path, Bitmap bitmap)
        {
            string extension = Path.GetExtension(path);
            if (!extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            using (var overlayBitmap = LinkOverlay.ToBitmap())
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.DrawImage(
                    overlayBitmap,
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            }
        }

        /// <summary>将 Shell 返回的 32 位 HBITMAP 转换为保留 Alpha 通道的位图。</summary>
        private static Bitmap CreateBitmapWithAlpha(IntPtr bitmapHandle)
        {
            BITMAP nativeBitmap;
            if (GetObject(bitmapHandle, Marshal.SizeOf(typeof(BITMAP)), out nativeBitmap) == 0)
                return null;

            int width = Math.Abs(nativeBitmap.Width);
            int height = Math.Abs(nativeBitmap.Height);
            if (width == 0 || height == 0)
                return null;

            var header = new BITMAPINFOHEADER
            {
                Size = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER)),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
                SizeImage = (uint)(width * height * 4)
            };
            byte[] pixels = new byte[header.SizeImage];
            IntPtr deviceContext = GetDC(IntPtr.Zero);
            try
            {
                if (deviceContext == IntPtr.Zero || GetDIBits(
                    deviceContext,
                    bitmapHandle,
                    0,
                    (uint)height,
                    pixels,
                    ref header,
                    DIB_RGB_COLORS) == 0)
                {
                    return null;
                }
            }
            finally
            {
                if (deviceContext != IntPtr.Zero)
                    ReleaseDC(IntPtr.Zero, deviceContext);
            }

            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            var bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppPArgb);
            try
            {
                int sourceStride = width * 4;
                for (int row = 0; row < height; row++)
                {
                    Marshal.Copy(
                        pixels,
                        row * sourceStride,
                        IntPtr.Add(bitmapData.Scan0, row * bitmapData.Stride),
                        sourceStride);
                }
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            return bitmap;
        }

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

        /// <summary>SHGetFileInfo 返回的文件图标信息。</summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        /// <summary>Shell 图像工厂请求尺寸。</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int Width;
            public int Height;
        }

        /// <summary>原生 HBITMAP 基本信息。</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAP
        {
            public int Type;
            public int Width;
            public int Height;
            public int WidthBytes;
            public ushort Planes;
            public ushort BitsPixel;
            public IntPtr Bits;
        }

        /// <summary>GetDIBits 使用的 32 位位图头。</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint Size;
            public int Width;
            public int Height;
            public ushort Planes;
            public ushort BitCount;
            public uint Compression;
            public uint SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public uint ColorsUsed;
            public uint ColorsImportant;
        }

        [ComImport]
        [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(SIZE size, uint flags, out IntPtr bitmapHandle);
        }

        [DllImport("shell32.dll")]
        public static extern int SHGetStockIconInfo(uint siid, uint uFlags, ref SHSTOCKICONINFO psii);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(
            string pszPath,
            uint dwFileAttributes,
            ref SHFILEINFO psfi,
            uint cbFileInfo,
            uint uFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(
            string path,
            IntPtr bindContext,
            ref Guid interfaceId,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory imageFactory);

        [DllImport("gdi32.dll")]
        private static extern int GetObject(IntPtr handle, int objectSize, out BITMAP bitmap);

        [DllImport("gdi32.dll")]
        private static extern int GetDIBits(
            IntPtr deviceContext,
            IntPtr bitmap,
            uint startScan,
            uint scanLines,
            byte[] bits,
            ref BITMAPINFOHEADER bitmapInfo,
            uint usage);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr window);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr handle);

        // 文件夹图标常量
        private const uint SHSIID_FOLDER = 0x3;
        private const uint SHSIID_LINK = 0x1D;
        private const uint SHGSI_ICON = 0x100;
        private const uint SHGSI_LARGEICON = 0x0;
        private const uint SHGSI_SMALLICON = 0x1;
        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;
        private const uint SHGFI_ADDOVERLAYS = 0x000000020;
        private const uint SIIGBF_BIGGERSIZEOK = 0x00000001;
        private const uint SIIGBF_ICONONLY = 0x00000004;
        private const uint SIIGBF_SCALEUP = 0x00000100;
        private const uint DIB_RGB_COLORS = 0;
        private static readonly Guid IID_IShellItemImageFactory =
            new Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B");
    }
}

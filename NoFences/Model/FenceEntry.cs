using System.Drawing;
using System.Threading.Tasks;
using System.Diagnostics;
using System;
using System.IO;
using NoFences.Win32;
using NoFences.Util;

namespace NoFences.Model
{
    /// <summary>
    /// 表示栅栏中的一个条目（文件或文件夹）。
    /// 负责提取图标和打开目标路径。
    /// </summary>
    public class FenceEntry
    {
        /// <summary>文件或文件夹的完整路径</summary>
        public string Path { get; }

        /// <summary>条目类型（文件/文件夹）</summary>
        public EntryType Type { get; }

        /// <summary>显示名称（不含扩展名）</summary>
        public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

        private FenceEntry(string path, EntryType type)
        {
            Path = path;
            Type = type;
        }

        /// <summary>
        /// 根据路径创建条目实例。自动检测是文件还是文件夹。
        /// </summary>
        public static FenceEntry FromPath(string path)
        {
            if (File.Exists(path))
                return new FenceEntry(path, EntryType.File);
            else if (Directory.Exists(path))
                return new FenceEntry(path, EntryType.Folder);
            else return null;
        }

        /// <summary>
        /// 提取条目的显示图标。
        /// 文件：优先使用缩略图（图片类），否则使用关联图标。
        /// 文件夹：使用缓存的系统文件夹大图标。
        /// </summary>
        public Icon ExtractIcon(ThumbnailProvider thumbnailProvider)
        {
            if (Type == EntryType.File)
            {
                if (thumbnailProvider.IsSupported(Path))
                    return thumbnailProvider.GenerateThumbnail(Path); // 图片文件生成缩略图
                else
                    return Icon.ExtractAssociatedIcon(Path); // 其他文件使用关联图标
            }
            else
            {
                return IconUtil.FolderLarge; // 文件夹使用缓存的系统大图标
            }
        }

        /// <summary>
        /// 打开条目。使用 fire-and-forget 的 Task.Run 异步启动，
        /// 不阻塞 UI 线程（不要 await 此方法）。
        /// </summary>
        public void Open()
        {
            Task.Run(() =>
            {
                try
                {
                    if (Type == EntryType.File)
                        Process.Start(Path);          // 使用默认程序打开文件
                    else if (Type == EntryType.Folder)
                        Process.Start("explorer.exe", Path); // 在资源管理器中打开文件夹
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to start: {e}");
                }
            });
        }
    }
}

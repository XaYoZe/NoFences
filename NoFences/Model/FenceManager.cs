using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;

namespace NoFences.Model
{
    /// <summary>
    /// 栅栏管理器（单例）。负责从磁盘加载/保存栅栏元数据，
    /// 以及创建和删除栅栏窗口。
    /// 
    /// 数据存储路径：%LocalAppData%/NoFences/<guid>/__fence_metadata.xml
    /// </summary>
    public class FenceManager
    {
        /// <summary>全局单例</summary>
        public static FenceManager Instance { get; } = new FenceManager();

        private const string MetaFileName = "__fence_metadata.xml";

        /// <summary>栅栏数据根目录：%LocalAppData%/NoFences/</summary>
        private readonly string basePath;

        private readonly DesktopShortcutManager desktopShortcutManager = new DesktopShortcutManager();
        private readonly List<FenceInfo> loadedFences = new List<FenceInfo>();
        private readonly object metadataLock = new object();

        public FenceManager()
        {
            basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoFences");
            EnsureDirectoryExists(basePath);
        }

        /// <summary>
        /// 从磁盘加载所有栅栏并创建对应的 FenceWindow。
        /// 遍历 basePath 下的每个子目录，反序列化其中的 __fence_metadata.xml。
        /// </summary>
        public string LoadFences()
        {
            var warnings = new List<string>();
            foreach (var dir in Directory.EnumerateDirectories(basePath))
            {
                var metaFile = Path.Combine(dir, MetaFileName);
                var serializer = new XmlSerializer(typeof(FenceInfo));
                FenceInfo fence;
                using (var reader = new StreamReader(metaFile))
                    fence = serializer.Deserialize(reader) as FenceInfo;
                if (fence == null)
                    continue;

                string operationError;
                if (fence.PendingRemoval)
                {
                    if (TryCompletePendingFenceRemoval(fence, out operationError))
                        continue;

                    warnings.Add(fence.Name + "：" + operationError);
                }
                else if (!desktopShortcutManager.PrepareForApplicationRun(
                    fence,
                    UpdateFence,
                    out operationError))
                {
                    warnings.Add(fence.Name + "：" + operationError);
                }

                loadedFences.Add(fence);
                new FenceWindow(fence).Show();
            }

            return string.Join(Environment.NewLine, warnings);
        }

        /// <summary>
        /// 创建新栅栏并显示。
        /// </summary>
        public void CreateFence(string name)
        {
            var fenceInfo = new FenceInfo(Guid.NewGuid())
            {
                Name = name,
                PosX = 100,
                PosY = 250,
                Height = 300,
                Width = 300
            };

            UpdateFence(fenceInfo);
            loadedFences.Add(fenceInfo);
            new FenceWindow(fenceInfo).Show();
        }

        /// <summary>
        /// 添加文件到栅栏。桌面根目录中的快捷方式会被搬入栅栏专属托管目录，
        /// 其他文件和目录仍仅保存原始路径。
        /// </summary>
        public bool TryAddEntry(FenceInfo info, string path, out string error)
        {
            if (info.Files == null)
                info.Files = new List<string>();

            if (desktopShortcutManager.IsDesktopShortcut(path))
            {
                return desktopShortcutManager.TryManageShortcut(
                    info,
                    path,
                    GetItemsFolderPath(info),
                    UpdateFence,
                    out error);
            }

            if (!ContainsPath(info.Files, path))
            {
                info.Files.Add(path);
                UpdateFence(info);
            }

            error = null;
            return true;
        }

        /// <summary>
        /// 从栅栏移除条目。受托管的桌面快捷方式会先恢复文件和原坐标，
        /// 普通条目只从文件列表删除。
        /// </summary>
        public bool TryRemoveEntry(FenceInfo info, string path, out string error)
        {
            if (desktopShortcutManager.IsTrackedEntry(info, path))
            {
                return desktopShortcutManager.TryReleaseEntry(
                    info,
                    path,
                    UpdateFence,
                    out error);
            }

            RemovePath(info.Files, path);
            UpdateFence(info);
            error = null;
            return true;
        }

        /// <summary>判断栅栏中的路径是否为由应用托管的桌面快捷方式。</summary>
        public bool IsManagedDesktopEntry(FenceInfo info, string path)
        {
            return desktopShortcutManager.IsTrackedEntry(info, path);
        }

        /// <summary>
        /// 删除栅栏前先恢复所有桌面快捷方式。任何恢复失败都会保留栅栏目录，
        /// 防止托管文件因递归删除而丢失。
        /// </summary>
        public bool TryRemoveFence(FenceInfo info, out string error)
        {
            info.PendingRemoval = true;
            UpdateFence(info);

            if (!desktopShortcutManager.TryReleaseAll(info, UpdateFence, out error))
                return false;

            if (!TryDeleteFenceDirectory(info, out error))
                return false;

            loadedFences.Remove(info);
            return true;
        }

        /// <summary>
        /// 正常退出应用前恢复全部已加载栅栏中的桌面快捷方式。
        /// 发生同名冲突时返回错误并保留托管副本，不覆盖桌面文件。
        /// </summary>
        public bool TryRestoreDesktopShortcutsForExit(out string error)
        {
            var errors = new List<string>();
            foreach (FenceInfo fence in loadedFences.ToArray())
            {
                string fenceError;
                if (!desktopShortcutManager.TryRestoreTrackedForExit(
                    fence,
                    UpdateFence,
                    out fenceError))
                {
                    errors.Add(fence.Name + "：" + fenceError);
                }
            }

            error = string.Join(Environment.NewLine, errors);
            return errors.Count == 0;
        }

        /// <summary>
        /// 将栅栏元数据序列化到磁盘（XmlSerializer）。
        /// </summary>
        public void UpdateFence(FenceInfo fenceInfo)
        {
            lock (metadataLock)
            {
                var path = GetFolderPath(fenceInfo);
                EnsureDirectoryExists(path);

                var metaFile = Path.Combine(path, MetaFileName);
                var temporaryFile = Path.Combine(
                    path,
                    MetaFileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
                var serializer = new XmlSerializer(typeof(FenceInfo));
                try
                {
                    using (var writer = new StreamWriter(temporaryFile))
                        serializer.Serialize(writer, fenceInfo);

                    if (File.Exists(metaFile))
                        File.Replace(temporaryFile, metaFile, null);
                    else
                        File.Move(temporaryFile, metaFile);
                }
                finally
                {
                    TryDeleteTemporaryMetadata(temporaryFile);
                }
            }
        }

        /// <summary>尽力清理由中断或写盘失败遗留的临时元数据文件。</summary>
        private static void TryDeleteTemporaryMetadata(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        /// <summary>继续完成上次未结束的栅栏删除事务。</summary>
        private bool TryCompletePendingFenceRemoval(FenceInfo info, out string error)
        {
            if (!desktopShortcutManager.TryReleaseAll(info, UpdateFence, out error))
                return false;

            return TryDeleteFenceDirectory(info, out error);
        }

        /// <summary>
        /// 确认托管目录没有遗留文件后删除栅栏数据目录。
        /// 遗留文件会阻止删除，避免清理未知或未恢复的数据。
        /// </summary>
        private bool TryDeleteFenceDirectory(FenceInfo info, out string error)
        {
            try
            {
                string itemsDirectory = GetItemsFolderPath(info);
                if (Directory.Exists(itemsDirectory) &&
                    Directory.EnumerateFiles(itemsDirectory, "*", SearchOption.AllDirectories).Any())
                {
                    error = "托管目录仍有未恢复文件，已取消删除。";
                    return false;
                }

                string folderPath = GetFolderPath(info);
                if (Directory.Exists(folderPath))
                    Directory.Delete(folderPath, true);

                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = "无法删除栅栏数据目录：" + ex.Message;
                return false;
            }
        }

        /// <summary>确保目录存在，不存在则创建。</summary>
        private void EnsureDirectoryExists(string dir)
        {
            var di = new DirectoryInfo(dir);
            if (!di.Exists)
                di.Create();
        }

        /// <summary>获取栅栏对应的存储目录路径。</summary>
        private string GetFolderPath(FenceInfo fenceInfo)
        {
            return Path.Combine(basePath, fenceInfo.Id.ToString());
        }

        /// <summary>获取栅栏内桌面快捷方式的专属托管根目录。</summary>
        private string GetItemsFolderPath(FenceInfo fenceInfo)
        {
            return Path.Combine(GetFolderPath(fenceInfo), "items");
        }

        /// <summary>按 Windows 路径规则判断文件列表是否已包含指定路径。</summary>
        private static bool ContainsPath(IEnumerable<string> paths, string path)
        {
            return paths != null && paths.Any(candidate => PathsEqual(candidate, path));
        }

        /// <summary>按 Windows 路径规则从文件列表删除指定路径的所有实例。</summary>
        private static void RemovePath(List<string> paths, string path)
        {
            if (paths == null)
                return;

            for (int index = paths.Count - 1; index >= 0; index--)
            {
                if (PathsEqual(paths[index], path))
                    paths.RemoveAt(index);
            }
        }

        /// <summary>将两个路径标准化后按不区分大小写方式比较。</summary>
        private static bool PathsEqual(string firstPath, string secondPath)
        {
            if (string.IsNullOrWhiteSpace(firstPath) || string.IsNullOrWhiteSpace(secondPath))
                return false;

            try
            {
                firstPath = Path.GetFullPath(firstPath).TrimEnd('\\', '/');
                secondPath = Path.GetFullPath(secondPath).TrimEnd('\\', '/');
            }
            catch
            {
            }

            return string.Equals(firstPath, secondPath, StringComparison.OrdinalIgnoreCase);
        }
    }
}

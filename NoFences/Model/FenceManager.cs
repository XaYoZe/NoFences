using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
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
        private const string MetaBackupFileName = "__fence_metadata.xml.bak";
        private const long MaximumMetadataBytes = 4L * 1024L * 1024L;
        private static readonly XmlSerializer FenceSerializer =
            new XmlSerializer(typeof(FenceInfo));

        /// <summary>栅栏数据根目录：%LocalAppData%/NoFences/</summary>
        private readonly string basePath;

        private readonly DesktopShortcutManager desktopShortcutManager = new DesktopShortcutManager();
        private readonly List<FenceInfo> loadedFences = new List<FenceInfo>();
        private readonly Dictionary<FenceInfo, string> fenceDirectories =
            new Dictionary<FenceInfo, string>();
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
            string[] directories;
            string enumerationError;
            if (!TryGetFenceDirectories(out directories, out enumerationError))
                return enumerationError;

            foreach (var dir in directories)
            {
                FenceInfo fence;
                string loadWarning;
                bool loadedFromBackup;
                if (!TryLoadFence(dir, out fence, out loadWarning, out loadedFromBackup))
                {
                    if (!string.IsNullOrWhiteSpace(loadWarning))
                        warnings.Add(loadWarning);
                    continue;
                }

                fenceDirectories[fence] = Path.GetFullPath(dir);
                if (!string.IsNullOrWhiteSpace(loadWarning))
                    warnings.Add(loadWarning);
                if (loadedFromBackup)
                {
                    try
                    {
                        File.Copy(
                            Path.Combine(dir, MetaBackupFileName),
                            Path.Combine(dir, MetaFileName),
                            true);
                    }
                    catch (Exception ex)
                    {
                        warnings.Add(fence.Name + "：无法修复主元数据文件：" + ex.Message);
                    }
                }

                string operationError;
                try
                {
                    if (fence.PendingRemoval)
                    {
                        if (TryCompletePendingFenceRemoval(fence, out operationError))
                        {
                            fenceDirectories.Remove(fence);
                            continue;
                        }

                        CancelPendingRemoval(fence, ref operationError);
                        warnings.Add(fence.Name + "：" + operationError);
                    }
                    else if (!desktopShortcutManager.PrepareForApplicationRun(
                        fence,
                        GetItemsFolderPath(fence),
                        UpdateFence,
                        out operationError))
                    {
                        warnings.Add(fence.Name + "：" + operationError);
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add(fence.Name + "：无法校正分区状态：" + ex.Message);
                }

                try
                {
                    loadedFences.Add(fence);
                    new FenceWindow(fence).Show();
                }
                catch (Exception ex)
                {
                    loadedFences.Remove(fence);
                    fenceDirectories.Remove(fence);
                    warnings.Add(fence.Name + "：无法创建分区窗口：" + ex.Message);
                }
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

            fenceDirectories[fenceInfo] = Path.Combine(basePath, fenceInfo.Id.ToString());
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
            try
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
                    try
                    {
                        UpdateFence(info);
                    }
                    catch
                    {
                        RemovePath(info.Files, path);
                        throw;
                    }
                }

                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = "无法添加项目：" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 从栅栏移除条目。受托管的桌面快捷方式会先恢复文件和原坐标，
        /// 普通条目只从文件列表删除。
        /// </summary>
        public bool TryRemoveEntry(FenceInfo info, string path, out string error)
        {
            try
            {
                if (desktopShortcutManager.IsTrackedEntry(info, path))
                {
                    return desktopShortcutManager.TryReleaseEntry(
                        info,
                        path,
                        GetItemsFolderPath(info),
                        UpdateFence,
                        out error);
                }

                var previousFiles = info.Files != null
                    ? new List<string>(info.Files)
                    : new List<string>();
                RemovePath(info.Files, path);
                try
                {
                    UpdateFence(info);
                }
                catch
                {
                    info.Files = previousFiles;
                    throw;
                }
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = "无法移除项目：" + ex.Message;
                return false;
            }
        }

        /// <summary>判断栅栏中的路径是否为由应用托管的桌面快捷方式。</summary>
        public bool IsManagedDesktopEntry(FenceInfo info, string path)
        {
            return desktopShortcutManager.IsTrackedEntry(info, path);
        }

        /// <summary>同步托管快捷方式在原生 Shell 菜单中发生的重命名、移动或删除。</summary>
        public bool ReconcileManagedEntryAfterShellCommand(
            FenceInfo info,
            string path,
            out string error)
        {
            try
            {
                return desktopShortcutManager.ReconcileAfterShellCommand(
                    info,
                    path,
                    GetItemsFolderPath(info),
                    UpdateFence,
                    out error);
            }
            catch (Exception ex)
            {
                error = "无法同步原生菜单操作：" + ex.Message;
                return false;
            }
        }

        /// <summary>返回当前分区实际绑定的数据目录，供文件变化监控使用。</summary>
        public string GetFenceDataDirectory(FenceInfo info)
        {
            return GetFolderPath(info);
        }

        /// <summary>
        /// 删除栅栏前先恢复所有桌面快捷方式。任何恢复失败都会保留栅栏目录，
        /// 防止托管文件因递归删除而丢失。
        /// </summary>
        public bool TryRemoveFence(FenceInfo info, out string error)
        {
            try
            {
                info.PendingRemoval = true;
                UpdateFence(info);
            }
            catch (Exception ex)
            {
                info.PendingRemoval = false;
                error = "无法保存删除事务：" + ex.Message;
                return false;
            }

            try
            {
                if (!desktopShortcutManager.TryReleaseAll(
                    info,
                    GetItemsFolderPath(info),
                    UpdateFence,
                    out error))
                {
                    CancelPendingRemoval(info, ref error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = "无法恢复分区中的快捷方式：" + ex.Message;
                CancelPendingRemoval(info, ref error);
                return false;
            }

            if (!TryDeleteFenceDirectory(info, out error))
            {
                CancelPendingRemoval(info, ref error);
                return false;
            }

            loadedFences.Remove(info);
            fenceDirectories.Remove(info);
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
                try
                {
                    if (!desktopShortcutManager.TryRestoreTrackedForExit(
                        fence,
                        GetItemsFolderPath(fence),
                        UpdateFence,
                        out fenceError))
                    {
                        errors.Add(fence.Name + "：" + fenceError);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(fence.Name + "：无法恢复快捷方式：" + ex.Message);
                }
            }

            error = string.Join(Environment.NewLine, errors);
            return errors.Count == 0;
        }

        /// <summary>
        /// 不创建窗口、也不重新隐藏桌面图标，直接遍历磁盘上的全部分区并永久解除
        /// 快捷方式托管。供卸载和手工故障恢复使用。
        /// </summary>
        public bool TryReleaseAllStoredDesktopShortcuts(out string error)
        {
            var errors = new List<string>();
            string[] directories;
            string enumerationError;
            if (!TryGetFenceDirectories(out directories, out enumerationError))
            {
                error = enumerationError;
                return false;
            }

            foreach (string directory in directories)
            {
                FenceInfo fence;
                string loadWarning;
                bool loadedFromBackup;
                if (!TryLoadFence(
                    directory,
                    out fence,
                    out loadWarning,
                    out loadedFromBackup))
                {
                    if (!string.IsNullOrWhiteSpace(loadWarning))
                        errors.Add(loadWarning);
                    continue;
                }

                fenceDirectories[fence] = Path.GetFullPath(directory);
                try
                {
                    if (loadedFromBackup)
                    {
                        File.Copy(
                            Path.Combine(directory, MetaBackupFileName),
                            Path.Combine(directory, MetaFileName),
                            true);
                    }

                    string releaseError;
                    if (!desktopShortcutManager.TryReleaseAll(
                        fence,
                        GetItemsFolderPath(fence),
                        UpdateFence,
                        out releaseError))
                    {
                        errors.Add(fence.Name + "：" + releaseError);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(fence.Name + "：无法恢复快捷方式：" + ex.Message);
                }
                finally
                {
                    fenceDirectories.Remove(fence);
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
                try
                {
                    using (var stream = new FileStream(
                        temporaryFile,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None))
                    using (var writer = new StreamWriter(stream))
                    {
                        FenceSerializer.Serialize(writer, fenceInfo);
                        writer.Flush();
                        if (stream.Length > MaximumMetadataBytes)
                            throw new InvalidDataException("分区元数据超过安全大小限制，已保留上一版本。");
                        stream.Flush(true);
                    }

                    if (File.Exists(metaFile))
                    {
                        var backupFile = Path.Combine(path, MetaBackupFileName);
                        try
                        {
                            File.Replace(temporaryFile, metaFile, backupFile);
                        }
                        catch (PlatformNotSupportedException)
                        {
                            File.Copy(metaFile, backupFile, true);
                            File.Copy(temporaryFile, metaFile, true);
                            File.Delete(temporaryFile);
                        }
                    }
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
            if (!desktopShortcutManager.TryReleaseAll(
                info,
                GetItemsFolderPath(info),
                UpdateFence,
                out error))
                return false;

            return TryDeleteFenceDirectory(info, out error);
        }

        /// <summary>取消未完成的删除事务，使保留下来的分区不会在下次启动时自动删除。</summary>
        private void CancelPendingRemoval(FenceInfo info, ref string error)
        {
            info.PendingRemoval = false;
            try
            {
                UpdateFence(info);
            }
            catch (Exception ex)
            {
                string rollbackError = "无法取消待删除状态：" + ex.Message;
                error = string.IsNullOrWhiteSpace(error)
                    ? rollbackError
                    : error + Environment.NewLine + rollbackError;
            }
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
                string unexpectedEntry;
                if (!ContainsOnlyManagedFenceData(folderPath, itemsDirectory, out unexpectedEntry))
                {
                    error = "分区数据目录包含未知内容，已取消删除：" + unexpectedEntry;
                    return false;
                }
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
            string boundDirectory;
            if (fenceDirectories.TryGetValue(fenceInfo, out boundDirectory))
                return boundDirectory;
            return Path.Combine(basePath, fenceInfo.Id.ToString());
        }

        /// <summary>获取栅栏内桌面快捷方式的专属托管根目录。</summary>
        private string GetItemsFolderPath(FenceInfo fenceInfo)
        {
            return Path.Combine(GetFolderPath(fenceInfo), "items");
        }

        /// <summary>安全枚举分区目录，根目录不可访问时返回可展示的错误而不是终止进程。</summary>
        private bool TryGetFenceDirectories(out string[] directories, out string error)
        {
            try
            {
                directories = Directory.GetDirectories(basePath);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                directories = new string[0];
                error = "无法读取分区数据目录：" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 独立加载一个分区。主元数据损坏时尝试最近备份，并验证目录名与序列化 ID
        /// 一致，避免一份复制或损坏的 XML 操作到其他分区目录。
        /// </summary>
        private static bool TryLoadFence(
            string directory,
            out FenceInfo fence,
            out string warning,
            out bool loadedFromBackup)
        {
            fence = null;
            warning = null;
            loadedFromBackup = false;
            Guid directoryId;
            if (!Guid.TryParse(Path.GetFileName(directory), out directoryId))
            {
                warning = "已忽略无法识别的分区数据目录：" + directory;
                return false;
            }

            string metaFile = Path.Combine(directory, MetaFileName);
            string backupFile = Path.Combine(directory, MetaBackupFileName);
            Exception primaryError = null;
            foreach (string candidate in new[] { metaFile, backupFile })
            {
                if (!File.Exists(candidate))
                    continue;

                try
                {
                    var fileInfo = new FileInfo(candidate);
                    if (fileInfo.Length > MaximumMetadataBytes)
                        throw new InvalidDataException("元数据文件超过安全大小限制。");

                    var settings = new XmlReaderSettings
                    {
                        DtdProcessing = DtdProcessing.Prohibit,
                        XmlResolver = null,
                        MaxCharactersInDocument = MaximumMetadataBytes * 2
                    };
                    using (var stream = new FileStream(
                        candidate,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read))
                    using (var reader = XmlReader.Create(stream, settings))
                        fence = FenceSerializer.Deserialize(reader) as FenceInfo;
                    if (fence == null)
                        throw new InvalidDataException("元数据内容为空。");
                    if (fence.Id != directoryId)
                        throw new InvalidDataException("元数据 ID 与所属目录不匹配。");

                    if (!string.Equals(candidate, metaFile, StringComparison.OrdinalIgnoreCase))
                    {
                        loadedFromBackup = true;
                        warning = "分区 “" + (fence.Name ?? directoryId.ToString()) +
                                  "” 的主元数据损坏，已从备份恢复。";
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    if (primaryError == null)
                        primaryError = ex;
                    fence = null;
                }
            }

            warning = "无法加载分区数据 “" + directory + "”：" +
                      (primaryError != null ? primaryError.Message : "缺少元数据文件。");
            return false;
        }

        /// <summary>删除前确认分区根目录只包含应用自身创建的元数据和托管目录。</summary>
        private static bool ContainsOnlyManagedFenceData(
            string folderPath,
            string itemsDirectory,
            out string unexpectedEntry)
        {
            unexpectedEntry = null;
            if (!Directory.Exists(folderPath))
                return true;

            foreach (string file in Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(file);
                bool isMetadata = string.Equals(name, MetaFileName, StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(name, MetaBackupFileName, StringComparison.OrdinalIgnoreCase) ||
                                  (name.StartsWith(MetaFileName + ".", StringComparison.OrdinalIgnoreCase) &&
                                   name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
                if (!isMetadata)
                {
                    unexpectedEntry = file;
                    return false;
                }
            }

            foreach (string directory in Directory.EnumerateDirectories(folderPath, "*", SearchOption.TopDirectoryOnly))
            {
                if (!string.Equals(
                    Path.GetFullPath(directory).TrimEnd('\\', '/'),
                    Path.GetFullPath(itemsDirectory).TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase))
                {
                    unexpectedEntry = directory;
                    return false;
                }
            }
            return true;
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

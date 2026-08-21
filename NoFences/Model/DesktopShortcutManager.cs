using NoFences.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace NoFences.Model
{
    /// <summary>
    /// 管理桌面快捷方式在原桌面路径与 NoFences 托管目录之间的安全搬移。
    /// 每次文件操作前后都会持久化状态，并在启动时按目标状态完成恢复。
    /// </summary>
    internal sealed class DesktopShortcutManager
    {
        private static readonly HashSet<string> SupportedExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".lnk",
                ".url",
                ".website"
            };

        /// <summary>
        /// 判断路径是否为用户桌面或公共桌面根目录中的普通快捷方式文件。
        /// 子目录内文件和 Shell 虚拟图标不会被接管。
        /// </summary>
        public bool IsDesktopShortcut(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
                !SupportedExtensions.Contains(Path.GetExtension(path)))
            {
                return false;
            }

            string parent = Path.GetDirectoryName(NormalizePath(path));
            string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

            return PathEquals(parent, userDesktop) || PathEquals(parent, publicDesktop);
        }

        /// <summary>
        /// 判断给定栅栏路径是否对应一个仍受跟踪的桌面快捷方式。
        /// 原路径和托管路径均可用于匹配。
        /// </summary>
        public bool IsTrackedEntry(FenceInfo fenceInfo, string path)
        {
            EnsureCollections(fenceInfo);
            return FindRecord(fenceInfo, path) != null;
        }

        /// <summary>
        /// 将一个桌面快捷方式加入栅栏并搬入应用托管目录。
        /// 如果它已经被当前栅栏跟踪，则只校正未完成的搬移状态。
        /// </summary>
        public bool TryManageShortcut(
            FenceInfo fenceInfo,
            string originalPath,
            string itemsDirectory,
            Action<FenceInfo> persist,
            out string error)
        {
            error = null;
            EnsureCollections(fenceInfo);
            RemoveNullRecords(fenceInfo, persist);

            DesktopShortcutInfo record = FindRecord(fenceInfo, originalPath);
            bool wasAlreadyListed = ContainsPath(fenceInfo.Files, originalPath);

            if (record == null)
            {
                record = new DesktopShortcutInfo
                {
                    Id = Guid.NewGuid(),
                    OriginalPath = NormalizePath(originalPath),
                    State = DesktopShortcutState.Restored,
                    KeepTracked = true
                };
                record.ManagedPath = Path.Combine(
                    itemsDirectory,
                    record.Id.ToString("N"),
                    Path.GetFileName(record.OriginalPath));

                fenceInfo.DesktopShortcuts.Add(record);
                if (!wasAlreadyListed)
                    fenceInfo.Files.Add(record.OriginalPath);
                try
                {
                    persist(fenceInfo);
                }
                catch (Exception ex)
                {
                    fenceInfo.DesktopShortcuts.Remove(record);
                    if (!wasAlreadyListed)
                        RemovePaths(fenceInfo.Files, record.OriginalPath, record.ManagedPath);
                    error = "无法保存快捷方式接管事务：" + ex.Message;
                    return false;
                }
            }
            else if (!record.KeepTracked)
            {
                error = "该快捷方式正在恢复到桌面，请稍后重试。";
                return false;
            }

            if (!ValidateRecordLocation(record, itemsDirectory, out error))
                return false;

            if (EnsureManaged(fenceInfo, record, persist, out error))
                return true;

            // 首次接管尚未改变原文件时，撤销新建记录，保持原有栅栏数据不变。
            if (record.State == DesktopShortcutState.MovingToStorage &&
                File.Exists(record.OriginalPath) && !File.Exists(record.ManagedPath))
            {
                fenceInfo.DesktopShortcuts.Remove(record);
                if (!wasAlreadyListed)
                    RemovePaths(fenceInfo.Files, record.OriginalPath, record.ManagedPath);
                persist(fenceInfo);
            }

            return false;
        }

        /// <summary>
        /// 应用启动时校正所有事务状态：待移除条目优先恢复，
        /// 仍受跟踪的条目则搬回托管目录并从桌面隐藏。
        /// </summary>
        public bool PrepareForApplicationRun(
            FenceInfo fenceInfo,
            string itemsDirectory,
            Action<FenceInfo> persist,
            out string error)
        {
            EnsureCollections(fenceInfo);
            RemoveNullRecords(fenceInfo, persist);
            var errors = new List<string>();

            foreach (DesktopShortcutInfo record in fenceInfo.DesktopShortcuts.ToArray())
            {
                string itemError;
                if (!ValidateRecordLocation(record, itemsDirectory, out itemError))
                {
                    errors.Add(FormatError(record, itemError));
                    continue;
                }

                if (record.State == DesktopShortcutState.Managed &&
                    !File.Exists(record.ManagedPath))
                {
                    if (!ReconcileAfterShellCommand(
                        fenceInfo,
                        record.ManagedPath,
                        itemsDirectory,
                        persist,
                        out itemError))
                    {
                        errors.Add(FormatError(record, itemError));
                        continue;
                    }
                    if (!fenceInfo.DesktopShortcuts.Contains(record))
                        continue;
                }

                if (!record.KeepTracked)
                {
                    if (EnsureRestored(fenceInfo, record, persist, out itemError))
                        RemoveTracking(fenceInfo, record, persist);
                    else
                        errors.Add(FormatError(record, itemError));
                }
                else if (!EnsureManaged(fenceInfo, record, persist, out itemError))
                {
                    errors.Add(FormatError(record, itemError));
                }
            }

            error = JoinErrors(errors);
            return errors.Count == 0;
        }

        /// <summary>
        /// 正常退出应用前将所有快捷方式恢复到桌面。仍受跟踪的记录会保留，
        /// 供下次启动再次隐藏；待解除跟踪的记录则在恢复后彻底移除。
        /// </summary>
        public bool TryRestoreTrackedForExit(
            FenceInfo fenceInfo,
            string itemsDirectory,
            Action<FenceInfo> persist,
            out string error)
        {
            EnsureCollections(fenceInfo);
            RemoveNullRecords(fenceInfo, persist);
            var errors = new List<string>();

            foreach (DesktopShortcutInfo record in fenceInfo.DesktopShortcuts.ToArray())
            {
                string itemError;
                if (!ValidateRecordLocation(record, itemsDirectory, out itemError))
                {
                    errors.Add(FormatError(record, itemError));
                    continue;
                }

                if (record.State == DesktopShortcutState.Restored &&
                    File.Exists(record.OriginalPath) &&
                    !File.Exists(record.ManagedPath))
                {
                    if (!record.KeepTracked)
                        RemoveTracking(fenceInfo, record, persist);
                    continue;
                }

                if (EnsureRestored(fenceInfo, record, persist, out itemError))
                {
                    if (!record.KeepTracked)
                        RemoveTracking(fenceInfo, record, persist);
                }
                else
                {
                    errors.Add(FormatError(record, itemError));
                }
            }

            error = JoinErrors(errors);
            return errors.Count == 0;
        }

        /// <summary>
        /// 从栅栏移除指定快捷方式。先持久化“不再跟踪”，再恢复文件和坐标，
        /// 最后删除跟踪记录，确保中途退出时下次启动不会再次隐藏它。
        /// </summary>
        public bool TryReleaseEntry(
            FenceInfo fenceInfo,
            string entryPath,
            string itemsDirectory,
            Action<FenceInfo> persist,
            out string error)
        {
            EnsureCollections(fenceInfo);
            DesktopShortcutInfo record = FindRecord(fenceInfo, entryPath);
            if (record == null)
            {
                error = "找不到该桌面快捷方式的恢复记录。";
                return false;
            }
            if (!ValidateRecordLocation(record, itemsDirectory, out error))
                return false;

            bool previousKeepTracked = record.KeepTracked;
            record.KeepTracked = false;
            try
            {
                persist(fenceInfo);
            }
            catch (Exception ex)
            {
                record.KeepTracked = previousKeepTracked;
                error = "无法保存快捷方式恢复事务：" + ex.Message;
                return false;
            }

            if (!EnsureRestored(fenceInfo, record, persist, out error))
                return false;

            RemoveTracking(fenceInfo, record, persist);
            error = null;
            return true;
        }

        /// <summary>
        /// 删除栅栏前恢复并解除跟踪其中的全部桌面快捷方式。
        /// 任一文件发生路径冲突时保留元数据与托管文件，不覆盖用户文件。
        /// </summary>
        public bool TryReleaseAll(
            FenceInfo fenceInfo,
            string itemsDirectory,
            Action<FenceInfo> persist,
            out string error)
        {
            EnsureCollections(fenceInfo);
            RemoveNullRecords(fenceInfo, persist);
            var errors = new List<string>();

            var validRecords = new List<DesktopShortcutInfo>();
            var changedRecords = new List<DesktopShortcutInfo>();
            foreach (DesktopShortcutInfo record in fenceInfo.DesktopShortcuts.ToArray())
            {
                string validationError;
                if (!ValidateRecordLocation(record, itemsDirectory, out validationError))
                {
                    errors.Add(FormatError(record, validationError));
                    continue;
                }

                validRecords.Add(record);
                if (record.KeepTracked)
                {
                    record.KeepTracked = false;
                    changedRecords.Add(record);
                }
            }
            if (changedRecords.Count > 0)
            {
                try
                {
                    persist(fenceInfo);
                }
                catch (Exception ex)
                {
                    foreach (DesktopShortcutInfo record in changedRecords)
                        record.KeepTracked = true;
                    errors.Add("无法保存批量恢复事务：" + ex.Message);
                    error = JoinErrors(errors);
                    return false;
                }
            }

            foreach (DesktopShortcutInfo record in validRecords)
            {
                string itemError;
                if (EnsureRestored(fenceInfo, record, persist, out itemError))
                    RemoveTracking(fenceInfo, record, persist);
                else
                    errors.Add(FormatError(record, itemError));
            }

            error = JoinErrors(errors);
            return errors.Count == 0;
        }

        /// <summary>
        /// 原生 Shell 菜单可能直接重命名、移动或删除托管文件。菜单关闭后根据每条
        /// 记录独占的 GUID 目录重新识别文件，并同步路径或解除已经离开的记录。
        /// </summary>
        public bool ReconcileAfterShellCommand(
            FenceInfo fenceInfo,
            string entryPath,
            string itemsDirectory,
            Action<FenceInfo> persist,
            out string error)
        {
            EnsureCollections(fenceInfo);
            DesktopShortcutInfo record = FindRecord(fenceInfo, entryPath);
            if (record == null || record.State != DesktopShortcutState.Managed)
            {
                error = null;
                return true;
            }
            if (!ValidateRecordLocation(record, itemsDirectory, out error))
                return false;

            if (File.Exists(record.ManagedPath))
            {
                error = null;
                return true;
            }

            string managedDirectory = Path.GetDirectoryName(record.ManagedPath);
            try
            {
                string[] remainingFiles = !string.IsNullOrWhiteSpace(managedDirectory) &&
                                          Directory.Exists(managedDirectory)
                    ? Directory.GetFiles(managedDirectory, "*", SearchOption.TopDirectoryOnly)
                    : new string[0];

                if (remainingFiles.Length == 0)
                {
                    // 文件可能已通过“剪切”回到桌面，也可能被删除或移到其他位置。
                    // 两种情况都应遵循 Shell 原始语义并解除跟踪。
                    RemoveTracking(fenceInfo, record, persist);
                    error = null;
                    return true;
                }

                if (remainingFiles.Length != 1)
                {
                    error = "托管目录中出现多个文件，无法确定重命名后的快捷方式。";
                    return false;
                }

                string oldOriginalPath = record.OriginalPath;
                string oldManagedPath = record.ManagedPath;
                var oldFiles = new List<string>(fenceInfo.Files);
                string newManagedPath = NormalizePath(remainingFiles[0]);
                string originalDirectory = Path.GetDirectoryName(oldOriginalPath);
                string newOriginalPath = Path.Combine(
                    originalDirectory,
                    Path.GetFileName(newManagedPath));

                record.ManagedPath = newManagedPath;
                record.OriginalPath = newOriginalPath;
                ReplacePathsAfterShellRename(
                    fenceInfo.Files,
                    oldOriginalPath,
                    oldManagedPath,
                    newManagedPath);
                try
                {
                    persist(fenceInfo);
                }
                catch (Exception persistError)
                {
                    string rollbackError = null;
                    try
                    {
                        if (File.Exists(newManagedPath) && !File.Exists(oldManagedPath))
                            File.Move(newManagedPath, oldManagedPath);
                        if (!File.Exists(oldManagedPath) || File.Exists(newManagedPath))
                            throw new IOException("重命名后的文件未能恢复到原托管路径。");
                        record.ManagedPath = oldManagedPath;
                        record.OriginalPath = oldOriginalPath;
                        fenceInfo.Files = oldFiles;
                    }
                    catch (Exception ex)
                    {
                        rollbackError = ex.Message;
                    }

                    error = "无法保存重命名后的托管记录：" + persistError.Message;
                    if (!string.IsNullOrWhiteSpace(rollbackError))
                    {
                        error += Environment.NewLine +
                            "同时无法撤销文件重命名：" + rollbackError;
                    }
                    return false;
                }
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = "无法同步原生菜单操作：" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 使指定记录最终处于托管状态。文件同时存在时仅在内容完全相同的情况下
        /// 删除桌面副本；不同内容视为冲突并保留两份文件。
        /// </summary>
        private static bool EnsureManaged(
            FenceInfo fenceInfo,
            DesktopShortcutInfo record,
            Action<FenceInfo> persist,
            out string error)
        {
            if (!ValidateRecord(record, out error))
                return false;

            bool originalExists = File.Exists(record.OriginalPath);
            bool managedExists = File.Exists(record.ManagedPath);

            if (originalExists && managedExists)
            {
                if (!FilesAreEquivalent(record.OriginalPath, record.ManagedPath))
                {
                    error = "桌面原路径与托管路径均存在且内容不同，已保留两份文件。";
                    return false;
                }

                try
                {
                    DeleteFileClearingReadOnly(record.OriginalPath);
                    DesktopUtil.NotifyShellItemDeleted(record.OriginalPath);
                }
                catch (Exception ex)
                {
                    error = "无法移除桌面上的重复快捷方式：" + ex.Message;
                    return false;
                }

                return FinalizeManaged(fenceInfo, record, persist, out error);
            }

            if (managedExists)
                return FinalizeManaged(fenceInfo, record, persist, out error);

            if (!originalExists)
            {
                error = "原桌面路径和托管路径中的文件都不存在。";
                return false;
            }

            Point position;
            if (DesktopUtil.TryGetDesktopItemPosition(record.OriginalPath, out position))
            {
                record.PositionX = position.X;
                record.PositionY = position.Y;
                record.HasPosition = true;
            }

            DesktopShortcutState previousState = record.State;
            record.State = DesktopShortcutState.MovingToStorage;
            try
            {
                persist(fenceInfo);
            }
            catch (Exception ex)
            {
                record.State = previousState;
                error = "无法保存移入托管目录的事务：" + ex.Message;
                return false;
            }

            if (!TryTransferFile(record.OriginalPath, record.ManagedPath, out error))
                return false;

            DesktopUtil.NotifyShellItemDeleted(record.OriginalPath);
            return FinalizeManaged(fenceInfo, record, persist, out error);
        }

        /// <summary>
        /// 完成托管状态的内存与 XML 更新，并把栅栏渲染路径切换到托管文件。
        /// </summary>
        private static bool FinalizeManaged(
            FenceInfo fenceInfo,
            DesktopShortcutInfo record,
            Action<FenceInfo> persist,
            out string error)
        {
            if (!File.Exists(record.ManagedPath))
            {
                error = "托管文件不存在，无法完成接管。";
                return false;
            }

            record.State = DesktopShortcutState.Managed;
            ReplaceEntryPath(fenceInfo.Files, record, record.ManagedPath);
            try
            {
                persist(fenceInfo);
            }
            catch (Exception ex)
            {
                error = "快捷方式已移入托管目录，但无法保存完成状态：" + ex.Message;
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>
        /// 使指定记录最终处于桌面恢复状态。同名目标已存在时绝不覆盖，
        /// 仅允许清理内容完全相同的搬移中间副本。
        /// </summary>
        private static bool EnsureRestored(
            FenceInfo fenceInfo,
            DesktopShortcutInfo record,
            Action<FenceInfo> persist,
            out string error)
        {
            if (!ValidateRecord(record, out error))
                return false;

            bool originalExists = File.Exists(record.OriginalPath);
            bool managedExists = File.Exists(record.ManagedPath);

            if (originalExists && managedExists)
            {
                if (!FilesAreEquivalent(record.OriginalPath, record.ManagedPath))
                {
                    error = "桌面原路径已有不同内容的同名文件，未执行覆盖。";
                    return false;
                }

                try
                {
                    DeleteFileClearingReadOnly(record.ManagedPath);
                }
                catch (Exception ex)
                {
                    error = "无法清理托管目录中的重复快捷方式：" + ex.Message;
                    return false;
                }

                return FinalizeRestored(fenceInfo, record, persist, out error);
            }

            if (originalExists)
                return FinalizeRestored(fenceInfo, record, persist, out error);

            if (!managedExists)
            {
                error = "原桌面路径和托管路径中的文件都不存在。";
                return false;
            }

            DesktopShortcutState previousState = record.State;
            record.State = DesktopShortcutState.MovingToDesktop;
            try
            {
                persist(fenceInfo);
            }
            catch (Exception ex)
            {
                record.State = previousState;
                error = "无法保存恢复到桌面的事务：" + ex.Message;
                return false;
            }

            string originalRestoreError;
            if (!TryTransferFile(
                record.ManagedPath,
                record.OriginalPath,
                out originalRestoreError))
            {
                string fallbackPath;
                if (!TryGetUserDesktopFallbackPath(record.OriginalPath, out fallbackPath))
                {
                    error = originalRestoreError;
                    return false;
                }

                // 先持久化回退目标，再搬移文件。若进程在搬移后异常退出，下一次
                // 启动仍能从 MovingToDesktop 状态继续完成，而不会遗失实际目标。
                string previousOriginalPath = record.OriginalPath;
                record.OriginalPath = fallbackPath;
                try
                {
                    persist(fenceInfo);
                }
                catch (Exception ex)
                {
                    record.OriginalPath = previousOriginalPath;
                    error = originalRestoreError + Environment.NewLine +
                            "无法保存当前用户桌面回退事务：" + ex.Message;
                    return false;
                }

                string fallbackError;
                if (!TryTransferFile(record.ManagedPath, fallbackPath, out fallbackError))
                {
                    record.OriginalPath = previousOriginalPath;
                    try
                    {
                        persist(fenceInfo);
                    }
                    catch (Exception ex)
                    {
                        fallbackError += Environment.NewLine +
                            "无法回滚回退目标记录：" + ex.Message;
                    }

                    error = originalRestoreError + Environment.NewLine +
                            "恢复到当前用户桌面也失败：" + fallbackError;
                    return false;
                }
            }

            return FinalizeRestored(fenceInfo, record, persist, out error);
        }

        /// <summary>
        /// 完成恢复状态的内存与 XML 更新，通知 Explorer 刷新并尽力恢复原坐标。
        /// 自动排列或显示器布局变化时最终坐标仍由 Explorer 决定。
        /// </summary>
        private static bool FinalizeRestored(
            FenceInfo fenceInfo,
            DesktopShortcutInfo record,
            Action<FenceInfo> persist,
            out string error)
        {
            if (!File.Exists(record.OriginalPath))
            {
                error = "桌面文件不存在，无法完成恢复。";
                return false;
            }

            record.State = DesktopShortcutState.Restored;
            ReplaceEntryPath(fenceInfo.Files, record, record.OriginalPath);
            try
            {
                persist(fenceInfo);
            }
            catch (Exception ex)
            {
                error = "快捷方式已恢复到桌面，但无法保存完成状态：" + ex.Message;
                return false;
            }

            DesktopUtil.NotifyShellItemCreated(record.OriginalPath);
            if (record.HasPosition)
            {
                DesktopUtil.TrySetDesktopItemPosition(
                    record.OriginalPath,
                    new Point(record.PositionX, record.PositionY));
            }

            error = null;
            return true;
        }

        /// <summary>
        /// 在不覆盖目标的前提下搬移文件。同卷优先使用原子移动，
        /// 跨卷时回退到复制、校验、删除，并在删除源文件失败时撤销副本。
        /// </summary>
        private static bool TryTransferFile(string sourcePath, string destinationPath, out string error)
        {
            error = null;
            if (!File.Exists(sourcePath))
            {
                error = "源文件不存在：" + sourcePath;
                return false;
            }
            if (File.Exists(destinationPath))
            {
                error = "目标路径已经存在，未执行覆盖：" + destinationPath;
                return false;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                File.Move(sourcePath, destinationPath);
                return true;
            }
            catch (Exception moveException)
                when (moveException is IOException ||
                      moveException is UnauthorizedAccessException)
            {
                if (File.Exists(destinationPath) && !File.Exists(sourcePath))
                    return true;
                if (File.Exists(destinationPath))
                {
                    error = "移动目标已经存在：" + destinationPath;
                    return false;
                }

                try
                {
                    FileAttributes attributes = File.GetAttributes(sourcePath);
                    DateTime creationTime = File.GetCreationTimeUtc(sourcePath);
                    DateTime writeTime = File.GetLastWriteTimeUtc(sourcePath);

                    File.Copy(sourcePath, destinationPath, false);
                    if (!FilesAreEquivalent(sourcePath, destinationPath))
                        throw new IOException("复制后的文件校验失败。", moveException);

                    TryApplyFileMetadata(destinationPath, attributes, creationTime, writeTime);
                    try
                    {
                        DeleteFileClearingReadOnly(sourcePath);
                    }
                    catch
                    {
                        TryDeleteFile(destinationPath);
                        throw;
                    }

                    return true;
                }
                catch (Exception copyException)
                {
                    error = "无法搬移快捷方式：" + copyException.Message;
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = "无法搬移快捷方式：" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 公共桌面写入受限时返回当前用户桌面的等价恢复路径。
        /// 仅改变目录并保留原文件名；目标冲突仍交给搬移逻辑拒绝覆盖。
        /// </summary>
        private static bool TryGetUserDesktopFallbackPath(
            string originalPath,
            out string fallbackPath)
        {
            fallbackPath = null;
            if (string.IsNullOrWhiteSpace(originalPath))
                return false;

            string originalDirectory = Path.GetDirectoryName(NormalizePath(originalPath));
            string commonDesktop = Environment.GetFolderPath(
                Environment.SpecialFolder.CommonDesktopDirectory);
            if (!PathEquals(originalDirectory, commonDesktop))
                return false;

            string userDesktop = Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(userDesktop) ||
                PathEquals(userDesktop, commonDesktop))
            {
                return false;
            }

            fallbackPath = Path.Combine(userDesktop, Path.GetFileName(originalPath));
            return true;
        }

        /// <summary>搬移回退失败时尽力恢复源文件原有属性。</summary>
        private static void TryRestoreSourceAttributes(
            string sourcePath,
            FileAttributes attributes)
        {
            try
            {
                if (File.Exists(sourcePath))
                    File.SetAttributes(sourcePath, attributes);
            }
            catch
            {
            }
        }

        /// <summary>
        /// 删除文件前临时清除只读位；删除失败时恢复原属性并把异常交给调用方处理。
        /// </summary>
        private static void DeleteFileClearingReadOnly(string path)
        {
            FileAttributes attributes = File.GetAttributes(path);
            try
            {
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(
                        path,
                        attributes & ~FileAttributes.ReadOnly);
                }
                File.Delete(path);
            }
            catch
            {
                TryRestoreSourceAttributes(path, attributes);
                throw;
            }
        }

        /// <summary>尽力把原文件的基础属性和时间戳应用到跨卷副本。</summary>
        private static void TryApplyFileMetadata(
            string path,
            FileAttributes attributes,
            DateTime creationTime,
            DateTime writeTime)
        {
            try { File.SetCreationTimeUtc(path, creationTime); } catch { }
            try { File.SetLastWriteTimeUtc(path, writeTime); } catch { }
            try { File.SetAttributes(path, attributes); } catch { }
        }

        /// <summary>比较两个文件的长度与 SHA-256，确认是否为同一份搬移内容。</summary>
        private static bool FilesAreEquivalent(string firstPath, string secondPath)
        {
            try
            {
                var firstInfo = new FileInfo(firstPath);
                var secondInfo = new FileInfo(secondPath);
                if (firstInfo.Length != secondInfo.Length)
                    return false;

                using (var algorithm = SHA256.Create())
                using (var firstStream = File.OpenRead(firstPath))
                using (var secondStream = File.OpenRead(secondPath))
                {
                    byte[] firstHash = algorithm.ComputeHash(firstStream);
                    byte[] secondHash = algorithm.ComputeHash(secondStream);
                    return firstHash.SequenceEqual(secondHash);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>从栅栏文件列表和恢复列表中彻底移除已恢复的跟踪记录。</summary>
        private static void RemoveTracking(
            FenceInfo fenceInfo,
            DesktopShortcutInfo record,
            Action<FenceInfo> persist)
        {
            var previousFiles = new List<string>(fenceInfo.Files);
            int recordIndex = fenceInfo.DesktopShortcuts.IndexOf(record);
            RemovePaths(fenceInfo.Files, record.OriginalPath, record.ManagedPath);
            fenceInfo.DesktopShortcuts.Remove(record);
            try
            {
                persist(fenceInfo);
            }
            catch
            {
                fenceInfo.Files = previousFiles;
                if (recordIndex >= 0 && !fenceInfo.DesktopShortcuts.Contains(record))
                {
                    fenceInfo.DesktopShortcuts.Insert(
                        Math.Min(recordIndex, fenceInfo.DesktopShortcuts.Count),
                        record);
                }
                throw;
            }
            TryDeleteEmptyItemDirectory(record.ManagedPath);
        }

        /// <summary>把同一记录可能出现的原路径或托管路径统一替换为当前活动路径。</summary>
        private static void ReplaceEntryPath(
            List<string> files,
            DesktopShortcutInfo record,
            string activePath)
        {
            int insertionIndex = files.Count;
            for (int index = files.Count - 1; index >= 0; index--)
            {
                if (PathEquals(files[index], record.OriginalPath) ||
                    PathEquals(files[index], record.ManagedPath))
                {
                    insertionIndex = Math.Min(insertionIndex, index);
                    files.RemoveAt(index);
                }
            }

            files.Insert(Math.Min(insertionIndex, files.Count), activePath);
        }

        /// <summary>把 Shell 重命名前的原路径或托管路径替换为新的托管路径。</summary>
        private static void ReplacePathsAfterShellRename(
            List<string> files,
            string oldOriginalPath,
            string oldManagedPath,
            string newManagedPath)
        {
            int insertionIndex = files.Count;
            for (int index = files.Count - 1; index >= 0; index--)
            {
                if (PathEquals(files[index], oldOriginalPath) ||
                    PathEquals(files[index], oldManagedPath))
                {
                    insertionIndex = Math.Min(insertionIndex, index);
                    files.RemoveAt(index);
                }
            }
            files.Insert(Math.Min(insertionIndex, files.Count), newManagedPath);
        }

        /// <summary>删除文件列表中与任一路径匹配的所有项。</summary>
        private static void RemovePaths(List<string> files, params string[] paths)
        {
            for (int index = files.Count - 1; index >= 0; index--)
            {
                if (paths.Any(path => PathEquals(files[index], path)))
                    files.RemoveAt(index);
            }
        }

        /// <summary>查找与原路径或托管路径匹配的桌面快捷方式记录。</summary>
        private static DesktopShortcutInfo FindRecord(FenceInfo fenceInfo, string path)
        {
            return fenceInfo.DesktopShortcuts.FirstOrDefault(record =>
                record != null &&
                (PathEquals(record.OriginalPath, path) || PathEquals(record.ManagedPath, path)));
        }

        /// <summary>确保旧版或手工编辑的 XML 不会留下空集合。</summary>
        private static void EnsureCollections(FenceInfo fenceInfo)
        {
            if (fenceInfo.Files == null)
                fenceInfo.Files = new List<string>();
            if (fenceInfo.DesktopShortcuts == null)
                fenceInfo.DesktopShortcuts = new List<DesktopShortcutInfo>();
        }

        /// <summary>移除异常或手工编辑 XML 产生的空记录，并立即持久化修复结果。</summary>
        private static void RemoveNullRecords(
            FenceInfo fenceInfo,
            Action<FenceInfo> persist)
        {
            if (fenceInfo.DesktopShortcuts.RemoveAll(record => record == null) > 0)
                persist(fenceInfo);
        }

        /// <summary>验证恢复记录包含有效的原路径和托管路径。</summary>
        private static bool ValidateRecord(DesktopShortcutInfo record, out string error)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.OriginalPath) ||
                string.IsNullOrWhiteSpace(record.ManagedPath))
            {
                error = "恢复记录缺少必要的文件路径。";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// 验证恢复记录的原路径仍位于用户/公共桌面，托管路径仍位于当前分区中
        /// 与记录 ID 对应的专属目录，防止损坏 XML 把文件操作指向任意位置。
        /// </summary>
        private static bool ValidateRecordLocation(
            DesktopShortcutInfo record,
            string itemsDirectory,
            out string error)
        {
            if (!ValidateRecord(record, out error))
                return false;
            if (record.Id == Guid.Empty)
            {
                error = "恢复记录缺少有效的唯一标识。";
                return false;
            }

            try
            {
                string expectedManagedDirectory = Path.Combine(
                    Path.GetFullPath(itemsDirectory),
                    record.Id.ToString("N"));
                string actualManagedDirectory = Path.GetDirectoryName(
                    Path.GetFullPath(record.ManagedPath));
                if (!PathEquals(actualManagedDirectory, expectedManagedDirectory))
                {
                    error = "托管路径已越出当前分区的专属目录，已拒绝文件操作。";
                    return false;
                }

                string originalDirectory = Path.GetDirectoryName(
                    Path.GetFullPath(record.OriginalPath));
                string userDesktop = Environment.GetFolderPath(
                    Environment.SpecialFolder.DesktopDirectory);
                string publicDesktop = Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonDesktopDirectory);
                if (!PathEquals(originalDirectory, userDesktop) &&
                    !PathEquals(originalDirectory, publicDesktop))
                {
                    error = "原始路径已不在用户桌面或公共桌面，已拒绝文件操作。";
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = "恢复记录包含无效路径：" + ex.Message;
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>判断列表是否已包含指定路径，Windows 文件路径比较不区分大小写。</summary>
        private static bool ContainsPath(IEnumerable<string> paths, string path)
        {
            return paths.Any(candidate => PathEquals(candidate, path));
        }

        /// <summary>将路径标准化为绝对路径，失败时保留输入文本用于安全比较。</summary>
        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            try
            {
                return Path.GetFullPath(path).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        /// <summary>按 Windows 路径语义比较两个路径。</summary>
        private static bool PathEquals(string firstPath, string secondPath)
        {
            if (string.IsNullOrWhiteSpace(firstPath) || string.IsNullOrWhiteSpace(secondPath))
                return false;

            return string.Equals(
                NormalizePath(firstPath),
                NormalizePath(secondPath),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>给单条恢复错误附加用户可识别的快捷方式名称。</summary>
        private static string FormatError(DesktopShortcutInfo record, string error)
        {
            string name = record == null || string.IsNullOrWhiteSpace(record.OriginalPath)
                ? "未知快捷方式"
                : Path.GetFileName(record.OriginalPath);
            return name + "：" + error;
        }

        /// <summary>把多个恢复错误合并为适合消息框显示的文本。</summary>
        private static string JoinErrors(IEnumerable<string> errors)
        {
            return string.Join(Environment.NewLine, errors.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        /// <summary>尽力删除本次操作创建但未能使用的文件副本。</summary>
        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    DeleteFileClearingReadOnly(path);
            }
            catch
            {
            }
        }

        /// <summary>恢复完成后清理记录专属的空托管目录，不递归删除任何内容。</summary>
        private static void TryDeleteEmptyItemDirectory(string managedPath)
        {
            try
            {
                string directory = Path.GetDirectoryName(managedPath);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) &&
                    !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory, false);
                }
            }
            catch
            {
            }
        }
    }
}

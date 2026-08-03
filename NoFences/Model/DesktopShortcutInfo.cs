using System;

namespace NoFences.Model
{
    /// <summary>
    /// 桌面快捷方式在栅栏与桌面之间搬移时的持久化状态。
    /// 状态会先于文件操作写盘，以便进程异常结束后继续完成未完成的搬移。
    /// </summary>
    public enum DesktopShortcutState
    {
        /// <summary>快捷方式当前位于原桌面路径。</summary>
        Restored,

        /// <summary>快捷方式正在从桌面搬入应用托管目录。</summary>
        MovingToStorage,

        /// <summary>快捷方式当前位于应用托管目录。</summary>
        Managed,

        /// <summary>快捷方式正在从应用托管目录恢复到桌面。</summary>
        MovingToDesktop
    }

    /// <summary>
    /// 记录一个由 NoFences 托管的桌面快捷方式。
    /// 原路径、托管路径和桌面坐标均通过 XmlSerializer 持久化。
    /// </summary>
    public class DesktopShortcutInfo
    {
        /// <summary>记录唯一标识，用于创建互不冲突的托管子目录。</summary>
        public Guid Id { get; set; }

        /// <summary>快捷方式被接管前在桌面上的完整路径。</summary>
        public string OriginalPath { get; set; }

        /// <summary>快捷方式运行期间在 NoFences 数据目录中的完整路径。</summary>
        public string ManagedPath { get; set; }

        /// <summary>原桌面图标左上角的 X 坐标（Shell 文件夹视图坐标）。</summary>
        public int PositionX { get; set; }

        /// <summary>原桌面图标左上角的 Y 坐标（Shell 文件夹视图坐标）。</summary>
        public int PositionY { get; set; }

        /// <summary>是否成功读取过原桌面图标位置。</summary>
        public bool HasPosition { get; set; }

        /// <summary>当前或上一次持久化的文件搬移状态。</summary>
        public DesktopShortcutState State { get; set; }

        /// <summary>
        /// 是否继续由栅栏跟踪。移除条目或删除栅栏时先设为 false，
        /// 异常退出后下次启动会优先完成恢复而不会再次隐藏图标。
        /// </summary>
        public bool KeepTracked { get; set; } = true;

        /// <summary>供 XmlSerializer 使用的无参数构造函数。</summary>
        public DesktopShortcutInfo()
        {
        }
    }
}

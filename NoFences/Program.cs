using NoFences.Model;
using System;
using System.Threading;
using System.Windows.Forms;
using NoFences.Theming;

namespace NoFences
{
    /// <summary>
    /// 应用程序入口点。通过命名 Mutex 确保单实例运行，
    /// 加载栅栏数据并启动 WinForms 消息循环。
    /// </summary>
    static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {
            // 在创建任何窗口前加载主题；同时设置原生菜单的明暗模式。
            ThemeManager.Initialize();

            // 通过命名 Mutex 确保单实例运行
            using (var mutex = new Mutex(true, "No_fences", out var createdNew))
            {
                if (createdNew)
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.ApplicationExit += Application_ApplicationExit;

                    // 从 %LocalAppData%/NoFences/ 加载已有栅栏
                    string startupWarning = FenceManager.Instance.LoadFences();
                    // 首次运行：创建默认栅栏
                    if (Application.OpenForms.Count == 0)
                        FenceManager.Instance.CreateFence("First fence");

                    if (!string.IsNullOrWhiteSpace(startupWarning))
                    {
                        MessageBox.Show(
                            "Some desktop shortcut operations could not be completed. No files were overwritten.\n\n" + startupWarning,
                            "NoFences startup",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }

                    // 进入 WinForms 消息循环（无主窗体，由各 FenceWindow 自行驱动）
                    Application.Run();
                }
            }
        }

        /// <summary>
        /// 为非菜单触发的正常退出提供最后一道恢复保障。
        /// 错误写入跟踪日志；搬移事务仍保存在 XML 中供下次启动继续处理。
        /// </summary>
        private static void Application_ApplicationExit(object sender, EventArgs e)
        {
            string error;
            if (!FenceManager.Instance.TryRestoreDesktopShortcutsForExit(out error))
                System.Diagnostics.Trace.WriteLine("Desktop shortcut restore failed: " + error);
        }

    }
}

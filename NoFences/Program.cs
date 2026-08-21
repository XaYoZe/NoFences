using NoFences.Model;
using System;
using System.Threading;
using System.Windows.Forms;
using NoFences.Theming;
using System.Linq;

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
        static void Main(string[] args)
        {
            bool restoreOnly = args.Any(argument => string.Equals(
                argument,
                "--restore-shortcuts",
                StringComparison.OrdinalIgnoreCase));
            bool silent = args.Any(argument => string.Equals(
                argument,
                "--silent",
                StringComparison.OrdinalIgnoreCase));

            if (restoreOnly)
            {
                RunShortcutRecovery(silent);
                return;
            }

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
                        FenceManager.Instance.CreateFence(ThemeText.DefaultFenceName);

                    if (!string.IsNullOrWhiteSpace(startupWarning))
                    {
                        MessageBox.Show(
                            ThemeText.StartupWarning + Environment.NewLine + Environment.NewLine + startupWarning,
                            ThemeText.StartupTitle,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }

                    // 进入 WinForms 消息循环（无主窗体，由各 FenceWindow 自行驱动）
                    Application.Run();
                }
            }
        }

        /// <summary>执行无界面的永久快捷方式恢复，供卸载程序和故障恢复调用。</summary>
        private static void RunShortcutRecovery(bool silent)
        {
            using (var mutex = new Mutex(true, "No_fences", out var createdNew))
            {
                if (!createdNew)
                {
                    Environment.ExitCode = 2;
                    if (!silent)
                    {
                        MessageBox.Show(
                            ThemeText.RecoveryRunning,
                            ThemeText.RecoveryTitle,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                    return;
                }

                string error;
                if (FenceManager.Instance.TryReleaseAllStoredDesktopShortcuts(out error))
                {
                    Environment.ExitCode = 0;
                    if (!silent)
                    {
                        MessageBox.Show(
                            ThemeText.RecoveryCompleted,
                            ThemeText.RecoveryTitle,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    return;
                }

                Environment.ExitCode = 1;
                System.Diagnostics.Trace.WriteLine("Desktop shortcut recovery failed: " + error);
                if (!silent)
                {
                    MessageBox.Show(
                        ThemeText.RecoveryFailed + Environment.NewLine + Environment.NewLine + error,
                        ThemeText.RecoveryTitle,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
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

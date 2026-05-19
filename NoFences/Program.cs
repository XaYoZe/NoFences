using NoFences.Model;
using System;
using System.Threading;
using System.Windows.Forms;
using NoFences.Win32;

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
            // 设置上下文菜单为深色模式（继承系统主题设置）
            WindowUtil.SetPreferredAppMode(1);

            // 通过命名 Mutex 确保单实例运行
            using (var mutex = new Mutex(true, "No_fences", out var createdNew))
            {
                if (createdNew)
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);

                    // 从 %LocalAppData%/NoFences/ 加载已有栅栏
                    FenceManager.Instance.LoadFences();
                    // 首次运行：创建默认栅栏
                    if (Application.OpenForms.Count == 0)
                        FenceManager.Instance.CreateFence("First fence");

                    // 进入 WinForms 消息循环（无主窗体，由各 FenceWindow 自行驱动）
                    Application.Run();
                }
            }
        }

    }
}

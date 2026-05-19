using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NoFences.Win32
{
    /// <summary>
    /// 窗口投影工具类。通过 DWM API 为无边框窗口添加原生阴影。
    /// </summary>
    public class DropShadow
    {
        #region Shadowing

        #region Fields

        private const int WM_NCHITTEST = 0x84;
        private const int WS_MINIMIZEBOX = 0x20000;
        private const int HTCLIENT = 0x1;
        private const int HTCAPTION = 0x2;
        private const int CS_DBLCLKS = 0x8;
        private const int CS_DROPSHADOW = 0x00020000;
        private const int WM_NCPAINT = 0x0085;
        private const int WM_ACTIVATEAPP = 0x001C;

        #endregion

        #region Structures

        /// <summary>DWM 扩展边距结构体。</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public struct MARGINS
        {
            public int leftWidth;
            public int rightWidth;
            public int topHeight;
            public int bottomHeight;
        }

        #endregion

        #region Methods

        #region Public

        /// <summary>将 DWM 窗框扩展到工作区（用于创建阴影效果）。</summary>
        [DllImport("dwmapi.dll")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

        /// <summary>设置 DWM 窗口属性。</summary>
        [DllImport("dwmapi.dll")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        /// <summary>查询 DWM 合成状态是否启用。</summary>
        [DllImport("dwmapi.dll")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static extern int DwmIsCompositionEnabled(ref int pfEnabled);

        /// <summary>检查 DWM 合成是否可用（Vista+）。</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static bool IsCompositionEnabled()
        {
            if (Environment.OSVersion.Version.Major < 6) return false;

            bool enabled;
            DwmIsCompositionEnabled(out enabled);

            return enabled;
        }

        #endregion

        #region Private

        [DllImport("dwmapi.dll")]
        private static extern int DwmIsCompositionEnabled(out bool enabled);

        /// <summary>创建圆角矩形区域句柄（GDI）。</summary>
        [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        private static extern IntPtr CreateRoundRectRgn
        (
            int nLeftRect,
            int nTopRect,
            int nRightRect,
            int nBottomRect,
            int nWidthEllipse,
            int nHeightEllipse
         );

        /// <summary>检查 Aero 是否启用（Vista/7）。</summary>
        private bool CheckIfAeroIsEnabled()
        {
            if (Environment.OSVersion.Version.Major >= 6)
            {
                int enabled = 0;
                DwmIsCompositionEnabled(ref enabled);

                return (enabled == 1) ? true : false;
            }
            return false;
        }

        #endregion

        #region Overrides

        /// <summary>
        /// 为 WinForms 窗体应用 DWM 原生阴影效果。
        /// 通过 DwmExtendFrameIntoClientArea 和 DwmSetWindowAttribute 实现。
        /// </summary>
        public static void ApplyShadows(Form form)
        {
            var v = 2;

            // 设置窗口为非工作区渲染策略
            DwmSetWindowAttribute(form.Handle, 2, ref v, 4);

            MARGINS margins = new MARGINS()
            {
                bottomHeight = 0,
                leftWidth = 0,
                rightWidth = 0,
                topHeight = 1   // 顶部扩展 1 像素以触发 DWM 阴影
            };

            DwmExtendFrameIntoClientArea(form.Handle, ref margins);
        }

        #endregion

        #endregion

        #endregion
    }
}

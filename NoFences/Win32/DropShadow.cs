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
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWCP_DONOTROUND = 1;
        private const int DWMWCP_ROUND = 2;
        private const int DWM_COLOR_DEFAULT = unchecked((int)0xFFFFFFFF);
        private const int DWM_COLOR_NONE = unchecked((int)0xFFFFFFFE);

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
            SetShadow(form, true);
        }

        /// <summary>
        /// 启用或关闭 DWM 原生投影。使用 WinForms Region 的自定义圆角必须
        /// 关闭该投影；DWM 原生圆角则可以安全保留由系统同步裁剪的投影。
        /// </summary>
        public static void SetShadow(Form form, bool enabled)
        {
            if (form == null || !form.IsHandleCreated || !IsCompositionEnabled())
                return;

            var margins = new MARGINS
            {
                bottomHeight = 0,
                leftWidth = 0,
                rightWidth = 0,
                topHeight = enabled ? 1 : 0
            };
            DwmExtendFrameIntoClientArea(form.Handle, ref margins);

            // 2 = DWMNCRP_ENABLED，1 = DWMNCRP_DISABLED。
            int renderingPolicy = enabled ? 2 : 1;
            DwmSetWindowAttribute(form.Handle, 2, ref renderingPolicy, 4);
        }

        /// <summary>
        /// 在 Windows 11 22000+ 请求 DWM 原生顶层窗口圆角。原生圆角会统一
        /// 裁剪窗口内容、Acrylic 合成表面和拖动快照；旧系统返回 false。
        /// </summary>
        public static bool TrySetNativeCorners(Form form, bool rounded)
        {
            if (form == null || !form.IsHandleCreated ||
                !IsCompositionEnabled() || !SupportsNativeCorners())
            {
                return false;
            }

            int preference = rounded ? DWMWCP_ROUND : DWMWCP_DONOTROUND;
            if (DwmSetWindowAttribute(
                form.Handle,
                DWMWA_WINDOW_CORNER_PREFERENCE,
                ref preference,
                4) != 0)
            {
                return false;
            }

            // 应用自行绘制主题边框，关闭 DWM 的额外 1px 系统边框；恢复直角
            // 主题时同时恢复系统默认值。
            int borderColor = rounded ? DWM_COLOR_NONE : DWM_COLOR_DEFAULT;
            DwmSetWindowAttribute(
                form.Handle,
                DWMWA_BORDER_COLOR,
                ref borderColor,
                4);
            return true;
        }

        /// <summary>判断当前系统是否提供 Windows 11 原生窗口圆角属性。</summary>
        private static bool SupportsNativeCorners()
        {
            Version version = Environment.OSVersion.Version;
            return version.Major >= 10 && version.Build >= 22000;
        }

        #endregion

        #endregion

        #endregion
    }
}

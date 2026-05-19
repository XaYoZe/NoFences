using System;
using System.Runtime.InteropServices;

namespace NoFences.Win32
{
    public class WindowUtil
    {
        public const int WM_NCHITTEST = 0x84;          // variables for dragging the form
        public const int HTCLIENT = 0x1;
        public const int HTCAPTION = 0x2;
        public const int HTLEFT = 10;
        public const int HTRIGHT = 11;
        public const int HTTOP = 12;
        public const int HTTOPLEFT = 13;
        public const int HTTOPRIGHT = 14;
        public const int HTBOTTOM = 15;
        public const int HTBOTTOMLEFT = 16;
        public const int HTBOTTOMRIGHT = 17;

        public const int WM_SYSCOMMAND = 274;
        public const int SC_MAXIMIZE = 0xF030;
        public const int SC_MINIMIZE = 0xF020;

        public const UInt32 SWP_NOSIZE = 0x0001;
        public const UInt32 SWP_NOMOVE = 0x0002;
        public const UInt32 SWP_NOACTIVATE = 0x0010;
        public const UInt32 SWP_NOZORDER = 0x0004;
        public const int WM_ACTIVATEAPP = 0x001C;
        public const int WM_ACTIVATE = 0x0006;
        public const int WM_SETFOCUS = 0x0007;
        public const int WM_SETTINGCHANGE = 0x001A;  // 系统设置变化广播（主题/字体等）
        public const int WM_DPICHANGED = 0x02E0;     // 当前窗口 DPI 变化通知
        public const int WM_SIZING = 0x0214;          // 窗口拖动调整大小中
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public const int WM_WINDOWPOSCHANGING = 0x0046;

        // WM_SIZING 时 wParam 指示正在拖拽的边框/角
        public const int WMSZ_LEFT = 1;
        public const int WMSZ_RIGHT = 2;
        public const int WMSZ_TOP = 3;
        public const int WMSZ_TOPLEFT = 4;
        public const int WMSZ_TOPRIGHT = 5;
        public const int WMSZ_BOTTOM = 6;
        public const int WMSZ_BOTTOMLEFT = 7;
        public const int WMSZ_BOTTOMRIGHT = 8;

        public const uint SPI_GETICONMETRICS = 0x002D; // SystemParametersInfo 获取桌面图标度量

        /// <summary>逻辑字体结构体，对应 Win32 LOGFONTW</summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct LOGFONT
        {
            public int lfHeight;         // 字符高度（逻辑单位）
            public int lfWidth;          // 平均字符宽度
            public int lfEscapement;     // 文本输出角度
            public int lfOrientation;    // 字符基线角度
            public int lfWeight;         // 字重（400=常规，700=粗体）
            public byte lfItalic;        // 斜体
            public byte lfUnderline;     // 下划线
            public byte lfStrikeOut;     // 删除线
            public byte lfCharSet;       // 字符集
            public byte lfOutPrecision;  // 输出精度
            public byte lfClipPrecision; // 裁剪精度
            public byte lfQuality;       // 输出质量
            public byte lfPitchAndFamily;// 字距和字体族
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string lfFaceName;    // 字体名称（如 "Segoe UI"）
        }

        /// <summary>桌面图标度量结构体，对应 Win32 ICONMETRICSW</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ICONMETRICS
        {
            public uint cbSize;          // 结构体大小（调用前必须设置）
            public int iHorzSpacing;     // 图标水平间距（逻辑像素）
            public int iVertSpacing;     // 图标垂直间距（逻辑像素）
            public int iTitleWrap;       // 图标标题是否换行
            public LOGFONT lfFont;       // 图标文字字体
        }

        /// <summary>Win32 RECT 结构体，用于 WM_SIZING 消息的边界矩形。</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref ICONMETRICS pvParam, uint fWinIni);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateFontIndirect(ref LOGFONT lf);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X,
           int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")]
        public static extern IntPtr DeferWindowPos(IntPtr hWinPosInfo, IntPtr hWnd,
           IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")]
        public static extern IntPtr BeginDeferWindowPos(int nNumWindows);
        [DllImport("user32.dll")]
        public static extern bool EndDeferWindowPos(IntPtr hWinPosInfo);

        [DllImport("user32.dll")]
        public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        /// <summary>获取指定窗口的当前 DPI（Windows 10 1607+）。不受 WinForms 缓存影响，可获取实时 DPI。</summary>
        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hWnd);
        #region 窗口样式与属性操作

        /// <summary>扩展窗口样式枚举。</summary>
        [Flags]
        public enum ExtendedWindowStyles
        {
            WS_EX_TOOLWINDOW = 0x00000080, // 工具窗口：从 Alt+Tab 和任务栏中隐藏
        }

        /// <summary>GetWindowLong 索引枚举。</summary>
        public enum GetWindowLongFields
        {
            GWL_EXSTYLE = (-20), // 扩展窗口样式
        }

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowLong(IntPtr hWnd, int nIndex);

        /// <summary>
        /// 设置窗口属性。自动根据进程位数（32/64）选择 SetWindowLong 或 SetWindowLongPtr。
        /// Win32 的 SetWindowLong 成功时不清理错误码，需要手动处理。
        /// </summary>
        public static IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            int error = 0;
            IntPtr result = IntPtr.Zero;
            // Win32 SetWindowLong 成功时不清理错误码，需手动置零
            SetLastError(0);

            if (IntPtr.Size == 4)
            {
                // 32 位进程：使用 SetWindowLong
                Int32 tempResult = IntSetWindowLong(hWnd, nIndex, IntPtrToInt32(dwNewLong));
                error = Marshal.GetLastWin32Error();
                result = new IntPtr(tempResult);
            }
            else
            {
                // 64 位进程：使用 SetWindowLongPtr
                result = IntSetWindowLongPtr(hWnd, nIndex, dwNewLong);
                error = Marshal.GetLastWin32Error();
            }

            if ((result == IntPtr.Zero) && (error != 0))
            {
                throw new System.ComponentModel.Win32Exception(error);
            }

            return result;
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr IntSetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern Int32 IntSetWindowLong(IntPtr hWnd, int nIndex, Int32 dwNewLong);

        private static int IntPtrToInt32(IntPtr intPtr)
        {
            return unchecked((int)intPtr.ToInt64());
        }

        [DllImport("kernel32.dll", EntryPoint = "SetLastError")]
        public static extern void SetLastError(int dwErrorCode);
        #endregion

        /// <summary>
        /// 设置上下文菜单的视觉主题模式。
        /// 1 = 允许深色模式（继承系统设置），2 = 强制深色模式。
        /// </summary>
        [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int SetPreferredAppMode(int preferredAppMode);

        /// <summary>
        /// 将窗口从 Alt+Tab 切换列表中隐藏。
        /// 通过添加 WS_EX_TOOLWINDOW 扩展样式实现。
        /// </summary>
        public static void HideFromAltTab(IntPtr Handle)
        {
            SetWindowPos(Handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
            int exStyle = (int)GetWindowLong(Handle, (int)GetWindowLongFields.GWL_EXSTYLE);
            exStyle |= (int)ExtendedWindowStyles.WS_EX_TOOLWINDOW;
            SetWindowLong(Handle, (int)GetWindowLongFields.GWL_EXSTYLE, (IntPtr)exStyle);
        }
    }
}

using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace NoFences.Win32
{
    /// <summary>
    /// 桌面窗口操作工具类。
    /// 负责将栅栏窗口粘附到桌面 Progman 窗口并防止最小化。
    /// </summary>
    public class DesktopUtil
    {
        private const Int32 GWL_STYLE = -16;
        private const Int32 GWL_HWNDPARENT = -8;
        private const Int32 WS_MAXIMIZEBOX = 0x00010000;
        private const Int32 WS_MINIMIZEBOX = 0x00020000;
        private const int SWC_DESKTOP = 8;
        private const int SWFO_NEEDDISPATCH = 1;
        private const int QUERY_SERVICE_VTABLE_INDEX = 3;
        private const int GET_VIEW_MODE_AND_ICON_SIZE_VTABLE_INDEX = 36;

        private static readonly Guid CLSID_ShellWindows = new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
        private static readonly Guid IID_IServiceProvider = new Guid("6D5140C1-7436-11CE-8034-00AA006009FA");
        private static readonly Guid SID_SFolderView = new Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
        private static readonly Guid IID_IFolderView2 = new Guid("1AF3A467-214F-4298-908E-06B03E0B39F9");

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueryServiceDelegate(
            IntPtr serviceProvider,
            ref Guid serviceId,
            ref Guid interfaceId,
            out IntPtr service);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetViewModeAndIconSizeDelegate(
            IntPtr folderView,
            out int viewMode,
            out int iconSize);

        [DllImport("User32.dll", EntryPoint = "GetWindowLong")]
        private extern static Int32 GetWindowLongPtr(IntPtr hWnd, Int32 nIndex);

        [DllImport("User32.dll", EntryPoint = "SetWindowLong")]
        private extern static Int32 SetWindowLongPtr(IntPtr hWnd, Int32 nIndex, Int32 dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpWindowClass, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        /// <summary>
        /// 读取桌面 Shell 视图当前实际使用的图标边长（96 DPI 逻辑像素）。
        /// 通过 IFolderView2.GetViewModeAndIconSize 获取当前 Ctrl+滚轮缩放级别，
        /// 避免跨进程使用 Explorer 的 HIMAGELIST 句柄。
        /// </summary>
        /// <returns>桌面图标逻辑边长，读取失败时返回 -1</returns>
        public static int GetDesktopIconSize()
        {
            object shellWindows = null;
            object desktopWindow = null;
            IntPtr desktopWindowUnknown = IntPtr.Zero;
            IntPtr serviceProvider = IntPtr.Zero;
            IntPtr folderView = IntPtr.Zero;

            try
            {
                var shellWindowsType = Type.GetTypeFromCLSID(CLSID_ShellWindows);
                if (shellWindowsType == null)
                    return -1;

                shellWindows = Activator.CreateInstance(shellWindowsType);
                var findWindowArguments = new object[]
                {
                    0,
                    0,
                    SWC_DESKTOP,
                    0,
                    SWFO_NEEDDISPATCH
                };
                desktopWindow = shellWindows.GetType().InvokeMember(
                    "FindWindowSW",
                    BindingFlags.InvokeMethod,
                    null,
                    shellWindows,
                    findWindowArguments);
                if (desktopWindow == null)
                    return -1;

                desktopWindowUnknown = Marshal.GetIUnknownForObject(desktopWindow);
                var serviceProviderId = IID_IServiceProvider;
                if (Marshal.QueryInterface(
                    desktopWindowUnknown,
                    ref serviceProviderId,
                    out serviceProvider) < 0)
                {
                    return -1;
                }

                var queryService = GetComMethod<QueryServiceDelegate>(
                    serviceProvider,
                    QUERY_SERVICE_VTABLE_INDEX);
                var folderViewServiceId = SID_SFolderView;
                var folderViewInterfaceId = IID_IFolderView2;
                if (queryService(
                    serviceProvider,
                    ref folderViewServiceId,
                    ref folderViewInterfaceId,
                    out folderView) < 0)
                {
                    return -1;
                }

                var getViewModeAndIconSize = GetComMethod<GetViewModeAndIconSizeDelegate>(
                    folderView,
                    GET_VIEW_MODE_AND_ICON_SIZE_VTABLE_INDEX);
                int viewMode;
                int iconSize;
                return getViewModeAndIconSize(folderView, out viewMode, out iconSize) >= 0 && iconSize > 0
                    ? iconSize
                    : -1;
            }
            catch
            {
                return -1;
            }
            finally
            {
                if (folderView != IntPtr.Zero)
                    Marshal.Release(folderView);
                if (serviceProvider != IntPtr.Zero)
                    Marshal.Release(serviceProvider);
                if (desktopWindowUnknown != IntPtr.Zero)
                    Marshal.Release(desktopWindowUnknown);
                ReleaseComObject(desktopWindow);
                ReleaseComObject(shellWindows);
            }
        }

        /// <summary>
        /// 从 COM 接口虚表读取指定方法并转换为托管委托。
        /// </summary>
        private static T GetComMethod<T>(IntPtr interfacePointer, int methodIndex)
            where T : class
        {
            var virtualTable = Marshal.ReadIntPtr(interfacePointer);
            var methodPointer = Marshal.ReadIntPtr(virtualTable, methodIndex * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer(methodPointer, typeof(T)) as T;
        }

        /// <summary>
        /// 安全释放本方法创建的 COM 运行时包装对象。
        /// Explorer 重启导致对象提前断开时忽略释放异常。
        /// </summary>
        private static void ReleaseComObject(object comObject)
        {
            if (comObject == null || !Marshal.IsComObject(comObject))
                return;

            try
            {
                Marshal.FinalReleaseComObject(comObject);
            }
            catch (InvalidComObjectException)
            {
            }
        }

        /// <summary>
        /// 移除窗口的最小化和最大化按钮样式位，
        /// 防止栅栏窗口被意外最小化。
        /// </summary>
        public static void PreventMinimize(IntPtr handle)
        {
            Int32 windowStyle = GetWindowLongPtr(handle, GWL_STYLE);
            SetWindowLongPtr(handle, GWL_STYLE, windowStyle & ~WS_MAXIMIZEBOX & ~WS_MINIMIZEBOX);
        }

        /// <summary>
        /// 将窗口粘附到桌面 Progman 窗口上，
        /// 使其随桌面一起显示/隐藏（Win+D 等）。
        /// </summary>
        public static void GlueToDesktop(IntPtr handle)
        {
            IntPtr nWinHandle = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", null);
            SetWindowLongPtr(handle, GWL_HWNDPARENT, nWinHandle.ToInt32());
           
        }
    }
}

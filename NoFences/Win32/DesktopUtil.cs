using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Text;
using System.Threading;

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
        private const int QUERY_ACTIVE_SHELL_VIEW_VTABLE_INDEX = 15;
        private const int FOLDER_VIEW_GET_FOLDER_VTABLE_INDEX = 5;
        private const int FOLDER_VIEW_ITEM_VTABLE_INDEX = 6;
        private const int FOLDER_VIEW_ITEM_COUNT_VTABLE_INDEX = 7;
        private const int FOLDER_VIEW_GET_ITEM_POSITION_VTABLE_INDEX = 11;
        private const int FOLDER_VIEW_SELECT_AND_POSITION_ITEMS_VTABLE_INDEX = 16;
        private const int GET_VIEW_MODE_AND_ICON_SIZE_VTABLE_INDEX = 36;
        private const int SHELL_FOLDER_GET_DISPLAY_NAME_OF_VTABLE_INDEX = 11;
        private const uint SVGIO_ALLVIEW = 0x00000002;
        private const uint SHGDN_FORPARSING = 0x00008000;
        private const uint SVSI_POSITIONITEM = 0x00000080;
        private const uint SHCNE_CREATE = 0x00000002;
        private const uint SHCNE_DELETE = 0x00000004;
        private const uint SHCNF_PATHW = 0x00000005;
        private const uint SHCNF_FLUSH = 0x00001000;
        private const int STRRET_BUFFER_SIZE = 520;
        private const int MAX_SHELL_PATH = 32768;

        private static readonly Guid CLSID_ShellWindows = new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
        private static readonly Guid IID_IServiceProvider = new Guid("6D5140C1-7436-11CE-8034-00AA006009FA");
        private static readonly Guid SID_SFolderView = new Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
        private static readonly Guid SID_STopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
        private static readonly Guid IID_IShellBrowser = new Guid("000214E2-0000-0000-C000-000000000046");
        private static readonly Guid IID_IFolderView2 = new Guid("1AF3A467-214F-4298-908E-06B03E0B39F9");
        private static readonly Guid IID_IShellFolder = new Guid("000214E6-0000-0000-C000-000000000046");

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

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueryActiveShellViewDelegate(
            IntPtr shellBrowser,
            out IntPtr shellView);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetFolderDelegate(
            IntPtr folderView,
            ref Guid interfaceId,
            out IntPtr folder);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ItemDelegate(
            IntPtr folderView,
            int itemIndex,
            out IntPtr itemIdList);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ItemCountDelegate(
            IntPtr folderView,
            uint flags,
            out int itemCount);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetItemPositionDelegate(
            IntPtr folderView,
            IntPtr itemIdList,
            out NativePoint position);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SelectAndPositionItemsDelegate(
            IntPtr folderView,
            uint itemCount,
            IntPtr itemIdLists,
            IntPtr positions,
            uint flags);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDisplayNameOfDelegate(
            IntPtr shellFolder,
            IntPtr itemIdList,
            uint flags,
            IntPtr stringResult);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;

            /// <summary>从托管 Point 创建原生 POINT 结构。</summary>
            public NativePoint(Point point)
            {
                X = point.X;
                Y = point.Y;
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpWindowClass, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrRetToBufW(
            IntPtr stringResult,
            IntPtr itemIdList,
            StringBuilder output,
            uint outputLength);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(
            uint eventId,
            uint flags,
            [MarshalAs(UnmanagedType.LPWStr)] string item1,
            IntPtr item2);

        /// <summary>
        /// 读取桌面 Shell 视图当前实际使用的图标边长（96 DPI 逻辑像素）。
        /// 通过 IFolderView2.GetViewModeAndIconSize 获取当前 Ctrl+滚轮缩放级别，
        /// 避免跨进程使用 Explorer 的 HIMAGELIST 句柄。
        /// </summary>
        /// <returns>桌面图标逻辑边长，读取失败时返回 -1</returns>
        public static int GetDesktopIconSize()
        {
            DesktopFolderViewContext context;
            if (!TryOpenDesktopFolderView(out context))
                return -1;

            using (context)
            {
                try
                {
                    var getViewModeAndIconSize = GetComMethod<GetViewModeAndIconSizeDelegate>(
                        context.FolderView,
                        GET_VIEW_MODE_AND_ICON_SIZE_VTABLE_INDEX);
                    int viewMode;
                    int iconSize;
                    return getViewModeAndIconSize(
                        context.FolderView,
                        out viewMode,
                        out iconSize) >= 0 && iconSize > 0
                            ? iconSize
                            : -1;
                }
                catch
                {
                    return -1;
                }
            }
        }

        /// <summary>
        /// 读取指定桌面文件在 Shell 文件夹视图中的左上角坐标。
        /// 使用视图返回的子 PIDL，避免按显示名称猜测图标索引。
        /// </summary>
        public static bool TryGetDesktopItemPosition(string path, out Point position)
        {
            position = Point.Empty;
            DesktopFolderViewContext context;
            if (!TryOpenDesktopFolderView(out context))
                return false;

            using (context)
            {
                IntPtr itemIdList;
                if (!TryFindDesktopItem(context.FolderView, path, out itemIdList))
                    return false;

                try
                {
                    var getItemPosition = GetComMethod<GetItemPositionDelegate>(
                        context.FolderView,
                        FOLDER_VIEW_GET_ITEM_POSITION_VTABLE_INDEX);
                    NativePoint nativePosition;
                    if (getItemPosition(context.FolderView, itemIdList, out nativePosition) < 0)
                        return false;

                    position = new Point(nativePosition.X, nativePosition.Y);
                    return true;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(itemIdList);
                }
            }
        }

        /// <summary>
        /// 将指定桌面文件恢复到保存的 Shell 文件夹视图坐标。
        /// Explorer 接收文件创建通知后可能短暂无法枚举新项，因此最多重试约一秒。
        /// </summary>
        public static bool TrySetDesktopItemPosition(string path, Point position)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                if (TrySetDesktopItemPositionOnce(path, position))
                    return true;

                if (attempt < 19)
                    Thread.Sleep(50);
            }

            return false;
        }

        /// <summary>通知 Shell 一个桌面文件已经创建，并同步刷新相关视图。</summary>
        public static void NotifyShellItemCreated(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                SHChangeNotify(SHCNE_CREATE, SHCNF_PATHW | SHCNF_FLUSH, path, IntPtr.Zero);
        }

        /// <summary>通知 Shell 一个桌面文件已经移除，并同步刷新相关视图。</summary>
        public static void NotifyShellItemDeleted(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                SHChangeNotify(SHCNE_DELETE, SHCNF_PATHW | SHCNF_FLUSH, path, IntPtr.Zero);
        }

        /// <summary>执行一次桌面图标定位，不包含 Explorer 刷新等待。</summary>
        private static bool TrySetDesktopItemPositionOnce(string path, Point position)
        {
            DesktopFolderViewContext context;
            if (!TryOpenDesktopFolderView(out context))
                return false;

            using (context)
            {
                IntPtr itemIdList;
                if (!TryFindDesktopItem(context.FolderView, path, out itemIdList))
                    return false;

                IntPtr itemIdListArray = IntPtr.Zero;
                IntPtr positionArray = IntPtr.Zero;
                try
                {
                    itemIdListArray = Marshal.AllocHGlobal(IntPtr.Size);
                    Marshal.WriteIntPtr(itemIdListArray, itemIdList);

                    var nativePosition = new NativePoint(position);
                    positionArray = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativePoint)));
                    Marshal.StructureToPtr(nativePosition, positionArray, false);

                    var selectAndPosition = GetComMethod<SelectAndPositionItemsDelegate>(
                        context.FolderView,
                        FOLDER_VIEW_SELECT_AND_POSITION_ITEMS_VTABLE_INDEX);
                    return selectAndPosition(
                        context.FolderView,
                        1,
                        itemIdListArray,
                        positionArray,
                        SVSI_POSITIONITEM) >= 0;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    if (positionArray != IntPtr.Zero)
                        Marshal.FreeHGlobal(positionArray);
                    if (itemIdListArray != IntPtr.Zero)
                        Marshal.FreeHGlobal(itemIdListArray);
                    Marshal.FreeCoTaskMem(itemIdList);
                }
            }
        }

        /// <summary>
        /// 枚举桌面视图中的子 PIDL，并按 Shell 返回的解析路径匹配指定文件。
        /// 返回的 PIDL 由调用方使用 CoTaskMemFree 释放。
        /// </summary>
        private static bool TryFindDesktopItem(
            IntPtr folderView,
            string path,
            out IntPtr matchingItemIdList)
        {
            matchingItemIdList = IntPtr.Zero;
            IntPtr shellFolder = IntPtr.Zero;

            try
            {
                var getFolder = GetComMethod<GetFolderDelegate>(
                    folderView,
                    FOLDER_VIEW_GET_FOLDER_VTABLE_INDEX);
                Guid shellFolderInterfaceId = IID_IShellFolder;
                if (getFolder(folderView, ref shellFolderInterfaceId, out shellFolder) < 0 ||
                    shellFolder == IntPtr.Zero)
                {
                    return false;
                }

                var itemCount = GetComMethod<ItemCountDelegate>(
                    folderView,
                    FOLDER_VIEW_ITEM_COUNT_VTABLE_INDEX);
                int count;
                if (itemCount(folderView, SVGIO_ALLVIEW, out count) < 0)
                    return false;

                var item = GetComMethod<ItemDelegate>(
                    folderView,
                    FOLDER_VIEW_ITEM_VTABLE_INDEX);
                for (int index = 0; index < count; index++)
                {
                    IntPtr itemIdList = IntPtr.Zero;
                    try
                    {
                        if (item(folderView, index, out itemIdList) < 0 ||
                            itemIdList == IntPtr.Zero)
                        {
                            continue;
                        }

                        string parsingPath;
                        if (TryGetParsingPath(shellFolder, itemIdList, out parsingPath) &&
                            PathsEqual(parsingPath, path))
                        {
                            matchingItemIdList = itemIdList;
                            itemIdList = IntPtr.Zero;
                            return true;
                        }
                    }
                    finally
                    {
                        if (itemIdList != IntPtr.Zero)
                            Marshal.FreeCoTaskMem(itemIdList);
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                ReleaseInterface(ref shellFolder);
            }
        }

        /// <summary>通过 IShellFolder.GetDisplayNameOf 获取子 PIDL 对应的完整解析路径。</summary>
        private static bool TryGetParsingPath(
            IntPtr shellFolder,
            IntPtr itemIdList,
            out string path)
        {
            path = null;
            IntPtr stringResult = Marshal.AllocCoTaskMem(STRRET_BUFFER_SIZE);
            try
            {
                var getDisplayName = GetComMethod<GetDisplayNameOfDelegate>(
                    shellFolder,
                    SHELL_FOLDER_GET_DISPLAY_NAME_OF_VTABLE_INDEX);
                if (getDisplayName(
                    shellFolder,
                    itemIdList,
                    SHGDN_FORPARSING,
                    stringResult) < 0)
                {
                    return false;
                }

                var output = new StringBuilder(MAX_SHELL_PATH);
                if (StrRetToBufW(
                    stringResult,
                    itemIdList,
                    output,
                    (uint)output.Capacity) < 0)
                {
                    return false;
                }

                path = output.ToString();
                return !string.IsNullOrWhiteSpace(path);
            }
            finally
            {
                Marshal.FreeCoTaskMem(stringResult);
            }
        }

        /// <summary>打开当前 Explorer 桌面的 IFolderView2 并持有本次调用所需的 COM 引用。</summary>
        private static bool TryOpenDesktopFolderView(out DesktopFolderViewContext context)
        {
            context = new DesktopFolderViewContext();
            try
            {
                Type shellWindowsType = Type.GetTypeFromCLSID(CLSID_ShellWindows);
                if (shellWindowsType == null)
                    throw new InvalidOperationException("ShellWindows COM 类型不可用。");

                context.ShellWindows = Activator.CreateInstance(shellWindowsType);
                var findWindowArguments = new object[]
                {
                    0,
                    0,
                    SWC_DESKTOP,
                    0,
                    SWFO_NEEDDISPATCH
                };
                context.DesktopWindow = context.ShellWindows.GetType().InvokeMember(
                    "FindWindowSW",
                    BindingFlags.InvokeMethod,
                    null,
                    context.ShellWindows,
                    findWindowArguments);
                if (context.DesktopWindow == null)
                    throw new InvalidOperationException("找不到桌面 Shell 窗口。");

                context.DesktopWindowUnknown = Marshal.GetIUnknownForObject(context.DesktopWindow);
                Guid serviceProviderId = IID_IServiceProvider;
                if (Marshal.QueryInterface(
                    context.DesktopWindowUnknown,
                    ref serviceProviderId,
                    out context.ServiceProvider) < 0)
                {
                    throw new InvalidOperationException("无法查询桌面 IServiceProvider。");
                }

                var queryService = GetComMethod<QueryServiceDelegate>(
                    context.ServiceProvider,
                    QUERY_SERVICE_VTABLE_INDEX);
                Guid browserServiceId = SID_STopLevelBrowser;
                Guid browserInterfaceId = IID_IShellBrowser;
                if (queryService(
                    context.ServiceProvider,
                    ref browserServiceId,
                    ref browserInterfaceId,
                    out context.ShellBrowser) >= 0 && context.ShellBrowser != IntPtr.Zero)
                {
                    var queryActiveShellView = GetComMethod<QueryActiveShellViewDelegate>(
                        context.ShellBrowser,
                        QUERY_ACTIVE_SHELL_VIEW_VTABLE_INDEX);
                    if (queryActiveShellView(
                        context.ShellBrowser,
                        out context.ShellView) >= 0 && context.ShellView != IntPtr.Zero)
                    {
                        Guid folderViewInterfaceId = IID_IFolderView2;
                        Marshal.QueryInterface(
                            context.ShellView,
                            ref folderViewInterfaceId,
                            out context.FolderView);
                    }
                }

                // 较旧 Shell 实现可能允许从站点直接查询视图，作为兼容回退保留。
                if (context.FolderView == IntPtr.Zero)
                {
                    Guid folderViewServiceId = SID_SFolderView;
                    Guid folderViewInterfaceId = IID_IFolderView2;
                    queryService(
                        context.ServiceProvider,
                        ref folderViewServiceId,
                        ref folderViewInterfaceId,
                        out context.FolderView);
                }

                if (context.FolderView == IntPtr.Zero)
                    throw new InvalidOperationException("无法查询桌面 IFolderView2。");

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    "Failed to open the desktop IFolderView2: " + ex);
                context.Dispose();
                context = null;
                return false;
            }
        }

        /// <summary>按 Windows 文件路径规则比较 Shell 解析路径与目标路径。</summary>
        private static bool PathsEqual(string firstPath, string secondPath)
        {
            if (string.IsNullOrWhiteSpace(firstPath) || string.IsNullOrWhiteSpace(secondPath))
                return false;

            try
            {
                firstPath = System.IO.Path.GetFullPath(firstPath).TrimEnd('\\', '/');
                secondPath = System.IO.Path.GetFullPath(secondPath).TrimEnd('\\', '/');
            }
            catch
            {
            }

            return string.Equals(firstPath, secondPath, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>持有并按正确顺序释放一次桌面文件夹视图查询创建的 COM 引用。</summary>
        private sealed class DesktopFolderViewContext : IDisposable
        {
            public object ShellWindows;
            public object DesktopWindow;
            public IntPtr DesktopWindowUnknown;
            public IntPtr ServiceProvider;
            public IntPtr ShellBrowser;
            public IntPtr ShellView;
            public IntPtr FolderView;

            /// <summary>释放原生接口指针与运行时可调用包装对象。</summary>
            public void Dispose()
            {
                ReleaseInterface(ref FolderView);
                ReleaseInterface(ref ShellView);
                ReleaseInterface(ref ShellBrowser);
                ReleaseInterface(ref ServiceProvider);
                ReleaseInterface(ref DesktopWindowUnknown);

                ReleaseComObject(DesktopWindow);
                DesktopWindow = null;
                ReleaseComObject(ShellWindows);
                ShellWindows = null;
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
        /// 安全释放原生 COM 接口指针，并先清空字段避免异常路径重复释放。
        /// Explorer 重启导致接口提前断开时忽略释放异常。
        /// </summary>
        private static void ReleaseInterface(ref IntPtr interfacePointer)
        {
            IntPtr pointer = interfacePointer;
            interfacePointer = IntPtr.Zero;
            if (pointer == IntPtr.Zero)
                return;

            try
            {
                Marshal.Release(pointer);
            }
            catch
            {
            }
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
            catch (COMException)
            {
            }
        }

        /// <summary>
        /// 移除窗口的最小化和最大化按钮样式位，
        /// 防止栅栏窗口被意外最小化。
        /// </summary>
        public static void PreventMinimize(IntPtr handle)
        {
            long windowStyle = WindowUtil.GetWindowLong(handle, GWL_STYLE).ToInt64();
            WindowUtil.SetWindowLong(
                handle,
                GWL_STYLE,
                new IntPtr(windowStyle & ~WS_MAXIMIZEBOX & ~WS_MINIMIZEBOX));
        }

        /// <summary>
        /// 将窗口粘附到桌面 Progman 窗口上，
        /// 使其随桌面一起显示/隐藏（Win+D 等）。
        /// </summary>
        public static void GlueToDesktop(IntPtr handle)
        {
            IntPtr nWinHandle = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", null);
            if (nWinHandle == IntPtr.Zero)
                throw new InvalidOperationException("找不到桌面 Progman 窗口。");
            WindowUtil.SetWindowLong(handle, GWL_HWNDPARENT, nWinHandle);
           
        }
    }
}
